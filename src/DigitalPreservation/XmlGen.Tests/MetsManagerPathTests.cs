using System.Xml.Linq;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common.Mets;
using Storage.Repository.Common.Mets.StorageImpl;

namespace XmlGen.Tests;

/// <summary>
/// Tests that verify round-trip fidelity and raw XML attribute consistency when
/// WorkingFile and WorkingDirectory LocalPaths contain characters that are
/// problematic in XML ID / IDREFS attributes — in particular spaces, ampersands,
/// and Unicode characters.
///
/// Background: MetsManager generates ID, ADMID, FILEID, and DMDID attribute values
/// directly from LocalPath strings without escaping. Spaces are especially
/// problematic because ADMID is typed as IDREFS in the METS schema, which means
/// the XmlSerializer backing XmlGen types splits the attribute value on whitespace
/// into a Collection&lt;string&gt; of individual tokens. MetadataManager line 195
/// works around this with:
///
///     ctx.FileAdmId = string.Join(' ', ctx.File.Admid);
///
/// which rejoins the tokens before looking up the matching AmdSec element.
///
/// Every test validates at two levels:
///   1. Parser round-trip — MetsParser reads back what MetsManager wrote and
///      WorkingFile / WorkingDirectory properties are correct.
///   2. Raw XML — XDocument assertions confirm that the ID, ADMID, FILEID and
///      HREF attributes in the generated XML are mutually consistent, because
///      software other than MetsParser may consume the XML.
/// </summary>
public class MetsManagerPathTests
{
    private readonly MetsManager metsManager;
    private readonly MetsParser parser;

    private static readonly XNamespace MetsNs = "http://www.loc.gov/METS/";
    private static readonly XNamespace XLinkNs = "http://www.w3.org/1999/xlink";

    // A real SHA-256 digest used as consistent test data throughout this file
    private const string TestDigest = "eb634d64ce8e6be5195174ceaef9ac9e19c37119f3b31618630aa633ccdbf68f";

    public MetsManagerPathTests()
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

    private async Task<(Uri metsUri, string eTag)> CreateEmptyMets(string outputFileName)
    {
        var fi = new FileInfo($"Outputs/{outputFileName}");
        var uri = new Uri(fi.FullName);
        var result = await metsManager.CreateStandardMets(uri, "Test METS");
        result.Success.Should().BeTrue();
        return (uri, result.Value!.ETag!);
    }

    private async Task<string> AddFile(Uri metsUri, WorkingFile file, string eTag)
    {
        var result = await metsManager.HandleSingleFileUpload(metsUri, file, eTag);
        result.Success.Should().BeTrue(result.ErrorMessage ?? "upload failed");
        var parsed = await parser.GetMetsFileWrapper(metsUri);
        return parsed.Value!.ETag!;
    }

