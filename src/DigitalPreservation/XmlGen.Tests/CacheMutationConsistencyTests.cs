using System.Xml;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using DigitalPreservation.XmlGen.Mets;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XmlGen.Tests;

/// <summary>
/// The path cache must stay truthful under mutation even when a document's metadata
/// disagrees with its tree structure (edited or third-party METS): a mutation that creates
/// a duplicate-path state must RECORD it (in PathDiagnostics, which is what every trust
/// gate tests) rather than
/// silently overwrite a cache entry, and a delete in a flagged document must refresh the
/// diagnostics so a healed document regains the trusted-cache fast path.
/// </summary>
public class CacheMutationConsistencyTests
{
    private readonly MetsManager metsManager;

    private const string TestDigest = "eb634d64ce8e6be5195174ceaef9ac9e19c37119f3b31618630aa633ccdbf68f";

    public CacheMutationConsistencyTests()
    {
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var factory = serviceProvider.GetService<ILoggerFactory>();
        var parser = new MetsParser(new FileSystemMetsLoader(), factory!.CreateLogger<MetsParser>());
        var metsStorage = new FileSystemMetsStorage(parser);
        var metadataManager = new MetadataManager(
            new PremisManager(), new PremisManagerExif(), new PremisEventManagerVirus());
        metsManager = new MetsManager(parser, metsStorage, metadataManager);
    }

    private async Task<FullMets> CreateAndLoadStandardMets(string outputFileName)
    {
        var fi = new FileInfo($"Outputs/{outputFileName}");
        var uri = new Uri(fi.FullName);
        var createResult = await metsManager.CreateStandardMets(uri, "Cache Consistency Test METS");
        createResult.Success.Should().BeTrue(createResult.ErrorMessage ?? "");
        var loadResult = await metsManager.GetFullMets(uri, createResult.Value!.ETag);
        loadResult.Success.Should().BeTrue(loadResult.ErrorMessage ?? "");
        return loadResult.Value!;
    }

    private static WorkingFile SimpleFile(string localPath, string name) =>
        new()
        {
            LocalPath = localPath,
            Name = name,
            ContentType = "application/pdf",
            Digest = TestDigest,
            Size = 54321,
            Modified = DateTime.UtcNow
        };

    private static void RewriteOriginalName(FullMets fullMets, DivType div, string newPath)
    {
        var amdSec = IdRefs.ResolveSingle(div.Admid,
            id => fullMets.Mets.AmdSec.FirstOrDefault(a => a.Id == id))!;
        var premisXml = (XmlElement)amdSec.TechMd.First().MdWrap!.XmlData.Any.First();
        var originalName = premisXml
            .GetElementsByTagName("originalName", XNames.premis.NamespaceName)
            .OfType<XmlElement>()
            .First();
        originalName.InnerText = newPath;
    }

    [Fact]
    public async Task Adding_At_A_Path_Claimed_By_A_Foreign_Div_Records_A_Duplicate()
    {
        var fullMets = await CreateAndLoadStandardMets("cache-foreign-claim.xml");
        var physRoot = fullMets.Mets.StructMap.Single(sm => sm.Type == Constants.Physical).Div!;
        var metadataDiv = physRoot.Div.Single(d => d.Label == FolderNames.Metadata);
        var adHocDiv = metadataDiv.Div.Single();

        // A div under metadata/ whose premis:originalName claims a path under objects/ -
        // resolvable and unique at (re)load, so the document carries no diagnostics and the
        // cache trusts the foreign claim.
        RewriteOriginalName(fullMets, adHocDiv, "objects/clash.pdf");
        MetsCache.Populate(fullMets).Should().BeEmpty();
        fullMets.PhysicalDivsByPath["objects/clash.pdf"].Should().BeSameAs(adHocDiv);

        // Adding a real file at the claimed path succeeds, and the resulting duplicate-path
        // state is RECORDED rather than silently overwriting the foreign div's cache entry.
        var firstAdd = metsManager.AddToMets(fullMets, SimpleFile("objects/clash.pdf", "clash.pdf"));
        firstAdd.Success.Should().BeTrue(firstAdd.ErrorMessage ?? "");
        fullMets.PathDiagnostics.Should().Contain(d => d.Contains("objects/clash.pdf"));

        // The next mutation must not trip the cache-consistency assertion (it previously
        // threw InvalidOperationException in DEBUG builds).
        var secondAdd = metsManager.AddToMets(fullMets, SimpleFile("objects/after.pdf", "after.pdf"));
        secondAdd.Success.Should().BeTrue(secondAdd.ErrorMessage ?? "");
    }

    [Fact]
    public async Task Deleting_The_Contested_File_In_A_Duplicate_Path_Document_Heals_It()
    {
        var fullMets = await CreateAndLoadStandardMets("cache-duplicate-heal.xml");
        var physRoot = fullMets.Mets.StructMap.Single(sm => sm.Type == Constants.Physical).Div!;
        var metadataDiv = physRoot.Div.Single(d => d.Label == FolderNames.Metadata);
        var adHocDiv = metadataDiv.Div.Single();

        RewriteOriginalName(fullMets, adHocDiv, "objects/clash.pdf");
        MetsCache.Populate(fullMets);
        metsManager.AddToMets(fullMets, SimpleFile("objects/clash.pdf", "clash.pdf"))
            .Success.Should().BeTrue();
        fullMets.PathDiagnostics.Should().Contain(d => d.Contains("objects/clash.pdf"));

        // Deleting the real file leaves only the foreign claim - the rebuild on delete must
        // reflect that: the duplicate diagnostic is gone and the claim owns the path again.
        var delete = metsManager.DeleteFromMets(fullMets, "objects/clash.pdf");

        delete.Success.Should().BeTrue(delete.ErrorMessage ?? "");
        fullMets.PathDiagnostics.Should().BeEmpty();
        fullMets.PhysicalDivsByPath["objects/clash.pdf"].Should().BeSameAs(adHocDiv);
    }

    [Fact]
    public async Task Deleting_The_Div_A_Diagnostic_Describes_Clears_The_Diagnostic()
    {
        var fullMets = await CreateAndLoadStandardMets("cache-ghost-cleanup.xml");
        var addDir = metsManager.AddToMets(fullMets, new WorkingDirectory
        {
            LocalPath = "objects/ghost",
            Name = "ghost",
            Modified = DateTime.UtcNow
        });
        addDir.Success.Should().BeTrue(addDir.ErrorMessage ?? "");

        // Break the div: no ADMID means no resolvable path, so a rebuild records a
        // diagnostic and the trusted-cache fast path is off for the whole document.
        var ghostDiv = fullMets.PhysicalDivsByPath["objects/ghost"];
        ghostDiv.Admid.Clear();
        MetsCache.Populate(fullMets).Should().NotBeEmpty();

        // Deletion is how broken content gets cleaned up - and a successful cleanup must
        // take its diagnostic with it, restoring the trusted-cache fast path.
        var delete = metsManager.DeleteFromMets(fullMets, "objects/ghost");

        delete.Success.Should().BeTrue(delete.ErrorMessage ?? "");
        fullMets.PathDiagnostics.Should().BeEmpty();
        fullMets.PhysicalDivsByPath.Should().NotContainKey("objects/ghost");
    }
}
