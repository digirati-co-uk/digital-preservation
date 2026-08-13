using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common.Mets;

namespace XmlGen.Tests;

/// <summary>
/// The merge gate for issue #188: whatever the platform writes, every xs:ID in it must be a
/// valid NCName and unique, and every reference to an ID must resolve. Nothing else in the
/// suite checks this — XmlSerializer does not enforce xs:ID — which is how #188 survived from
/// the beginning.
///
/// This is deliberately narrower than full METS XSD validation (which would need the schema
/// set committed and offline, and would fail on unrelated pre-existing issues): it checks the
/// ID/IDREF contract, which is what step 2 changes and what a broken change would corrupt.
///
/// DMDID is excluded from the reference check: the template writes DMDID onto the objects,
/// metadata and ad-hoc divs while their dmdSecs are only created lazily on first metadata
/// write, so a DMDID may dangle by design. That is a real (pre-existing, separate) conformance
/// gap, not something step 2 introduces.
/// </summary>
public class MetsIdIntegrityTests
{
    private readonly MetsManager metsManager;
    private readonly MetsParser parser;
    private readonly MetsFromArchivalGroup metsFromArchivalGroup;

    private static readonly XNamespace MetsNs = "http://www.loc.gov/METS/";
    private static readonly XNamespace XLinkNs = "http://www.w3.org/1999/xlink";
    private static readonly char[] XmlWhitespace = [' ', '\t', '\n', '\r'];

    private const string TestDigest = "eb634d64ce8e6be5195174ceaef9ac9e19c37119f3b31618630aa633ccdbf68f";

    public MetsIdIntegrityTests()
    {
        var serviceProvider = new ServiceCollection().AddLogging().BuildServiceProvider();
        var factory = serviceProvider.GetService<ILoggerFactory>();
        var parserLogger = factory!.CreateLogger<MetsParser>();
        var metsLoader = new FileSystemMetsLoader();
        parser = new MetsParser(metsLoader, parserLogger);
        var metsStorage = new FileSystemMetsStorage(parser);
        var premisManager = new PremisManager();
        var premisManagerExif = new PremisManagerExif();
        var premisEventManager = new PremisEventManagerVirus();
        var metadataManager = new MetadataManager(premisManager, premisManagerExif, premisEventManager);
        metsManager = new MetsManager(parser, metsStorage, metadataManager);
        metsFromArchivalGroup = new MetsFromArchivalGroup(metsManager, parser, metadataManager);
    }

    // -----------------------------------------------------------------------
    // The gate itself
    // -----------------------------------------------------------------------

    /// <param name="requireValidIds">
    /// False only for documents that legitimately contain pre-#188 raw IDs (the frozen legacy
    /// corpus). Reference integrity is still required there — that is the compatibility promise.
    /// </param>
    private static void AssertIdIntegrity(XDocument doc, bool requireValidIds = true)
    {
        var ids = doc.Descendants()
            .Select(e => (string?)e.Attribute("ID"))
            .Where(id => id != null)
            .Select(id => id!)
            .ToList();

        ids.Should().OnlyHaveUniqueItems("xs:ID values must be unique within a document");

        if (requireValidIds)
        {
            foreach (var id in ids)
            {
                var verify = () => XmlConvert.VerifyNCName(id);
                verify.Should().NotThrow($"'{id}' is written into an xs:ID attribute");
            }
        }

        var declared = ids.ToHashSet();

        foreach (var element in doc.Descendants())
        {
            // ADMID is IDREFS; FILEID is a single IDREF.
            AssertIdRefsResolve(declared, (string?)element.Attribute("ADMID"),
                $"{element.Name.LocalName}/@ADMID");

            var fileId = (string?)element.Attribute("FILEID");
            if (fileId != null)
            {
                declared.Should().Contain(fileId,
                    $"{element.Name.LocalName}/@FILEID must name a file element that exists");
            }
        }

        // structLink: the platform links FILE elements, so both ends must name real ones.
        // This is the property issue #188 step 2 could most easily have broken, by minting an
        // encoded ID for a file whose element still carries a raw one.
        foreach (var link in doc.Descendants(MetsNs + "smLink"))
        {
            foreach (var end in new[] { "from", "to" })
            {
                var value = (string?)link.Attribute(XLinkNs + end);
                if (value == null) continue;
                declared.Should().Contain(value, $"smLink/@xlink:{end} must name an element that exists");
            }
        }
    }