    private async Task<string> AddDirectory(Uri metsUri, WorkingDirectory dir, string eTag)
    {
        var result = await metsManager.HandleCreateFolder(metsUri, dir, eTag);
        result.Success.Should().BeTrue(result.ErrorMessage ?? "create folder failed");
        var parsed = await parser.GetMetsFileWrapper(metsUri);
        return parsed.Value!.ETag!;
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

    private static WorkingDirectory SimpleDirectory(string localPath, string name) =>
        new()
        {
            LocalPath = localPath,
            Name = name,
            Modified = DateTime.UtcNow
        };

    // -----------------------------------------------------------------------
    // SPACE IN FILENAME
    // -----------------------------------------------------------------------

    [Fact]
    public async Task File_With_Space_In_Name_Round_Trips()
    {
        // A file whose LocalPath contains a space in the filename.
        // The ADMID attribute "ADM_objects/my file.pdf" is split by the XmlSerializer
        // into ["ADM_objects/my", "file.pdf"], but all top-level WorkingFile properties
        // must survive the full write/parse cycle unchanged.

        var (metsUri, eTag) = await CreateEmptyMets("path-space-filename.xml");
        await AddFile(metsUri, SimpleFile("objects/my file.pdf", "my file.pdf"), eTag);

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();

        var file = parseResult.Value!.Files.Single(f => f.LocalPath == "objects/my file.pdf");
        file.Name.Should().Be("my file.pdf");
        file.ContentType.Should().Be("application/pdf");
        file.Digest.Should().Be(TestDigest);
        file.Size.Should().Be(54321);
    }

    [Fact]
    public async Task File_With_Space_In_Name_Raw_XML_IDs_Are_Consistent()
    {
        // Verifies that the ID, ADMID, FILEID, and HREF attribute strings in the
        // generated XML are mutually consistent: each attribute that references
        // another element uses exactly the same string as that element's ID.
        //
        // Note: ADMID is typed as IDREFS in the METS schema, so schema-aware parsers
        // will split "ADM_objects/my file.pdf" on the space. That is a known limitation
        // documented at MetadataManager line 192-195 and is out of scope for this test.
        // This test focuses on whether the string values written are self-consistent
        // (i.e., every cross-reference resolves to the right element).

        var (metsUri, eTag) = await CreateEmptyMets("path-space-rawxml.xml");
        await AddFile(metsUri, SimpleFile("objects/my file.pdf", "my file.pdf"), eTag);

        var doc = XDocument.Load(metsUri.LocalPath);

        // FileType: ID and ADMID attribute values
        var fileEl = doc.Descendants(MetsNs + "file")
            .Single(f => (string)f.Attribute("ID")! == "FILE_objects/my file.pdf");
        fileEl.Attribute("ADMID")!.Value.Should().Be("ADM_objects/my file.pdf");

        // FLocat HREF
        fileEl.Elements(MetsNs + "FLocat").Single()
            .Attribute(XLinkNs + "href")!.Value.Should().Be("objects/my file.pdf");

        // AmdSec ID matches the ADMID on the FileType
        var amdSecEl = doc.Descendants(MetsNs + "amdSec")
            .Single(a => (string)a.Attribute("ID")! == "ADM_objects/my file.pdf");

        // TechMD inside that AmdSec
        amdSecEl.Elements(MetsNs + "techMD").Single()
            .Attribute("ID")!.Value.Should().Be("TECH_objects/my file.pdf");

        // StructMap Div: ID, and its Fptr FILEID
        var divEl = doc.Descendants(MetsNs + "div")
            .Single(d => (string?)d.Attribute("ID") == "PHYS_objects/my file.pdf");
        divEl.Elements(MetsNs + "fptr").Single()
            .Attribute("FILEID")!.Value.Should().Be("FILE_objects/my file.pdf");
    }

    [Fact]
    public async Task File_With_Space_In_Name_Can_Be_Overwritten()
    {
        // Verifies that the update path through MetadataManager.GetMetadataXml (line 195)
        // correctly locates the AmdSec for a file whose path contains a space.
        //
        // On update (newUpload=false), MetadataManager does:
        //   ctx.FileAdmId = string.Join(' ', ctx.File.Admid);
        // to reconstruct "ADM_objects/my file.pdf" from ["ADM_objects/my", "file.pdf"],
        // then finds the AmdSec by that ID. If the join were wrong the update would throw.

        var (metsUri, eTag) = await CreateEmptyMets("path-space-overwrite.xml");
        eTag = await AddFile(metsUri, SimpleFile("objects/my file.pdf", "my file.pdf"), eTag);

        // Overwrite with FileFormatMetadata (simulates post-pipeline run)
        var updatedFile = SimpleFile("objects/my file.pdf", "my file.pdf");
        updatedFile.Metadata.Add(new FileFormatMetadata
        {
            Source = "Siegfried",
            PronomKey = "fmt/18",
            FormatName = "PDF 1.4",
            Size = 54321,
            Digest = TestDigest
        });

        var overwriteResult = await metsManager.HandleSingleFileUpload(metsUri, updatedFile, eTag);
        overwriteResult.Success.Should().BeTrue();

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        var file = parseResult.Value!.Files.Single(f => f.LocalPath == "objects/my file.pdf");
        var ffm = file.Metadata.OfType<FileFormatMetadata>().Single();
        ffm.PronomKey.Should().Be("fmt/18");
        ffm.FormatName.Should().Be("PDF 1.4");
    }

    // -----------------------------------------------------------------------
    // MULTIPLE SPACES IN A SINGLE PATH ELEMENT
    // -----------------------------------------------------------------------

    [Fact]
    public async Task File_With_Multiple_Spaces_In_Name_Round_Trips_And_Overwrite_Works()
    {
        // A path element with more than one space, e.g. "my great file.pdf".
        // The ADMID attribute "ADM_objects/my great file.pdf" is split by the XmlSerializer
        // into ["ADM_objects/my", "great", "file.pdf"] — three tokens, not two.
        // string.Join(' ', ...) must reconstruct the original string correctly in both
        // the initial upload path and the overwrite path.

        var (metsUri, eTag) = await CreateEmptyMets("path-multi-space.xml");
        eTag = await AddFile(metsUri, SimpleFile("objects/my great file.pdf", "my great file.pdf"), eTag);

        // Initial round-trip
        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        var file = parseResult.Value!.Files.Single(f => f.LocalPath == "objects/my great file.pdf");
        file.Name.Should().Be("my great file.pdf");
        file.Digest.Should().Be(TestDigest);

        // Overwrite — tests that the multi-token Admid Collection is handled in the update path
        var updatedFile = SimpleFile("objects/my great file.pdf", "my great file.pdf");
        updatedFile.Metadata.Add(new FileFormatMetadata
        {
            Source = "Siegfried",
            PronomKey = "fmt/19",
            FormatName = "PDF 1.5",
            Size = 54321,
            Digest = TestDigest
        });

        var overwriteResult = await metsManager.HandleSingleFileUpload(metsUri, updatedFile, eTag);
        overwriteResult.Success.Should().BeTrue();

        var parseResult2 = await parser.GetMetsFileWrapper(metsUri);
        var updatedFile2 = parseResult2.Value!.Files.Single(f => f.LocalPath == "objects/my great file.pdf");
        updatedFile2.Metadata.OfType<FileFormatMetadata>().Single().PronomKey.Should().Be("fmt/19");

        // Raw XML: ADMID contains all three space-separated tokens in a single attribute string
        var doc = XDocument.Load(metsUri.LocalPath);
        var fileEl = doc.Descendants(MetsNs + "file")
            .Single(f => (string)f.Attribute("ID")! == "FILE_objects/my great file.pdf");
        fileEl.Attribute("ADMID")!.Value.Should().Be("ADM_objects/my great file.pdf");
    }

    // -----------------------------------------------------------------------
    // SPACE IN DIRECTORY NAME
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Directory_With_Space_In_Name_Round_Trips()
    {
        // A child directory under objects/ whose LocalPath contains a space.
        // Navigation through GetMetsElements uses Div IDs derived from the path,
        // so "PHYS_objects/my folder" must be findable in memory after writing.

        var (metsUri, eTag) = await CreateEmptyMets("path-space-dirname.xml");
        eTag = await AddDirectory(metsUri, SimpleDirectory("objects/my folder", "my folder"), eTag);
        await AddFile(metsUri, SimpleFile("objects/my folder/document.pdf", "document.pdf"), eTag);

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();

        var objDir = parseResult.Value!.PhysicalStructure!
            .Directories.Single(d => d.Name == FolderNames.Objects);
        var spacedDir = objDir.Directories.Single(d => d.Name == "my folder");
        spacedDir.LocalPath.Should().Be("objects/my folder");
        spacedDir.Files.Should().HaveCount(1);
        spacedDir.Files[0].LocalPath.Should().Be("objects/my folder/document.pdf");
        spacedDir.Files[0].Name.Should().Be("document.pdf");

        // Raw XML: directory Div ID and its ADMID
        var doc = XDocument.Load(metsUri.LocalPath);
        var dirDiv = doc.Descendants(MetsNs + "div")
            .Single(d => (string?)d.Attribute("ID") == "PHYS_objects/my folder");
        dirDiv.Attribute("ADMID")!.Value.Should().Be("ADM_objects/my folder");
    }

    [Fact]
    public async Task File_With_Space_In_Both_Directory_And_Name_Round_Trips()
    {
        // Combines a spaced directory name with a spaced filename.
        // GetMetsElements navigates two levels of IDs, both containing spaces.

        var (metsUri, eTag) = await CreateEmptyMets("path-space-both.xml");
        eTag = await AddDirectory(metsUri, SimpleDirectory("objects/some archive", "some archive"), eTag);
        await AddFile(metsUri, SimpleFile("objects/some archive/scanned page.pdf", "scanned page.pdf"), eTag);

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();

        var file = parseResult.Value!.Files
            .Single(f => f.LocalPath == "objects/some archive/scanned page.pdf");
        file.Name.Should().Be("scanned page.pdf");
        file.Digest.Should().Be(TestDigest);

        // Raw XML: both the directory Div ID and the file Div ID contain spaces
        var doc = XDocument.Load(metsUri.LocalPath);
        doc.Descendants(MetsNs + "div")
            .Should().Contain(d => (string?)d.Attribute("ID") == "PHYS_objects/some archive");
        doc.Descendants(MetsNs + "div")
            .Should().Contain(d => (string?)d.Attribute("ID") == "PHYS_objects/some archive/scanned page.pdf");

        // The file FileType ADMID and AmdSec ID are consistent
        var fileEl = doc.Descendants(MetsNs + "file")
            .Single(f => (string)f.Attribute("ID")! == "FILE_objects/some archive/scanned page.pdf");
        fileEl.Attribute("ADMID")!.Value.Should().Be("ADM_objects/some archive/scanned page.pdf");
        doc.Descendants(MetsNs + "amdSec")
            .Should().Contain(a => (string?)a.Attribute("ID") == "ADM_objects/some archive/scanned page.pdf");
    }

    // -----------------------------------------------------------------------
    // AMPERSAND IN PATH
    // -----------------------------------------------------------------------

    [Fact]
    public async Task File_With_Ampersand_In_Name_Round_Trips()
    {
        // Ampersand is a reserved XML character and must be entity-encoded in
        // attribute values as &amp; The XmlSerializer handles encoding on write and
        // the XDocument parser decodes it on read. This test confirms the LocalPath
        // survives the encode/decode cycle with the original ampersand intact.

        var (metsUri, eTag) = await CreateEmptyMets("path-ampersand.xml");
        await AddFile(metsUri, SimpleFile("objects/AT&T guide.pdf", "AT&T guide.pdf"), eTag);

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();

        var file = parseResult.Value!.Files.Single(f => f.LocalPath == "objects/AT&T guide.pdf");
        file.Name.Should().Be("AT&T guide.pdf");
        file.Digest.Should().Be(TestDigest);

        // XDocument decodes &amp; back to & so we assert on the decoded string
        var doc = XDocument.Load(metsUri.LocalPath);
        var fLocat = doc.Descendants(MetsNs + "FLocat")
            .Single(fl => ((string)fl.Attribute(XLinkNs + "href")!).Contains("AT&T"));
        ((string)fLocat.Attribute(XLinkNs + "href")!).Should().Be("objects/AT&T guide.pdf");
    }

    // -----------------------------------------------------------------------
    // UNICODE IN PATH
    // -----------------------------------------------------------------------

    [Fact]
    public async Task File_With_Unicode_Characters_In_Name_Round_Trips()
    {
        // Accented and non-ASCII characters are valid XML Name characters (in the
        // extended Unicode range), so they should survive the write/read cycle without
        // any escaping issues.

        var (metsUri, eTag) = await CreateEmptyMets("path-unicode.xml");
        await AddFile(metsUri, SimpleFile("objects/résumé.pdf", "résumé.pdf"), eTag);

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();

        var file = parseResult.Value!.Files.Single(f => f.LocalPath == "objects/résumé.pdf");
        file.Name.Should().Be("résumé.pdf");
        file.Digest.Should().Be(TestDigest);

        // Raw XML: ID attributes contain the unescaped Unicode characters
        var doc = XDocument.Load(metsUri.LocalPath);
        doc.Descendants(MetsNs + "file")
            .Should().Contain(f => (string)f.Attribute("ID")! == "FILE_objects/résumé.pdf");
        doc.Descendants(MetsNs + "amdSec")
            .Should().Contain(a => (string?)a.Attribute("ID") == "ADM_objects/résumé.pdf");
    }
}
