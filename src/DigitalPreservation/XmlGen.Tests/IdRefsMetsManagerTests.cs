using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XmlGen.Tests;

/// <summary>
/// MetsManager-side IDREFS handling (IdRefs): legacy space-containing IDs arrive from the
/// XmlSerializer as several tokens and must resolve via the rejoined form, while genuine
/// multi-reference IDREFS must resolve per token. Deletion is tolerant of references that
/// don't resolve. See docs/issues/188/issue-188-idrefs-plan.md.
/// </summary>
public class IdRefsMetsManagerTests
{
    private readonly MetsManager metsManager;
    private readonly FileSystemMetsStorage metsStorage;

    private const string TestDigest = "eb634d64ce8e6be5195174ceaef9ac9e19c37119f3b31618630aa633ccdbf68f";

    public IdRefsMetsManagerTests()
    {
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var factory = serviceProvider.GetService<ILoggerFactory>();
        var parserLogger = factory!.CreateLogger<MetsParser>();
        var metsLoader = new FileSystemMetsLoader();
        var parser = new MetsParser(metsLoader, parserLogger);
        metsStorage = new FileSystemMetsStorage(parser);
        var premisManager = new PremisManager();
        var premisManagerExif = new PremisManagerExif();
        var premisEventManager = new PremisEventManagerVirus();
        var metadataManager = new MetadataManager(premisManager, premisManagerExif, premisEventManager);
        metsManager = new MetsManager(parser, metsStorage, metadataManager);
    }

    private async Task<FullMets> CreateAndLoadStandardMets(string outputFileName)
    {
        var fi = new FileInfo($"Outputs/{outputFileName}");
        var uri = new Uri(fi.FullName);
        var createResult = await metsManager.CreateStandardMets(uri, "IdRefs Test METS");
        createResult.Success.Should().BeTrue(createResult.ErrorMessage ?? "");
        var loadResult = await metsManager.GetFullMets(uri, createResult.Value!.ETag);
        loadResult.Success.Should().BeTrue(loadResult.ErrorMessage ?? "");
        return loadResult.Value!;
    }

    private static Uri CopyFixture(string fixtureName, string outputName)
    {
        var source = new FileInfo($"Samples/{fixtureName}");
        var dest = new FileInfo($"Outputs/{outputName}");
        File.Copy(source.FullName, dest.FullName, overwrite: true);
        return new Uri(dest.FullName);
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

    [Fact]
    public async Task Directory_With_Genuine_Multi_Token_Admid_Resolves_Via_A_Later_Token()
    {
        var fullMets = await CreateAndLoadStandardMets("idrefs-multi-admid.xml");
        var physRoot = fullMets.Mets.StructMap.Single(sm => sm.Type == Constants.Physical).Div!;
        var objectsDiv = physRoot.Div.Single(d => d.Label == FolderNames.Objects);

        // A genuine two-reference ADMID: the first token resolves nothing, the second is the
        // real objects amdSec (carrying premis:originalName).
        objectsDiv.Admid.Insert(0, "ADM_missing");

        var diagnostics = MetsCache.Populate(fullMets);

        diagnostics.Should().BeEmpty();
        fullMets.PhysicalDivsByPath[FolderNames.Objects].Should().BeSameAs(objectsDiv);

        // And navigation-dependent editing still works
        var addFile = metsManager.AddToMets(fullMets, SimpleFile("objects/multi.pdf", "multi.pdf"));
        addFile.Success.Should().BeTrue(addFile.ErrorMessage ?? "");
    }

    [Fact]
    public async Task Deleting_A_Div_With_A_Dangling_Dmdid_Succeeds()
    {
        var fullMets = await CreateAndLoadStandardMets("idrefs-dangling-dmdid.xml");
        var addDir = metsManager.AddToMets(fullMets, new WorkingDirectory
        {
            LocalPath = "objects/sub",
            Name = "sub",
            Modified = DateTime.UtcNow
        });
        addDir.Success.Should().BeTrue(addDir.ErrorMessage ?? "");

        // DMDID references dangle by design until metadata is first set (lazy dmdSec
        // creation) - deleting such a div must not fail on the unresolvable reference.
        fullMets.PhysicalDivsByPath["objects/sub"].Dmdid.Add("DMD_dangling");

        var deleteDir = metsManager.DeleteFromMets(fullMets, "objects/sub");

        deleteDir.Success.Should().BeTrue(deleteDir.ErrorMessage ?? "");
        fullMets.PhysicalDivsByPath.Should().NotContainKey("objects/sub");
    }

    [Fact]
    public async Task Deleting_A_File_Whose_AmdSec_Is_Missing_Succeeds()
    {
        var fullMets = await CreateAndLoadStandardMets("idrefs-missing-amdsec.xml");
        var addFile = metsManager.AddToMets(fullMets, SimpleFile("objects/orphan.pdf", "orphan.pdf"));
        addFile.Success.Should().BeTrue(addFile.ErrorMessage ?? "");

        // Break the METS: remove the file's amdSec, leaving its ADMID dangling. Deletion is
        // how broken content gets cleaned up, so it must still succeed.
        var div = fullMets.PhysicalDivsByPath["objects/orphan.pdf"];
        var fileGroup = fullMets.Mets.FileSec.FileGrp.Single(fg => fg.Use == "OBJECTS");
        var file = fileGroup.File.Single(f => f.Id == div.Fptr[0].Fileid);
        var amdSec = IdRefs.ResolveSingle(file.Admid, id => fullMets.Mets.AmdSec.FirstOrDefault(a => a.Id == id));
        fullMets.Mets.AmdSec.Remove(amdSec!);

        var deleteFile = metsManager.DeleteFromMets(fullMets, "objects/orphan.pdf");

        deleteFile.Success.Should().BeTrue(deleteFile.ErrorMessage ?? "");
        fileGroup.File.Should().BeEmpty();
        fullMets.PhysicalDivsByPath.Should().NotContainKey("objects/orphan.pdf");
    }

    [Fact]
    public async Task Updating_A_Legacy_Spaced_File_Resolves_Its_AmdSec_Via_The_Rejoined_Tokens()
    {
        // The frozen fixture's file IDs contain spaces, so ADMID arrives as multiple tokens;
        // updating the file exercises MetadataManager's amdSec resolution through IdRefs.
        var metsUri = CopyFixture("path-fixture-spaces.xml", "idrefs-legacy-update.xml");
        var loadResult = await metsStorage.GetFullMets(metsUri, null);
        loadResult.Success.Should().BeTrue(loadResult.ErrorMessage ?? "");
        var fullMets = loadResult.Value!;

        var update = metsManager.AddToMets(fullMets, SimpleFile("objects/my file.pdf", "my file.pdf"));

        update.Success.Should().BeTrue(update.ErrorMessage ?? "");
    }
}
