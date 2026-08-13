using System.Xml.Linq;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XmlGen.Tests;

/// <summary>
/// Tests that verify round-trip fidelity and raw XML attribute consistency when
/// WorkingFile and WorkingDirectory LocalPaths contain characters that are
/// problematic in XML ID / IDREFS attributes — in particular spaces, ampersands,
/// and Unicode characters.
///
/// Background: MetsManager builds ID, ADMID, FILEID and DMDID values from LocalPath.
/// A path is not a valid xs:ID — <c>/</c> and space are both illegal — so since issue
/// #188 the path goes through <see cref="DigitalPreservation.Utils.MetsIdEncoding.ToMetsId"/>
/// first and the ID becomes e.g. <c>FILE_objects_x002F_my_x0020_file.pdf</c>. Expected
/// values here are therefore built with <see cref="MetsIds"/> rather than written out.
/// The paths themselves (FLocat href, premis:originalName) are NOT encoded and keep
/// their spaces and slashes.
///
/// This removes the IDREFS hazard for everything the platform writes from now on: an
/// encoded ADMID is a single token, so the XmlSerializer no longer splits it. The
/// rejoining logic (IdRefs) stays for METS written BEFORE the fix, whose raw IDs still
/// split — see MetsManagerPathFixtureTests for that frozen corpus.
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
        // A file whose LocalPath contains a space in the filename. The space is encoded
        // out of the IDs, but the path itself keeps it, and all top-level WorkingFile
        // properties must survive the full write/parse cycle unchanged.

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
        // Since #188 the ADMID is also a single IDREFS token — the space that used to
        // split it is encoded — so a schema-aware parser resolves it to one amdSec.

        const string path = "objects/my file.pdf";
        var (metsUri, eTag) = await CreateEmptyMets("path-space-rawxml.xml");
        await AddFile(metsUri, SimpleFile(path, "my file.pdf"), eTag);

        var doc = XDocument.Load(metsUri.LocalPath);

        // FileType: ID and ADMID attribute values
        var fileEl = doc.Descendants(MetsNs + "file")
            .Single(f => (string)f.Attribute("ID")! == MetsIds.File(path));
        fileEl.Attribute("ADMID")!.Value.Should().Be(MetsIds.Adm(path));
        fileEl.Attribute("ADMID")!.Value.Should().NotContain(" ",
            "an encoded ADMID is one IDREFS token, not several");

        // FLocat HREF — the path itself is never encoded
        fileEl.Elements(MetsNs + "FLocat").Single()
            .Attribute(XLinkNs + "href")!.Value.Should().Be(path);

        // AmdSec ID matches the ADMID on the FileType
        var amdSecEl = doc.Descendants(MetsNs + "amdSec")
            .Single(a => (string)a.Attribute("ID")! == MetsIds.Adm(path));

        // TechMD inside that AmdSec
        amdSecEl.Elements(MetsNs + "techMD").Single()
            .Attribute("ID")!.Value.Should().Be(MetsIds.Tech(path));

        // StructMap Div: ID, and its Fptr FILEID
        var divEl = doc.Descendants(MetsNs + "div")
            .Single(d => (string?)d.Attribute("ID") == MetsIds.Phys(path));
        divEl.Elements(MetsNs + "fptr").Single()
            .Attribute("FILEID")!.Value.Should().Be(MetsIds.File(path));
    }

    [Fact]
    public async Task File_With_Space_In_Name_Can_Be_Overwritten()
    {
        // Verifies that the update path through MetadataManager.GetMetadataXml correctly
        // locates the AmdSec for a file whose path contains a space. On update
        // (newUpload=false) the amdSec is resolved from the file's ADMID tokens via IdRefs,
        // which handles both the single encoded token written here and the several tokens a
        // legacy raw ID splits into.

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
        // A path element with more than one space, e.g. "my great file.pdf" — which before
        // #188 produced an ADMID that split into THREE tokens. Both spaces are now encoded,
        // in the initial upload path and in the overwrite path.

        const string path = "objects/my great file.pdf";
        var (metsUri, eTag) = await CreateEmptyMets("path-multi-space.xml");
        eTag = await AddFile(metsUri, SimpleFile(path, "my great file.pdf"), eTag);

        // Initial round-trip
        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        var file = parseResult.Value!.Files.Single(f => f.LocalPath == path);
        file.Name.Should().Be("my great file.pdf");
        file.Digest.Should().Be(TestDigest);

        // Overwrite — tests that the Admid Collection is resolved in the update path
        var updatedFile = SimpleFile(path, "my great file.pdf");
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
        var updatedFile2 = parseResult2.Value!.Files.Single(f => f.LocalPath == path);
        updatedFile2.Metadata.OfType<FileFormatMetadata>().Single().PronomKey.Should().Be("fmt/19");

        // Raw XML: both spaces are encoded, so the ADMID is a single IDREFS token
        var doc = XDocument.Load(metsUri.LocalPath);
        var fileEl = doc.Descendants(MetsNs + "file")
            .Single(f => (string)f.Attribute("ID")! == MetsIds.File(path));
        fileEl.Attribute("ADMID")!.Value.Should().Be(MetsIds.Adm(path));
        fileEl.Attribute("ADMID")!.Value.Should().NotContain(" ");
    }

    // -----------------------------------------------------------------------
    // SPACE IN DIRECTORY NAME
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Directory_With_Space_In_Name_Round_Trips()
    {
        // A child directory under objects/ whose LocalPath contains a space. Navigation is
        // by premis:originalName (the path cache), so the div is findable whatever form its
        // ID takes; the ID itself is now the encoded form.

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
            .Single(d => (string?)d.Attribute("ID") == MetsIds.Phys("objects/my folder"));
        dirDiv.Attribute("ADMID")!.Value.Should().Be(MetsIds.Adm("objects/my folder"));
    }

    [Fact]
    public async Task File_With_Space_In_Both_Directory_And_Name_Round_Trips()
    {
        // Combines a spaced directory name with a spaced filename: navigation crosses two
        // levels whose paths both contain spaces.

        const string dirPath = "objects/some archive";
        const string filePath = "objects/some archive/scanned page.pdf";
        var (metsUri, eTag) = await CreateEmptyMets("path-space-both.xml");
        eTag = await AddDirectory(metsUri, SimpleDirectory(dirPath, "some archive"), eTag);
        await AddFile(metsUri, SimpleFile(filePath, "scanned page.pdf"), eTag);

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();

        var file = parseResult.Value!.Files.Single(f => f.LocalPath == filePath);
        file.Name.Should().Be("scanned page.pdf");
        file.Digest.Should().Be(TestDigest);

        // Raw XML: the spaces and slashes are encoded out of both Div IDs
        var doc = XDocument.Load(metsUri.LocalPath);
        doc.Descendants(MetsNs + "div")
            .Should().Contain(d => (string?)d.Attribute("ID") == MetsIds.Phys(dirPath));
        doc.Descendants(MetsNs + "div")
            .Should().Contain(d => (string?)d.Attribute("ID") == MetsIds.Phys(filePath));

        // The file FileType ADMID and AmdSec ID are consistent
        var fileEl = doc.Descendants(MetsNs + "file")
            .Single(f => (string)f.Attribute("ID")! == MetsIds.File(filePath));
        fileEl.Attribute("ADMID")!.Value.Should().Be(MetsIds.Adm(filePath));
        doc.Descendants(MetsNs + "amdSec")
            .Should().Contain(a => (string?)a.Attribute("ID") == MetsIds.Adm(filePath));
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

        // Raw XML: accented characters are valid NCName characters and pass through the ID
        // encoding untouched — only the path separator is escaped.
        var doc = XDocument.Load(metsUri.LocalPath);
        MetsIds.File("objects/résumé.pdf").Should().EndWith("résumé.pdf");
        doc.Descendants(MetsNs + "file")
            .Should().Contain(f => (string)f.Attribute("ID")! == MetsIds.File("objects/résumé.pdf"));
        doc.Descendants(MetsNs + "amdSec")
            .Should().Contain(a => (string?)a.Attribute("ID") == MetsIds.Adm("objects/résumé.pdf"));
    }
}