    private static void AssertIdRefsResolve(HashSet<string> declared, string? attributeValue, string what)
    {
        if (attributeValue == null || declared.Contains(attributeValue))
        {
            // Either absent, or a single ID — including a legacy one that contains spaces and
            // therefore looks like several tokens.
            return;
        }

        var tokens = attributeValue.Split(XmlWhitespace, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            declared.Should().Contain(token, $"{what} references '{token}'");
        }
    }

    // -----------------------------------------------------------------------
    // Documents the platform writes
    // -----------------------------------------------------------------------

    private async Task<(Uri metsUri, string eTag)> CreateEmptyMets(string outputFileName)
    {
        var uri = new Uri(new FileInfo($"Outputs/{outputFileName}").FullName);
        var result = await metsManager.CreateStandardMets(uri, "ID Integrity Test");
        result.Success.Should().BeTrue(result.ErrorMessage ?? "");
        return (uri, result.Value!.ETag!);
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
    public async Task An_Empty_Mets_Has_Valid_Unique_Resolvable_Ids()
    {
        var (metsUri, _) = await CreateEmptyMets("id-integrity-empty.xml");
        AssertIdIntegrity(XDocument.Load(metsUri.LocalPath));
    }

    [Fact]
    public async Task A_Mets_Full_Of_Awkward_Paths_Has_Valid_Unique_Resolvable_Ids()
    {
        var (metsUri, eTag) = await CreateEmptyMets("id-integrity-awkward.xml");

        // One directory and one file for each class of character that is illegal, or merely
        // suspicious, in an NCName.
        string[] directories = ["objects/my folder", "objects/A&B", "objects/2020 files"];
        foreach (var dir in directories)
        {
            var result = await metsManager.HandleCreateFolder(metsUri,
                new WorkingDirectory { LocalPath = dir, Name = LastSegment(dir), Modified = DateTime.UtcNow }, eTag);
            result.Success.Should().BeTrue(result.ErrorMessage ?? "");
            eTag = (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!;
        }

        string[] files =
        [
            "objects/my folder/my document.pdf",
            "objects/A&B/AT&T guide.pdf",
            "objects/2020 files/9 lives.pdf",
            "objects/résumé.pdf",
            "objects/report (final), v2.pdf"
        ];
        foreach (var file in files)
        {
            var result = await metsManager.HandleSingleFileUpload(metsUri,
                SimpleFile(file, LastSegment(file)), eTag);
            result.Success.Should().BeTrue(result.ErrorMessage ?? "");
            eTag = (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!;
        }

        AssertIdIntegrity(XDocument.Load(metsUri.LocalPath));
    }

    [Fact]
    public async Task A_Mets_With_A_Logical_StructMap_And_Links_Has_Resolvable_References()
    {
        var (metsUri, eTag) = await CreateEmptyMets("id-integrity-logical.xml");
        foreach (var path in new[] { "objects/page one.tif", "objects/page two.tif" })
        {
            var result = await metsManager.HandleSingleFileUpload(metsUri, SimpleFile(path, LastSegment(path)), eTag);
            result.Success.Should().BeTrue(result.ErrorMessage ?? "");
            eTag = (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!;
        }

        var fullMets = (await metsManager.GetFullMets(metsUri, eTag)).Value!;
        var setResult = metsManager.SetStructMap(fullMets, new LogicalRange
        {
            Id = "LOG_0000",
            Type = "Sequence",
            Name = "A sequence",
            Files = [new FilePointer { LocalPath = "objects/page one.tif" }],
            Ranges =
            [
                new LogicalRange
                {
                    Id = "LOG_0001", Type = "Item", Name = "Page two",
                    Files = [new FilePointer { LocalPath = "objects/page two.tif" }]
                }
            ]
        });
        setResult.Success.Should().BeTrue(setResult.ErrorMessage ?? "");

        metsManager.LinkFile(fullMets, "objects/page one.tif", "objects/page two.tif",
            new Uri("http://example.org/roles/transcription-of"));
        (await metsManager.WriteMets(fullMets)).Success.Should().BeTrue();

        AssertIdIntegrity(XDocument.Load(metsUri.LocalPath));
    }

    [Fact]
    public async Task A_Mets_Built_From_An_Archival_Group_Has_Valid_Unique_Resolvable_Ids()
    {
        var agFi = new FileInfo("Samples/archivalGroup.json");
        var agMetsFi = new FileInfo("Outputs/id-integrity-archival-group.xml");
        var archivalGroup = JsonSerializer.Deserialize<ArchivalGroup>(await File.ReadAllTextAsync(agFi.FullName))!;

        var result = await metsFromArchivalGroup.CreateStandardMets(
            new Uri(agMetsFi.FullName), archivalGroup, "ID Integrity AG");
        result.Success.Should().BeTrue(result.ErrorMessage ?? "");

        AssertIdIntegrity(XDocument.Load(agMetsFi.FullName));
    }

    // -----------------------------------------------------------------------
    // Mixed-format: a legacy document edited by the post-#188 code
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Editing_A_Legacy_Document_Leaves_Every_Reference_Resolvable()
    {
        // The compatibility promise: adding to a document whose IDs are the old raw form must
        // not leave a single dangling reference, even though the new entries carry encoded IDs.
        var metsUri = CopyFixture("path-fixture-spaces.xml", "id-integrity-legacy-mixed.xml");
        var eTag = (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!;

        var add = await metsManager.HandleSingleFileUpload(metsUri,
            SimpleFile("objects/my folder/new addition.pdf", "new addition.pdf"), eTag);
        add.Success.Should().BeTrue(add.ErrorMessage ?? "");

        eTag = (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!;
        var fullMets = (await metsManager.GetFullMets(metsUri, eTag)).Value!;
        // Link a legacy file to the newly added one: one end resolves to a raw ID, the other
        // to an encoded one.
        metsManager.LinkFile(fullMets, "objects/my file.pdf", "objects/my folder/new addition.pdf",
            new Uri("http://example.org/roles/transcription-of"));
        (await metsManager.WriteMets(fullMets)).Success.Should().BeTrue();

        var doc = XDocument.Load(metsUri.LocalPath);
        AssertIdIntegrity(doc, requireValidIds: false);

        // The legacy IDs are still there, untouched...
        var declaredIds = doc.Descendants()
            .Select(e => (string?)e.Attribute("ID")).Where(id => id != null).ToList();
        declaredIds.Should().Contain("FILE_objects/my file.pdf");
        declaredIds.Should().Contain("PHYS_objects/my folder");

        // ...and what we added is in the new, valid form.
        declaredIds.Should().Contain(MetsIds.File("objects/my folder/new addition.pdf"));
        var newIds = declaredIds.Where(id => id!.Contains("new_x0020_addition")).ToList();
        newIds.Should().NotBeEmpty("the added entry must use encoded IDs");
        foreach (var id in newIds)
        {
            var verify = () => XmlConvert.VerifyNCName(id!);
            verify.Should().NotThrow();
        }

        // The link's legacy end names the raw FILE id that exists, not a minted encoded one.
        var link = doc.Descendants(MetsNs + "smLink").Single();
        link.Attribute(XLinkNs + "from")!.Value.Should().Be("FILE_objects/my file.pdf");
        link.Attribute(XLinkNs + "to")!.Value
            .Should().Be(MetsIds.File("objects/my folder/new addition.pdf"));
    }

    private static string LastSegment(string localPath) => localPath.Split('/')[^1];

    private static Uri CopyFixture(string fixtureName, string outputName)
    {
        var source = new FileInfo($"Samples/{fixtureName}");
        var dest = new FileInfo($"Outputs/{outputName}");
        File.Copy(source.FullName, dest.FullName, overwrite: true);
        return new Uri(dest.FullName);
    }
}
