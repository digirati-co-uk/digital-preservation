using System.Xml.Linq;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XmlGen.Tests;

/// <summary>
/// Tests for the three lower-level MetsManager members that are not exercised by
/// the async Handle* tests:
///
/// - <see cref="IMetsManager.GetStandardMets"/> — builds and returns an in-memory
///   METS object without writing it. Tested in isolation by calling the method
///   directly, writing the result via <see cref="IMetsManager.WriteMets"/>, and
///   asserting on the parsed output.
///
/// - <see cref="IMetsManager.AddToMets"/> — synchronously mutates an in-memory
///   <see cref="FullMets"/>; callers are responsible for calling WriteMets
///   afterwards. Tests verify that:
///   (1) the parser round-trip sees correct WorkingFile/Directory properties, and
///   (2) the raw XML IDs (FILE_*, PHYS_*, ADM_*, TECH_*) are self-consistent.
///
/// - <see cref="IMetsManager.DeleteFromMets"/> — symmetrical to AddToMets; the
///   file or directory must be fully removed from the structMap, the fileSec, and
///   the amdSec. Tests verify the same two levels (parser + raw XML), plus the
///   two error returns (NotFound and BadRequest).
/// </summary>
public class MetsManagerSyncTests
{
    private readonly MetsManager metsManager;
    private readonly MetsParser parser;

    private static readonly XNamespace MetsNs = "http://www.loc.gov/METS/";
    private static readonly XNamespace XLinkNs = "http://www.w3.org/1999/xlink";

    private const string TestDigest = "eb634d64ce8e6be5195174ceaef9ac9e19c37119f3b31618630aa633ccdbf68f";

    public MetsManagerSyncTests()
    {
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

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
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static WorkingFile SimpleFile(string localPath, string name) =>
        new()
        {
            LocalPath = localPath,
            Name = name,
            ContentType = "image/tiff",
            Digest = TestDigest,
            Size = 12345,
            Modified = DateTime.UtcNow
        };

    private static WorkingDirectory SimpleDirectory(string localPath, string name) =>
        new()
        {
            LocalPath = localPath,
            Name = name,
            Modified = DateTime.UtcNow
        };

    // -----------------------------------------------------------------------
    // GetStandardMets
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetStandardMets_Builds_Mets_With_Standard_Structure_Verified_By_Parser()
    {
        // GetStandardMets returns an in-memory Mets object without writing anything.
        // This test calls it directly, writes the result, then verifies the written
        // file matches the same structural invariants as CreateStandardMets.

        var metsFi = new FileInfo("Outputs/sync-getstandardmets.xml");
        var metsUri = new Uri(metsFi.FullName);

        var (file, mets) = await metsManager.GetStandardMets(metsUri, "Test AG Name");

        // The returned URI should resolve to the same file we requested.
        file.Should().Be(metsUri);

        // Write the returned mets object so it can be parsed.
        var writeResult = await metsManager.WriteMets(new FullMets { Uri = file, Mets = mets });
        writeResult.Success.Should().BeTrue();

        var parseResult = await parser.GetMetsFileWrapper(file);
        parseResult.Success.Should().BeTrue();

        var wrapper = parseResult.Value!;
        wrapper.Name.Should().Be("Test AG Name");
        wrapper.PhysicalStructure.Should().NotBeNull();
        wrapper.PhysicalStructure!.Directories.Should().HaveCount(2);
        wrapper.PhysicalStructure.Directories.Should().Contain(d => d.Name == FolderNames.Objects);
        wrapper.PhysicalStructure.Directories.Should().Contain(d => d.Name == FolderNames.Metadata);
        wrapper.PhysicalStructure.Files.Should().HaveCount(1);
        wrapper.PhysicalStructure.Files[0].Name.Should().Be("sync-getstandardmets.xml");
    }

    [Fact]
    public async Task GetStandardMets_Raw_XML_Has_Physical_StructMap_With_Expected_Root_And_Child_Divs()
    {
        // Verifies the XML structure that GetStandardMets produces at the element level:
        // one PHYSICAL structMap whose root Div contains exactly the "objects" and
        // "metadata" child Divs (identified by their canonical PHYS_* IDs).

        var metsFi = new FileInfo("Outputs/sync-getstandardmets-rawxml.xml");
        var metsUri = new Uri(metsFi.FullName);

        var (file, mets) = await metsManager.GetStandardMets(metsUri, "Raw XML Test");
        await metsManager.WriteMets(new FullMets { Uri = file, Mets = mets });

        var doc = XDocument.Load(file.LocalPath);

        // Exactly one structMap and it is PHYSICAL.
        var structMaps = doc.Descendants(MetsNs + "structMap").ToList();
        structMaps.Should().HaveCount(1);
        structMaps[0].Attribute("TYPE")!.Value.Should().Be("PHYSICAL");

        // Root div exists.
        var rootDiv = structMaps[0].Elements(MetsNs + "div").Single();
        rootDiv.Attribute("ID")!.Value.Should().Be("PHYS_ROOT");

        // Two child divs: one for objects, one for metadata.
        var childDivIds = rootDiv.Elements(MetsNs + "div")
            .Select(d => (string)d.Attribute("ID")!)
            .ToList();
        childDivIds.Should().Contain("PHYS_objects");
        childDivIds.Should().Contain("PHYS_metadata");
        childDivIds.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetStandardMets_Null_AgName_Uses_Untitled_Label()
    {
        // When agNameFromDeposit is null the METS creator falls back to "[Untitled]".

        var metsFi = new FileInfo("Outputs/sync-getstandardmets-null-name.xml");
        var metsUri = new Uri(metsFi.FullName);

        var (file, mets) = await metsManager.GetStandardMets(metsUri, null);
        await metsManager.WriteMets(new FullMets { Uri = file, Mets = mets });

        var parseResult = await parser.GetMetsFileWrapper(file);
        parseResult.Success.Should().BeTrue();
        parseResult.Value!.Name.Should().Be("[Untitled]");
    }

    // -----------------------------------------------------------------------
    // AddToMets
    // -----------------------------------------------------------------------

    [Fact]
    public async Task AddToMets_File_Round_Trips_Via_Parser_After_WriteMets()
    {
        // AddToMets modifies the in-memory FullMets synchronously. The changes must
        // survive WriteMets and be readable by MetsParser with correct properties.

        var metsFi = new FileInfo("Outputs/sync-add-file-roundtrip.xml");
        var metsUri = new Uri(metsFi.FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Add File Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;

        var addResult = metsManager.AddToMets(fullMets, SimpleFile("objects/sample.tif", "sample.tif"));
        addResult.Success.Should().BeTrue();
        await metsManager.WriteMets(fullMets);

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();

        var objectsDir = parseResult.Value!.PhysicalStructure!.Directories
            .Single(d => d.Name == FolderNames.Objects);
        objectsDir.Files.Should().HaveCount(1);
        var file = objectsDir.Files[0];
        file.LocalPath.Should().Be("objects/sample.tif");
        file.Name.Should().Be("sample.tif");
        file.ContentType.Should().Be("image/tiff");
        file.Digest.Should().Be(TestDigest);
        file.Size.Should().Be(12345);
    }

    [Fact]
    public async Task AddToMets_File_Raw_XML_IDs_Are_Consistent()
    {
        // After AddToMets + WriteMets the FILE_*, PHYS_*, ADM_* and TECH_* IDs
        // written to the XML must all reference each other correctly.

        var metsFi = new FileInfo("Outputs/sync-add-file-rawxml.xml");
        var metsUri = new Uri(metsFi.FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Add File Raw XML Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;

        metsManager.AddToMets(fullMets, SimpleFile("objects/doc.tif", "doc.tif"));
        await metsManager.WriteMets(fullMets);

        var doc = XDocument.Load(metsUri.LocalPath);

        // FileType in fileSec
        var fileEl = doc.Descendants(MetsNs + "file")
            .Single(f => (string)f.Attribute("ID")! == "FILE_objects/doc.tif");
        fileEl.Attribute("ADMID")!.Value.Should().Be("ADM_objects/doc.tif");
        fileEl.Elements(MetsNs + "FLocat").Single()
            .Attribute(XLinkNs + "href")!.Value.Should().Be("objects/doc.tif");

        // AmdSec with matching ID
        doc.Descendants(MetsNs + "amdSec")
            .Should().Contain(a => (string?)a.Attribute("ID") == "ADM_objects/doc.tif");

        // TechMD inside that AmdSec
        var amdSec = doc.Descendants(MetsNs + "amdSec")
            .Single(a => (string)a.Attribute("ID")! == "ADM_objects/doc.tif");
        amdSec.Elements(MetsNs + "techMD").Single()
            .Attribute("ID")!.Value.Should().Be("TECH_objects/doc.tif");

        // StructMap Div
        var divEl = doc.Descendants(MetsNs + "div")
            .Single(d => (string?)d.Attribute("ID") == "PHYS_objects/doc.tif");
        divEl.Attribute("TYPE")!.Value.Should().Be("Item");
        divEl.Elements(MetsNs + "fptr").Single()
            .Attribute("FILEID")!.Value.Should().Be("FILE_objects/doc.tif");
    }

    [Fact]
    public async Task AddToMets_Directory_Round_Trips_Via_Parser_After_WriteMets()
    {
        // A WorkingDirectory passed to AddToMets must appear in the parsed structure
        // with the correct LocalPath and Name.

        var metsFi = new FileInfo("Outputs/sync-add-dir-roundtrip.xml");
        var metsUri = new Uri(metsFi.FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Add Dir Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;

        var addResult = metsManager.AddToMets(fullMets, SimpleDirectory("objects/sub-folder", "Sub Folder"));
        addResult.Success.Should().BeTrue();
        await metsManager.WriteMets(fullMets);

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();

        var objectsDir = parseResult.Value!.PhysicalStructure!.Directories
            .Single(d => d.Name == FolderNames.Objects);
        objectsDir.Directories.Should().HaveCount(1);
        var dir = objectsDir.Directories[0];
        dir.LocalPath.Should().Be("objects/sub-folder");
        dir.Name.Should().Be("Sub Folder");
    }

    [Fact]
    public async Task AddToMets_Directory_Raw_XML_IDs_Are_Consistent()
    {
        // After AddToMets + WriteMets for a directory, the PHYS_* structMap Div and
        // ADM_* amdSec must exist and reference the same ID string.

        var metsFi = new FileInfo("Outputs/sync-add-dir-rawxml.xml");
        var metsUri = new Uri(metsFi.FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Add Dir Raw XML Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;

        metsManager.AddToMets(fullMets, SimpleDirectory("objects/archive", "Archive"));
        await metsManager.WriteMets(fullMets);

        var doc = XDocument.Load(metsUri.LocalPath);

        // StructMap Div for the directory
        var dirDiv = doc.Descendants(MetsNs + "div")
            .Single(d => (string?)d.Attribute("ID") == "PHYS_objects/archive");
        dirDiv.Attribute("TYPE")!.Value.Should().Be("Directory");
        dirDiv.Attribute("LABEL")!.Value.Should().Be("Archive");
        dirDiv.Attribute("ADMID")!.Value.Should().Be("ADM_objects/archive");

        // AmdSec for the directory
        doc.Descendants(MetsNs + "amdSec")
            .Should().Contain(a => (string?)a.Attribute("ID") == "ADM_objects/archive");
    }

    [Fact]
    public async Task Multiple_AddToMets_Calls_Without_Intermediate_Write_All_Persist()
    {
        // AddToMets mutates the same in-memory FullMets instance. All mutations must
        // be present in the written file when WriteMets is called once at the end.

        var metsFi = new FileInfo("Outputs/sync-add-multi.xml");
        var metsUri = new Uri(metsFi.FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Multi Add Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;

        metsManager.AddToMets(fullMets, SimpleFile("objects/page001.tif", "page001.tif"));
        metsManager.AddToMets(fullMets, SimpleFile("objects/page002.tif", "page002.tif"));
        metsManager.AddToMets(fullMets, SimpleFile("objects/page003.tif", "page003.tif"));

        // Single WriteMets call for all three additions.
        await metsManager.WriteMets(fullMets);

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();

        var objectsDir = parseResult.Value!.PhysicalStructure!.Directories
            .Single(d => d.Name == FolderNames.Objects);
        objectsDir.Files.Should().HaveCount(3);
        objectsDir.Files.Select(f => f.LocalPath).Should().BeEquivalentTo(
            "objects/page001.tif",
            "objects/page002.tif",
            "objects/page003.tif");
    }

    // -----------------------------------------------------------------------
    // DeleteFromMets
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeleteFromMets_File_Removes_Entry_From_Parser_And_Raw_XML()
    {
        // After DeleteFromMets + WriteMets the deleted file must be absent from both
        // the MetsParser round-trip and the raw XML fileSec / amdSec.

        var metsFi = new FileInfo("Outputs/sync-delete-file.xml");
        var metsUri = new Uri(metsFi.FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Delete File Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;

        // Add two files then persist.
        metsManager.AddToMets(fullMets, SimpleFile("objects/keep.tif", "keep.tif"));
        metsManager.AddToMets(fullMets, SimpleFile("objects/remove.tif", "remove.tif"));
        await metsManager.WriteMets(fullMets);

        // Re-load for a fresh FullMets (clean in-memory snapshot).
        var parsedAfterAdd = await parser.GetMetsFileWrapper(metsUri);
        var fullMets2 = (await metsManager.GetFullMets(metsUri, parsedAfterAdd.Value!.ETag!)).Value!;

        var deleteResult = metsManager.DeleteFromMets(fullMets2, "objects/remove.tif");
        deleteResult.Success.Should().BeTrue();
        await metsManager.WriteMets(fullMets2);

        // Parser round-trip: only "keep.tif" should remain.
        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();
        var objectsDir = parseResult.Value!.PhysicalStructure!.Directories
            .Single(d => d.Name == FolderNames.Objects);
        objectsDir.Files.Should().HaveCount(1);
        objectsDir.Files[0].LocalPath.Should().Be("objects/keep.tif");

        // Raw XML: fileSec entry and amdSec entry for the removed file must be gone.
        var doc = XDocument.Load(metsUri.LocalPath);
        doc.Descendants(MetsNs + "file")
            .Should().NotContain(f => (string?)f.Attribute("ID") == "FILE_objects/remove.tif");
        doc.Descendants(MetsNs + "amdSec")
            .Should().NotContain(a => (string?)a.Attribute("ID") == "ADM_objects/remove.tif");
        // The structMap Div for the deleted file must also be gone.
        doc.Descendants(MetsNs + "div")
            .Should().NotContain(d => (string?)d.Attribute("ID") == "PHYS_objects/remove.tif");
    }

    [Fact]
    public async Task DeleteFromMets_Directory_Removes_Div_And_AmdSec_From_Raw_XML()
    {
        // After DeleteFromMets + WriteMets on an empty directory, the structMap Div
        // and the matching amdSec must both be absent from the raw XML.

        var metsFi = new FileInfo("Outputs/sync-delete-dir.xml");
        var metsUri = new Uri(metsFi.FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Delete Dir Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;

        // Add two directories then persist.
        metsManager.AddToMets(fullMets, SimpleDirectory("objects/keep-dir", "Keep Dir"));
        metsManager.AddToMets(fullMets, SimpleDirectory("objects/remove-dir", "Remove Dir"));
        await metsManager.WriteMets(fullMets);

        var parsedAfterAdd = await parser.GetMetsFileWrapper(metsUri);
        var fullMets2 = (await metsManager.GetFullMets(metsUri, parsedAfterAdd.Value!.ETag!)).Value!;

        var deleteResult = metsManager.DeleteFromMets(fullMets2, "objects/remove-dir");
        deleteResult.Success.Should().BeTrue();
        await metsManager.WriteMets(fullMets2);

        // Parser round-trip: only keep-dir remains.
        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();
        var objectsDir = parseResult.Value!.PhysicalStructure!.Directories
            .Single(d => d.Name == FolderNames.Objects);
        objectsDir.Directories.Should().HaveCount(1);
        objectsDir.Directories[0].LocalPath.Should().Be("objects/keep-dir");

        // Raw XML: structMap Div and amdSec for the removed directory must be gone.
        var doc = XDocument.Load(metsUri.LocalPath);
        doc.Descendants(MetsNs + "div")
            .Should().NotContain(d => (string?)d.Attribute("ID") == "PHYS_objects/remove-dir");
        doc.Descendants(MetsNs + "amdSec")
            .Should().NotContain(a => (string?)a.Attribute("ID") == "ADM_objects/remove-dir");
    }

    [Fact]
    public async Task DeleteFromMets_Returns_NotFound_For_Non_Existent_Path()
    {
        // DeleteFromMets must return NotFound (not throw) when the path does not exist
        // in the METS. The in-memory FullMets must be left unmodified.

        var metsFi = new FileInfo("Outputs/sync-delete-notfound.xml");
        var metsUri = new Uri(metsFi.FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Delete NotFound Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;

        var result = metsManager.DeleteFromMets(fullMets, "objects/does-not-exist.tif");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.NotFound);
    }

    [Fact]
    public async Task DeleteFromMets_Returns_BadRequest_For_Non_Empty_Directory()
    {
        // DeleteFromMets must refuse to delete a directory that still contains children
        // and return BadRequest, leaving the METS unchanged.

        var metsFi = new FileInfo("Outputs/sync-delete-nonempty-dir.xml");
        var metsUri = new Uri(metsFi.FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Delete NonEmpty Dir Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;

        metsManager.AddToMets(fullMets, SimpleDirectory("objects/parent", "Parent"));
        metsManager.AddToMets(fullMets, SimpleFile("objects/parent/child.tif", "child.tif"));
        await metsManager.WriteMets(fullMets);

        var parsedAfterAdd = await parser.GetMetsFileWrapper(metsUri);
        var fullMets2 = (await metsManager.GetFullMets(metsUri, parsedAfterAdd.Value!.ETag!)).Value!;

        var result = metsManager.DeleteFromMets(fullMets2, "objects/parent");

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BadRequest);
        result.ErrorMessage.Should().Contain("non-empty");
    }
}
