using System.Xml.Linq;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Mets;
using DigitalPreservation.XmlGen.Mets;
using DigitalPreservation.Mets.StorageImpl;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XmlGen.Tests;

/// <summary>
/// Tests for PREMIS metadata, virus scan events, and EXIF metadata written by MetsManager
/// and MetadataManager.
///
/// Every test validates at two levels:
///   1. Parser round-trip  — verifies that MetsParser can read back what MetsManager writes,
///      and that WorkingFile.Metadata is correctly populated (FileFormatMetadata, VirusScanMetadata,
///      ExifMetadata).
///   2. Raw XML            — verifies the actual METS XML structure, because software other than
///      MetsParser may consume it.
///
/// The METS and PREMIS namespace constants are declared once and used throughout.
/// </summary>
public class MetsManagerWithPremis
{
    private readonly MetsManager metsManager;
    private readonly MetsParser parser;

    // XML namespaces for raw XML assertions
    private static readonly XNamespace MetsNs = "http://www.loc.gov/METS/";
    private static readonly XNamespace PremisNs = "http://www.loc.gov/premis/v3";

    // A real SHA-256 digest used as consistent test data throughout this file
    private const string TestDigest = "eb634d64ce8e6be5195174ceaef9ac9e19c37119f3b31618630aa633ccdbf68f";
    private const string ClamAvDefinition = "ClamAV 1.4.3/27932/Fri Mar  6 07:24:27 2026";

    public MetsManagerWithPremis()
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

    /// <summary>
    /// Uploads a file and returns the ETag of the METS after writing.
    /// Asserts that the upload succeeded.
    /// </summary>
    private async Task<string> AddFile(Uri metsUri, WorkingFile file, string eTag)
    {
        var result = await metsManager.HandleSingleFileUpload(metsUri, file, eTag);
        result.Success.Should().BeTrue();
        var parsed = await parser.GetMetsFileWrapper(metsUri);
        return parsed.Value!.ETag!;
    }

    private static WorkingFile SimpleFile(string localPath, string name = "document.pdf") =>
        new()
        {
            LocalPath = localPath,
            Name = name,
            ContentType = "application/pdf",
            Digest = TestDigest,
            Size = 54321,
            Modified = DateTime.UtcNow
        };

    // -----------------------------------------------------------------------
    // BASIC PREMIS / FILEFORMATMETADATA
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Upload_Only_No_Pipeline_Produces_Basic_Premis_In_AmdSec()
    {
        // Represents an upload where no pipeline has run: WorkingFile has only top-level
        // properties (Digest, Size, ContentType) and no Metadata items.
        // MetsManager must still write a valid PREMIS techMD with fixity and size,
        // but without any PRONOM format information.
        // MetsParser will return a FileFormatMetadata with PronomKey = "UNKNOWN" because
        // it always produces one (see parser fallback on line 449 of MetsParser.cs).

        var (metsUri, eTag) = await CreateEmptyMets("premis-bare-upload.xml");
        await AddFile(metsUri, SimpleFile("objects/document.pdf"), eTag);

        // --- Parser round-trip ---
        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();
        var parsedFile = parseResult.Value!.Files.Single(f => f.LocalPath == "objects/document.pdf");

        parsedFile.Digest.Should().Be(TestDigest);
        parsedFile.Size.Should().Be(54321);
        parsedFile.ContentType.Should().Be("application/pdf");

        // Parser always returns a FileFormatMetadata; without PRONOM it falls back to "UNKNOWN"
        var ffm = parsedFile.Metadata.OfType<FileFormatMetadata>().Single();
        ffm.Source.Should().Be(Constants.Mets);
        ffm.PronomKey.Should().Be("UNKNOWN");
        ffm.Digest.Should().Be(TestDigest);

        // No virus scan data
        parsedFile.Metadata.OfType<VirusScanMetadata>().Should().BeEmpty();

        // --- Raw XML ---
        var xml = XDocument.Load(metsUri.LocalPath);

        // fileSec: file element has correct MIMETYPE
        var fileEl = xml.Descendants(MetsNs + "file")
            .Single(f => (string?)f.Attribute("ID") == MetsIds.File("objects/document.pdf"));
        fileEl.Attribute("MIMETYPE")?.Value.Should().Be("application/pdf");

        // amdSec: has techMD with PREMIS, but no digiprovMD (no virus event)
        var amdSec = xml.Descendants(MetsNs + "amdSec")
            .Single(a => (string?)a.Attribute("ID") == MetsIds.Adm("objects/document.pdf"));
        amdSec.Elements(MetsNs + "techMD").Should().HaveCount(1);
        amdSec.Elements(MetsNs + "digiprovMD").Should().BeEmpty();

        // PREMIS: fixity element present with SHA256 digest
        var premisObject = amdSec.Descendants(PremisNs + "object").Single();
        var fixity = premisObject.Descendants(PremisNs + "fixity").Single();
        fixity.Descendants(PremisNs + "messageDigestAlgorithm").Single().Value.Should().Be("SHA256");
        fixity.Descendants(PremisNs + "messageDigest").Single().Value.Should().Be(TestDigest);
    }

    [Fact]
    public async Task Upload_With_FileFormatMetadata_Writes_Pronom_To_Premis_And_Round_Trips()
    {
        // Normal case: upload + Siegfried pipeline ran.
        // WorkingFile carries a FileFormatMetadata with PRONOM key.
        // MetsManager must write PREMIS with fixity, size, and format/registry elements.

        var (metsUri, eTag) = await CreateEmptyMets("premis-with-pronom.xml");

        var file = new WorkingFile
        {
            LocalPath = "objects/document.pdf",
            Name = "document.pdf",
            ContentType = "application/pdf",
            Digest = TestDigest,
            Size = 54321,
            Modified = DateTime.UtcNow,
            Metadata =
            [
                new FileFormatMetadata
                {
                    Source = "Siegfried",
                    PronomKey = "fmt/276",
                    FormatName = "Acrobat PDF 1.7 - Portable Document Format",
                    Digest = TestDigest,
                    Size = 54321,
                    ContentType = "application/pdf"
                }
            ]
        };
        await AddFile(metsUri, file, eTag);

        // --- Parser round-trip ---
        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        var parsedFile = parseResult.Value!.Files.Single(f => f.LocalPath == "objects/document.pdf");

        parsedFile.Digest.Should().Be(TestDigest);
        parsedFile.Size.Should().Be(54321);

        var ffm = parsedFile.Metadata.OfType<FileFormatMetadata>().Single();
        ffm.Source.Should().Be(Constants.Mets);
        ffm.PronomKey.Should().Be("fmt/276");
        ffm.FormatName.Should().Be("Acrobat PDF 1.7 - Portable Document Format");
        ffm.Digest.Should().Be(TestDigest);
        ffm.Size.Should().Be(54321);

        // --- Raw XML ---
        var xml = XDocument.Load(metsUri.LocalPath);
        var amdSec = xml.Descendants(MetsNs + "amdSec")
            .Single(a => (string?)a.Attribute("ID") == MetsIds.Adm("objects/document.pdf"));
        var premisObject = amdSec.Descendants(PremisNs + "object").Single();

        // Fixity
        var fixity = premisObject.Descendants(PremisNs + "fixity").Single();
        fixity.Descendants(PremisNs + "messageDigestAlgorithm").Single().Value.Should().Be("SHA256");
        fixity.Descendants(PremisNs + "messageDigest").Single().Value.Should().Be(TestDigest);

        // Size
        premisObject.Descendants(PremisNs + "size").Single().Value.Should().Be("54321");

        // Format: PRONOM registry key and format name
        var format = premisObject.Descendants(PremisNs + "format").Single();
        format.Descendants(PremisNs + "formatName").Single().Value
            .Should().Be("Acrobat PDF 1.7 - Portable Document Format");
        format.Descendants(PremisNs + "formatRegistryKey").Single().Value.Should().Be("fmt/276");
        format.Descendants(PremisNs + "formatRegistryName").Single().Value.Should().Be("PRONOM");
    }

    [Fact]
    public async Task Upload_With_Multiple_Matching_FileFormatMetadata_Merges_To_Single_Pronom()
    {
        // Defensive case: two tools independently identify the same PRONOM key
        // (e.g., Siegfried run standalone and via Brunnhilde).
        // WorkingFile.GetFileFormatMetadata() merges them into one when PRONOM keys agree.
        // The merged result must produce a single, correct PRONOM entry in METS.

        var (metsUri, eTag) = await CreateEmptyMets("premis-merged-pronom.xml");

        var file = new WorkingFile
        {
            LocalPath = "objects/document.pdf",
            Name = "document.pdf",
            Digest = TestDigest,
            Size = 54321,
            Modified = DateTime.UtcNow,
            Metadata =
            [
                new FileFormatMetadata
                {
                    Source = "Siegfried",
                    PronomKey = "fmt/276",
                    FormatName = "Acrobat PDF 1.7 - Portable Document Format",
                    Digest = TestDigest,
                    Size = 54321,
                    ContentType = "application/pdf"
                },
                new FileFormatMetadata
                {
                    Source = "Brunnhilde",
                    PronomKey = "fmt/276",
                    FormatName = "Acrobat PDF 1.7 - Portable Document Format",
                    Digest = TestDigest,
                    Size = 54321,
                    ContentType = "application/pdf"
                }
            ]
        };
        await AddFile(metsUri, file, eTag);

        // --- Parser round-trip: single FileFormatMetadata with the correct PRONOM key ---
        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        var parsedFile = parseResult.Value!.Files.Single(f => f.LocalPath == "objects/document.pdf");
        var ffm = parsedFile.Metadata.OfType<FileFormatMetadata>().Single();
        ffm.PronomKey.Should().Be("fmt/276");

        // --- Raw XML: exactly one format element in PREMIS ---
        var xml = XDocument.Load(metsUri.LocalPath);
        var premisObject = xml.Descendants(MetsNs + "amdSec")
            .Single(a => (string?)a.Attribute("ID") == MetsIds.Adm("objects/document.pdf"))
            .Descendants(PremisNs + "object").Single();
        premisObject.Descendants(PremisNs + "format").Should().HaveCount(1);

        // --- Also: only one amdSec for this file ---
        AssertSingleAmdSecFor(xml, "objects/document.pdf");
    }

    // -----------------------------------------------------------------------
    // VIRUS SCAN
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Upload_With_Clean_Virus_Scan_Writes_Pass_Outcome_And_Round_Trips()
    {
        // Clean scan: HasVirus=false, VirusFound=null, VirusDefinition=ClamAV version string.
        // MetsManager must write a digiprovMD with a PREMIS event, outcome="Pass",
        // eventDetail = the definition string.

        var (metsUri, eTag) = await CreateEmptyMets("premis-virus-clean.xml");

        var file = new WorkingFile
        {
            LocalPath = "objects/document.pdf",
            Name = "document.pdf",
            Digest = TestDigest,
            Size = 54321,
            Modified = DateTime.UtcNow,
            Metadata =
            [
                new VirusScanMetadata
                {
                    Source = "ClamAV",
                    HasVirus = false,
                    VirusFound = null,
                    VirusDefinition = ClamAvDefinition
                }
            ]
        };
        await AddFile(metsUri, file, eTag);

        // --- Parser round-trip ---
        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        var parsedFile = parseResult.Value!.Files.Single(f => f.LocalPath == "objects/document.pdf");
        var virus = parsedFile.Metadata.OfType<VirusScanMetadata>().Single();
        virus.HasVirus.Should().BeFalse();
        virus.VirusDefinition.Should().Be(ClamAvDefinition);
        // Parser returns "" when the eventOutcomeDetailNote element is absent (null VirusFound)
        virus.VirusFound.Should().BeNullOrEmpty();

        // --- Raw XML ---
        var xml = XDocument.Load(metsUri.LocalPath);
        var amdSec = xml.Descendants(MetsNs + "amdSec")
            .Single(a => (string?)a.Attribute("ID") == MetsIds.Adm("objects/document.pdf"));

        var digiprovMd = amdSec.Elements(MetsNs + "digiprovMD").Single();
        var premisEvent = digiprovMd.Descendants(PremisNs + "event").Single();

        premisEvent.Descendants(PremisNs + "eventType").Single().Value.Should().Be("virus check");
        premisEvent.Descendants(PremisNs + "eventOutcome").Single().Value.Should().Be("Pass");
        premisEvent.Descendants(PremisNs + "eventDetail").Single().Value.Should().Be(ClamAvDefinition);
    }

    [Fact]
    public async Task Upload_With_Infected_File_Writes_Fail_Outcome_And_Round_Trips()
    {
        // Infected scan: HasVirus=true, VirusFound=signature name.
        // PREMIS event must have outcome="Fail" and the virus name in eventOutcomeDetailNote.

        var (metsUri, eTag) = await CreateEmptyMets("premis-virus-infected.xml");

        var file = new WorkingFile
        {
            LocalPath = "objects/suspect.exe",
            Name = "suspect.exe",
            ContentType = "application/octet-stream",
            Digest = TestDigest,
            Size = 1024,
            Modified = DateTime.UtcNow,
            Metadata =
            [
                new VirusScanMetadata
                {
                    Source = "ClamAV",
                    HasVirus = true,
                    VirusFound = "Win.Test.EICAR_HDB-1",
                    VirusDefinition = ClamAvDefinition
                }
            ]
        };
        await AddFile(metsUri, file, eTag);

        // --- Parser round-trip ---
        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        var parsedFile = parseResult.Value!.Files.Single(f => f.LocalPath == "objects/suspect.exe");
        var virus = parsedFile.Metadata.OfType<VirusScanMetadata>().Single();
        virus.HasVirus.Should().BeTrue();
        virus.VirusFound.Should().Be("Win.Test.EICAR_HDB-1");
        virus.VirusDefinition.Should().Be(ClamAvDefinition);

        // --- Raw XML ---
        var xml = XDocument.Load(metsUri.LocalPath);
        var premisEvent = xml.Descendants(MetsNs + "amdSec")
            .Single(a => (string?)a.Attribute("ID") == MetsIds.Adm("objects/suspect.exe"))
            .Descendants(MetsNs + "digiprovMD").Single()
            .Descendants(PremisNs + "event").Single();

        premisEvent.Descendants(PremisNs + "eventOutcome").Single().Value.Should().Be("Fail");
        premisEvent.Descendants(PremisNs + "eventOutcomeDetailNote").Single().Value
            .Should().Be("Win.Test.EICAR_HDB-1");
        premisEvent.Descendants(PremisNs + "eventDetail").Single().Value.Should().Be(ClamAvDefinition);
    }

    // -----------------------------------------------------------------------
    // EXIF METADATA
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Upload_With_Image_Exif_Tags_Writes_Extension_And_Round_Trips()
    {
        // Image file with ExifMetadata containing height and width tags.
        // MetsManager must write these into PREMIS ObjectCharacteristicsExtension.
        // MetsParser must read them back into ExifMetadata.Tags.

        var (metsUri, eTag) = await CreateEmptyMets("premis-exif-image.xml");

        var file = new WorkingFile
        {
            LocalPath = "objects/photo.tif",
            Name = "photo.tif",
            ContentType = "image/tiff",
            Digest = TestDigest,
            Size = 9876543,
            Modified = DateTime.UtcNow,
            Metadata =
            [
                new FileFormatMetadata
                {
                    Source = "Siegfried",
                    PronomKey = "fmt/353",
                    FormatName = "TIFF",
                    ContentType = "image/tiff",
                    Digest = TestDigest,
                    Size = 9876543
                },
                new ExifMetadata
                {
                    Source = "ExifTool",
                    Tags =
                    [
                        new ExifTag { TagName = "ImageHeight", TagValue = "3024" },
                        new ExifTag { TagName = "ImageWidth",  TagValue = "4032" }
                    ]
                }
            ]
        };
        await AddFile(metsUri, file, eTag);

        // --- Parser round-trip ---
        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        var parsedFile = parseResult.Value!.Files.Single(f => f.LocalPath == "objects/photo.tif");

        var ffm = parsedFile.Metadata.OfType<FileFormatMetadata>().Single();
        ffm.PronomKey.Should().Be("fmt/353");

        var exif = parsedFile.Metadata.OfType<ExifMetadata>().Single();
        exif.Tags.Should().HaveCount(2);
        exif.Tags!.Should().Contain(t => t.TagName == "ImageHeight" && t.TagValue == "3024");
        exif.Tags.Should().Contain(t => t.TagName == "ImageWidth"  && t.TagValue == "4032");

        // --- Raw XML: ObjectCharacteristicsExtension contains ExifMetadata element ---
        var xml = XDocument.Load(metsUri.LocalPath);
        var premisObject = xml.Descendants(MetsNs + "amdSec")
            .Single(a => (string?)a.Attribute("ID") == MetsIds.Adm("objects/photo.tif"))
            .Descendants(PremisNs + "object").Single();

        var exifEl = premisObject.Descendants("ExifMetadata").SingleOrDefault();
        exifEl.Should().NotBeNull("ExifMetadata element must be present in ObjectCharacteristicsExtension");
        exifEl!.Element("ImageHeight")?.Value.Should().Be("3024");
        exifEl.Element("ImageWidth")?.Value.Should().Be("4032");
    }

    [Fact]
    public async Task Upload_With_Audio_Video_Exif_Tags_Writes_Duration_And_Bitrate()
    {
        // Audio/video file with Duration and AvgBitrate tags.
        // PremisManagerExif promotes these specific tags to PREMIS SignificantProperties.

        var (metsUri, eTag) = await CreateEmptyMets("premis-exif-av.xml");

        var file = new WorkingFile
        {
            LocalPath = "objects/recording.mp3",
            Name = "recording.mp3",
            ContentType = "audio/mpeg",
            Digest = TestDigest,
            Size = 4567890,
            Modified = DateTime.UtcNow,
            Metadata =
            [
                new ExifMetadata
                {
                    Source = "ExifTool",
                    Tags =
                    [
                        new ExifTag { TagName = "Duration",   TagValue = "3:42" },
                        new ExifTag { TagName = "AvgBitrate", TagValue = "128 kbps" }
                    ]
                }
            ]
        };
        await AddFile(metsUri, file, eTag);

        // --- Parser round-trip: tags round-trip via ExifMetadata ---
        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        var parsedFile = parseResult.Value!.Files.Single(f => f.LocalPath == "objects/recording.mp3");
        var exif = parsedFile.Metadata.OfType<ExifMetadata>().Single();
        exif.Tags.Should().Contain(t => t.TagName == "Duration"   && t.TagValue == "3:42");
        exif.Tags.Should().Contain(t => t.TagName == "AvgBitrate" && t.TagValue == "128 kbps");

        // --- Raw XML: PREMIS significantProperties written for Duration and Bitrate ---
        var xml = XDocument.Load(metsUri.LocalPath);
        var premisObject = xml.Descendants(MetsNs + "amdSec")
            .Single(a => (string?)a.Attribute("ID") == MetsIds.Adm("objects/recording.mp3"))
            .Descendants(PremisNs + "object").Single();

        var sigProps = premisObject.Descendants(PremisNs + "significantProperties").ToList();
        sigProps.Should().Contain(sp =>
            sp.Descendants(PremisNs + "significantPropertiesType").Any(t => t.Value == "Duration"));
        sigProps.Should().Contain(sp =>
            sp.Descendants(PremisNs + "significantPropertiesType").Any(t => t.Value == "Bitrate"));
    }

    // -----------------------------------------------------------------------
    // COMBINED METADATA
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Upload_With_All_Metadata_Types_Writes_And_Round_Trips_All_Three()
    {
        // All three metadata types on a single file: FileFormatMetadata, VirusScanMetadata,
        // ExifMetadata. All must survive the write → parse round-trip independently.
        // The amdSec must have exactly one techMD (PREMIS) and one digiprovMD (virus event).

        var (metsUri, eTag) = await CreateEmptyMets("premis-all-metadata.xml");

        var file = new WorkingFile
        {
            LocalPath = "objects/photo.tif",
            Name = "photo.tif",
            ContentType = "image/tiff",
            Digest = TestDigest,
            Size = 9876543,
            Modified = DateTime.UtcNow,
            Metadata =
            [
                new FileFormatMetadata
                {
                    Source = "Siegfried",
                    PronomKey = "fmt/353",
                    FormatName = "TIFF",
                    ContentType = "image/tiff",
                    Digest = TestDigest,
                    Size = 9876543
                },
                new VirusScanMetadata
                {
                    Source = "ClamAV",
                    HasVirus = false,
                    VirusFound = null,
                    VirusDefinition = ClamAvDefinition
                },
                new ExifMetadata
                {
                    Source = "ExifTool",
                    Tags =
                    [
                        new ExifTag { TagName = "ImageHeight", TagValue = "3024" },
                        new ExifTag { TagName = "ImageWidth",  TagValue = "4032" }
                    ]
                }
            ]
        };
        await AddFile(metsUri, file, eTag);

        // --- Parser round-trip: all three types present ---
        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        var parsedFile = parseResult.Value!.Files.Single(f => f.LocalPath == "objects/photo.tif");

        parsedFile.Metadata.OfType<FileFormatMetadata>().Should().HaveCount(1);
        parsedFile.Metadata.OfType<FileFormatMetadata>().Single().PronomKey.Should().Be("fmt/353");

        parsedFile.Metadata.OfType<VirusScanMetadata>().Should().HaveCount(1);
        parsedFile.Metadata.OfType<VirusScanMetadata>().Single().HasVirus.Should().BeFalse();
        parsedFile.Metadata.OfType<VirusScanMetadata>().Single().VirusDefinition
            .Should().Be(ClamAvDefinition);

        parsedFile.Metadata.OfType<ExifMetadata>().Should().HaveCount(1);
        parsedFile.Metadata.OfType<ExifMetadata>().Single().Tags.Should().HaveCount(2);

        // --- Raw XML: one techMD, one digiprovMD ---
        var xml = XDocument.Load(metsUri.LocalPath);
        var amdSec = xml.Descendants(MetsNs + "amdSec")
            .Single(a => (string?)a.Attribute("ID") == MetsIds.Adm("objects/photo.tif"));
        amdSec.Elements(MetsNs + "techMD").Should().HaveCount(1);
        amdSec.Elements(MetsNs + "digiprovMD").Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // OVERWRITE / UPDATE (the Patch path in MetadataManager)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Overwriting_File_With_FileFormatMetadata_Updates_Premis_Without_Duplicating_AmdSec()
    {
        // First upload: bare file, no pipeline data (no PRONOM in METS).
        // Second upload: same path, now with FileFormatMetadata from Siegfried.
        // This exercises the newUpload=false path in MetadataManager.ProcessAllFileMetadata,
        // which calls PremisManager.Patch instead of Create.

        var (metsUri, eTag) = await CreateEmptyMets("premis-overwrite-ffm.xml");

        // First upload
        eTag = await AddFile(metsUri, SimpleFile("objects/document.pdf"), eTag);

        // Verify: PRONOM not yet present
        var first = await parser.GetMetsFileWrapper(metsUri);
        first.Value!.Files.Single(f => f.LocalPath == "objects/document.pdf")
            .Metadata.OfType<FileFormatMetadata>().Single()
            .PronomKey.Should().Be("UNKNOWN");

        // Second upload: same path, pipeline has now run
        var secondUpload = new WorkingFile
        {
            LocalPath = "objects/document.pdf",
            Name = "document.pdf",
            ContentType = "application/pdf",
            Digest = TestDigest,
            Size = 54321,
            Modified = DateTime.UtcNow,
            Metadata =
            [
                new FileFormatMetadata
                {
                    Source = "Siegfried",
                    PronomKey = "fmt/276",
                    FormatName = "Acrobat PDF 1.7 - Portable Document Format",
                    Digest = TestDigest,
                    Size = 54321,
                    ContentType = "application/pdf"
                }
            ]
        };
        await AddFile(metsUri, secondUpload, eTag);

        // --- Parser round-trip: PRONOM now present ---
        var second = await parser.GetMetsFileWrapper(metsUri);
        var ffm = second.Value!.Files.Single(f => f.LocalPath == "objects/document.pdf")
            .Metadata.OfType<FileFormatMetadata>().Single();
        ffm.PronomKey.Should().Be("fmt/276");

        // --- Raw XML: exactly one amdSec for this file (no duplicate created by overwrite) ---
        AssertSingleAmdSecFor(XDocument.Load(metsUri.LocalPath), "objects/document.pdf");
    }

    // -----------------------------------------------------------------------
    // A virus scan must not disturb other digital provenance (issue #219)
    // -----------------------------------------------------------------------

    private static WorkingFile ScannedFile(string path, string name, bool hasVirus, string definition,
        DateTime scannedAt = default)
    {
        var file = SimpleFile(path, name);
        file.Metadata.Add(new VirusScanMetadata
        {
            Source = "ClamAV",
            HasVirus = hasVirus,
            VirusFound = hasVirus ? "Eicar-Test-Signature" : null,
            VirusDefinition = definition,
            Timestamp = scannedAt
        });
        return file;
    }

    /// <summary>Provenance from some other tool, in a shape this platform knows nothing about.</summary>
    private static MdSecType ForeignProvenance(string id, string marker)
    {
        var payload = new System.Xml.XmlDocument().CreateElement("ingestEvent", "http://example.org/prov");
        payload.InnerText = marker;
        return new MdSecType
        {
            Id = id,
            MdWrap = new MdSecTypeMdWrap
            {
                Mdtype = MdSecTypeMdWrapMdtype.PremisEvent,
                XmlData = new MdSecTypeMdWrapXmlData { Any = { payload } }
            }
        };
    }

    [Fact]
    public async Task Repeated_Scans_Accumulate_As_Separate_Events_And_The_Latest_Wins()
    {
        // A digiprovMD records an EVENT, and events accumulate: each scan is its own, with its
        // own outcome, and the file's current virus status is the most recent one. Earlier
        // scans stay as history rather than being overwritten.
        const string path = "objects/document.pdf";
        var (metsUri, eTag) = await CreateEmptyMets("premis-scans-accumulate.xml");
        eTag = await AddFile(metsUri, SimpleFile(path, "document.pdf"), eTag);

        eTag = await AddFile(metsUri, ScannedFile(path, "document.pdf", false, "defs-27000"), eTag);
        await AddFile(metsUri, ScannedFile(path, "document.pdf", true, "defs-27932"), eTag);

        var amdSec = XDocument.Load(metsUri.LocalPath).Descendants(MetsNs + "amdSec")
            .Single(a => (string?)a.Attribute("ID") == MetsIds.Adm(path));
        var scans = amdSec.Elements(MetsNs + "digiprovMD")
            .Where(d => (d.Attribute("ID")?.Value ?? "").StartsWith(Constants.VirusProvEventPrefix))
            .ToList();

        scans.Should().HaveCount(2, "each scan is its own event; the first is not overwritten");
        scans.Select(d => (string?)d.Attribute("ID")).Should().OnlyHaveUniqueItems();

        // The reported status is the later scan.
        var parsed = await parser.GetMetsFileWrapper(metsUri);
        var virusScan = parsed.Value!.Files.Single(f => f.LocalPath == path)
            .Metadata.OfType<VirusScanMetadata>().Should().ContainSingle().Subject;
        virusScan.HasVirus.Should().BeTrue();
        virusScan.VirusDefinition.Should().Be("defs-27932");
    }

    [Fact]
    public async Task The_Same_Scan_Read_Twice_Is_Recorded_Once()
    {
        // Reading the same unchanged ClamAV output again is not a second scan - it is the same
        // event, seen again. It happens whenever a file goes through the writer while the last
        // run's output is still in the deposit, and it is what a redelivered pipeline job does
        // to every file in the deposit (issue #221).
        const string path = "objects/document.pdf";
        var scannedAt = new DateTime(2026, 6, 16, 14, 30, 45, DateTimeKind.Utc);
        var (metsUri, eTag) = await CreateEmptyMets("premis-scan-recorded-once.xml");
        eTag = await AddFile(metsUri, SimpleFile(path, "document.pdf"), eTag);

        eTag = await AddFile(metsUri, ScannedFile(path, "document.pdf", false, "defs-27000", scannedAt), eTag);
        await AddFile(metsUri, ScannedFile(path, "document.pdf", false, "defs-27000", scannedAt), eTag);

        ScanEventsFor(metsUri, path).Should().ContainSingle("one scan is one event, however often it is read");
    }

    [Fact]
    public async Task A_Later_Scan_Of_The_Same_File_Is_Still_Recorded()
    {
        // The guard above must not swallow a genuine re-scan, which is the whole point of keeping
        // a history. Their dates are what tell them apart - even when they found exactly the same
        // thing with exactly the same definitions.
        const string path = "objects/document.pdf";
        var firstScan = new DateTime(2026, 6, 16, 14, 30, 45, DateTimeKind.Utc);
        var (metsUri, eTag) = await CreateEmptyMets("premis-rescan-recorded.xml");
        eTag = await AddFile(metsUri, SimpleFile(path, "document.pdf"), eTag);

        eTag = await AddFile(metsUri, ScannedFile(path, "document.pdf", false, "defs-27000", firstScan), eTag);
        await AddFile(metsUri,
            ScannedFile(path, "document.pdf", false, "defs-27000", firstScan.AddDays(30)), eTag);

        ScanEventsFor(metsUri, path).Should().HaveCount(2,
            "\"re-checked a month later, still clean\" is provenance in its own right");
    }

    [Fact]
    public async Task Two_Scans_One_Second_Apart_Are_Both_Recorded()
    {
        // Where the resolution actually runs out. eventDateTime is written to the second, because
        // its source is an S3 last-modified time and that is all S3 stores - so one second is the
        // smallest gap at which two scans of one file are still distinguishable, and this pins it.
        // Closer than this they would share a date and the second would be read as a repeat of the
        // first. That is out of reach in practice rather than merely unlikely: a scan is a whole
        // Brunnhilde/ClamAV run over the deposit, and the deposit lock serialises runs, so two of
        // them cannot start and finish inside the same second. If this test ever needs to assert a
        // finer gap, that reasoning has changed and the dedup key needs to change with it.
        const string path = "objects/document.pdf";
        var firstScan = new DateTime(2026, 6, 16, 14, 30, 45, DateTimeKind.Utc);
        var (metsUri, eTag) = await CreateEmptyMets("premis-rescan-one-second.xml");
        eTag = await AddFile(metsUri, SimpleFile(path, "document.pdf"), eTag);

        eTag = await AddFile(metsUri, ScannedFile(path, "document.pdf", false, "defs-27000", firstScan), eTag);
        await AddFile(metsUri,
            ScannedFile(path, "document.pdf", false, "defs-27000", firstScan.AddSeconds(1)), eTag);

        ScanEventsFor(metsUri, path).Should().HaveCount(2);
    }

    [Fact]
    public async Task A_Scan_That_Does_Not_Say_When_It_Ran_Is_Always_Recorded()
    {
        // Nothing can tell such a scan from a re-scan, and losing a scan is worse than repeating one.
        const string path = "objects/document.pdf";
        var (metsUri, eTag) = await CreateEmptyMets("premis-scan-no-time.xml");
        eTag = await AddFile(metsUri, SimpleFile(path, "document.pdf"), eTag);

        eTag = await AddFile(metsUri, ScannedFile(path, "document.pdf", false, "defs-27000"), eTag);
        await AddFile(metsUri, ScannedFile(path, "document.pdf", false, "defs-27000"), eTag);

        ScanEventsFor(metsUri, path).Should().HaveCount(2);
    }

    private static List<XElement> ScanEventsFor(Uri metsUri, string localPath) =>
        XDocument.Load(metsUri.LocalPath).Descendants(MetsNs + "amdSec")
            .Single(a => (string?)a.Attribute("ID") == MetsIds.Adm(localPath))
            .Elements(MetsNs + "digiprovMD")
            .Where(d => (d.Attribute("ID")?.Value ?? "").StartsWith(Constants.VirusProvEventPrefix))
            .ToList();

    [Fact]
    public async Task Provenance_We_Do_Not_Recognise_Is_Left_Untouched_By_A_Scan()
    {
        // Anything that is not one of our ClamAV events is not ours to interpret, reorder or
        // remove - including an event with no ID at all, which METS permits.
        const string path = "objects/document.pdf";
        var (metsUri, eTag) = await CreateEmptyMets("premis-foreign-digiprov.xml");
        eTag = await AddFile(metsUri, SimpleFile(path, "document.pdf"), eTag);

        var fullMets = (await metsManager.GetFullMets(metsUri, eTag)).Value!;
        var fileAmdSec = fullMets.Mets.AmdSec.Single(a => a.Id == MetsIds.Adm(path));
        fileAmdSec.DigiprovMd.Add(ForeignProvenance("digiprovMD_ingest_doc", "ingested 2026-01-01"));
        fileAmdSec.DigiprovMd.Add(new MdSecType
        {
            MdWrap = new MdSecTypeMdWrap { Mdtype = MdSecTypeMdWrapMdtype.PremisEvent }
        });
        (await metsManager.WriteMets(fullMets)).Success.Should().BeTrue();
        eTag = (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!;

        var scan = await metsManager.HandleSingleFileUpload(
            metsUri, ScannedFile(path, "document.pdf", false, ClamAvDefinition), eTag);
        scan.Success.Should().BeTrue(scan.ErrorMessage ?? "");

        var amdSec = XDocument.Load(metsUri.LocalPath).Descendants(MetsNs + "amdSec")
            .Single(a => (string?)a.Attribute("ID") == MetsIds.Adm(path));
        var digiprovs = amdSec.Elements(MetsNs + "digiprovMD").ToList();

        var foreign = digiprovs.Should()
            .ContainSingle(d => (string?)d.Attribute("ID") == "digiprovMD_ingest_doc").Subject;
        foreign.Descendants().Should().Contain(e => e.Value == "ingested 2026-01-01",
            "a scan must not overwrite provenance written by something else");
        digiprovs.Should().ContainSingle(d => d.Attribute("ID") == null,
            "an ID-less event is legal METS and is left alone");
        digiprovs.Where(d => (d.Attribute("ID")?.Value ?? "").StartsWith(Constants.VirusProvEventPrefix))
            .Should().ContainSingle("the scan is recorded as its own event");

        var parsed = await parser.GetMetsFileWrapper(metsUri);
        parsed.Value!.Files.Single(f => f.LocalPath == path)
            .Metadata.OfType<VirusScanMetadata>().Should().ContainSingle()
            .Which.VirusDefinition.Should().Be(ClamAvDefinition);
    }

    [Fact]
    public async Task Accumulated_Scan_Ids_Do_Not_Collide_With_Another_File_S_Conventional_Id()
    {
        // A file named "…_2" is the shape that a trailing counter would have collided with.
        // Numbering scans between the prefix and the identifier removes that by construction —
        // a conventional ID never has a digit straight after the ClamAV prefix — which is why
        // minting only has to check this file's own amdSec. This pins that the two families of
        // ID stay disjoint.
        const string plain = "objects/a";
        const string lookalike = "objects/a_2";
        var (metsUri, eTag) = await CreateEmptyMets("premis-suffix-collision.xml");
        eTag = await AddFile(metsUri, SimpleFile(plain, "a"), eTag);
        eTag = await AddFile(metsUri, SimpleFile(lookalike, "a_2"), eTag);

        // Two scans of the first file (the second one takes a suffix), then one of the other.
        eTag = await AddFile(metsUri, ScannedFile(plain, "a", false, "defs-1"), eTag);
        eTag = await AddFile(metsUri, ScannedFile(plain, "a", false, "defs-2"), eTag);
        await AddFile(metsUri, ScannedFile(lookalike, "a_2", false, "defs-1"), eTag);

        var ids = XDocument.Load(metsUri.LocalPath)
            .Descendants(MetsNs + "digiprovMD")
            .Select(d => (string?)d.Attribute("ID"))
            .ToList();

        ids.Should().HaveCount(3);
        ids.Should().OnlyHaveUniqueItems("an xs:ID must be unique across the whole document");
    }

    [Fact]
    public async Task One_File_S_Later_Scan_Is_Never_Reported_As_Another_File_S_Status()
    {
        // The victim is a file whose name looks like a numbered scan of its neighbour
        // ("objects/a" rescanned vs a file called "objects/a_2"), which has never been scanned
        // itself and whose techMD is not PREMIS - so it takes the conventional-key fallback.
        // A trailing counter would make the neighbour's second scan carry exactly this file's
        // conventional ID, and its virus status would be reported here.
        const string scanned = "objects/a";
        const string lookalike = "objects/a_2";
        var (metsUri, eTag) = await CreateEmptyMets("premis-cross-file-misattribution.xml");
        eTag = await AddFile(metsUri, SimpleFile(scanned, "a"), eTag);
        eTag = await AddFile(metsUri, SimpleFile(lookalike, "a_2"), eTag);

        // Two scans of the neighbour, the second one INFECTED.
        eTag = await AddFile(metsUri, ScannedFile(scanned, "a", false, "defs-1"), eTag);
        eTag = await AddFile(metsUri, ScannedFile(scanned, "a", true, "defs-2"), eTag);

        // Force the victim onto the fallback path, and never scan it.
        var fullMets = (await metsManager.GetFullMets(metsUri, eTag)).Value!;
        fullMets.Mets.AmdSec.Single(a => a.Id == MetsIds.Adm(lookalike)).TechMd[0].MdWrap.XmlData =
            new MdSecTypeMdWrapXmlData
            {
                Any = { new System.Xml.XmlDocument().CreateElement("mix", "http://www.loc.gov/mix/v20") }
            };
        (await metsManager.WriteMets(fullMets)).Success.Should().BeTrue();

        var parsed = await parser.GetMetsFileWrapper(metsUri);
        parsed.Success.Should().BeTrue(parsed.ErrorMessage ?? "");
        parsed.Value!.Files.Single(f => f.LocalPath == lookalike)
            .Metadata.OfType<VirusScanMetadata>().Should().BeEmpty(
                "this file has never been scanned; its neighbour's result is not its own");
        parsed.Value.Files.Single(f => f.LocalPath == scanned)
            .Metadata.OfType<VirusScanMetadata>().Should().ContainSingle()
            .Which.HasVirus.Should().BeTrue();
    }

    [Fact]
    public async Task The_Conventional_Key_Fallback_Reports_The_LATEST_Scan()
    {
        // When a file's techMD carries no premis:object the structural lookup cannot run, and
        // the parser falls back to the conventional key. That key names the FIRST scan, so
        // without suffix-awareness a rescanned-and-infected file still reported clean — the
        // exact bug the accumulating model exists to prevent, on the fallback path.
        const string path = "objects/document.pdf";
        var (metsUri, eTag) = await CreateEmptyMets("premis-fallback-latest.xml");
        eTag = await AddFile(metsUri, SimpleFile(path, "document.pdf"), eTag);
        eTag = await AddFile(metsUri, ScannedFile(path, "document.pdf", false, "defs-27000"), eTag);
        eTag = await AddFile(metsUri, ScannedFile(path, "document.pdf", true, "defs-27932"), eTag);

        // Replace the PREMIS techMD payload with something the structural path cannot use,
        // leaving the accumulated digiprovMDs where they are.
        var fullMets = (await metsManager.GetFullMets(metsUri, eTag)).Value!;
        var amdSec = fullMets.Mets.AmdSec.Single(a => a.Id == MetsIds.Adm(path));
        amdSec.TechMd[0].MdWrap.XmlData = new MdSecTypeMdWrapXmlData
        {
            Any = { new System.Xml.XmlDocument().CreateElement("mix", "http://www.loc.gov/mix/v20") }
        };
        (await metsManager.WriteMets(fullMets)).Success.Should().BeTrue();

        var parsed = await parser.GetMetsFileWrapper(metsUri);
        parsed.Success.Should().BeTrue(parsed.ErrorMessage ?? "");
        var virusScan = parsed.Value!.Files.Single(f => f.LocalPath == path)
            .Metadata.OfType<VirusScanMetadata>().Should().ContainSingle().Subject;
        virusScan.HasVirus.Should().BeTrue("the later scan found a virus");
        virusScan.VirusDefinition.Should().Be("defs-27932");
    }

    [Fact]
    public async Task An_Upload_Carrying_No_Scan_Adds_No_Event()
    {
        // Only an actual scan produces an event. An ordinary metadata update must not append a
        // duplicate of the last one.
        const string path = "objects/document.pdf";
        var (metsUri, eTag) = await CreateEmptyMets("premis-no-scan-no-event.xml");
        eTag = await AddFile(metsUri, SimpleFile(path, "document.pdf"), eTag);
        eTag = await AddFile(metsUri, ScannedFile(path, "document.pdf", false, ClamAvDefinition), eTag);

        await AddFile(metsUri, SimpleFile(path, "document.pdf"), eTag);

        XDocument.Load(metsUri.LocalPath).Descendants(MetsNs + "amdSec")
            .Single(a => (string?)a.Attribute("ID") == MetsIds.Adm(path))
            .Elements(MetsNs + "digiprovMD").Should().ContainSingle();
    }

    /// <summary>
    /// Exactly one amdSec describes the file at this path. Matched on the PREMIS originalName
    /// inside the techMD, NOT on the amdSec's ID: the regression being guarded against is a
    /// SECOND amdSec for the same file, and a duplicate minted under a different ID scheme
    /// (a raw-path ID alongside an encoded one, in a mixed-format document) is invisible to an
    /// ID-equality check — which is how this assertion was silently defanged once already.
    /// </summary>
    private static void AssertSingleAmdSecFor(XDocument xml, string localPath)
    {
        xml.Descendants(MetsNs + "amdSec")
            .Where(amdSec => amdSec.Descendants(PremisNs + "originalName").Any(n => n.Value == localPath))
            .Should().HaveCount(1, $"exactly one amdSec may describe '{localPath}'");
    }

    [Fact]
    public async Task Overwriting_File_Updates_Virus_Scan_From_Clean_To_Infected()
    {
        // A file previously scanned as clean is re-scanned and found infected (e.g. updated
        // virus definitions). The reported status is the LATER scan; the earlier one stays as
        // history rather than being overwritten — a digiprovMD records an event, and events
        // accumulate (issue #219).

        var (metsUri, eTag) = await CreateEmptyMets("premis-overwrite-virus.xml");

        // First upload: clean scan
        var cleanUpload = new WorkingFile
        {
            LocalPath = "objects/document.pdf",
            Name = "document.pdf",
            Digest = TestDigest,
            Size = 54321,
            Modified = DateTime.UtcNow,
            Metadata =
            [
                new VirusScanMetadata
                {
                    Source = "ClamAV",
                    HasVirus = false,
                    VirusFound = null,
                    VirusDefinition = ClamAvDefinition
                }
            ]
        };
        eTag = await AddFile(metsUri, cleanUpload, eTag);

        var firstParse = await parser.GetMetsFileWrapper(metsUri);
        firstParse.Value!.Files.Single(f => f.LocalPath == "objects/document.pdf")
            .Metadata.OfType<VirusScanMetadata>().Single().HasVirus.Should().BeFalse();

        // Second upload: re-scanned with updated definitions, now infected
        var infectedUpload = new WorkingFile
        {
            LocalPath = "objects/document.pdf",
            Name = "document.pdf",
            Digest = TestDigest,
            Size = 54321,
            Modified = DateTime.UtcNow,
            Metadata =
            [
                new VirusScanMetadata
                {
                    Source = "ClamAV",
                    HasVirus = true,
                    VirusFound = "Win.Test.EICAR_HDB-1",
                    VirusDefinition = "ClamAV 1.4.3/27933/Sat Mar  7 07:24:27 2026"
                }
            ]
        };
        await AddFile(metsUri, infectedUpload, eTag);

        // --- Parser round-trip: infected result present ---
        var secondParse = await parser.GetMetsFileWrapper(metsUri);
        var virus = secondParse.Value!.Files.Single(f => f.LocalPath == "objects/document.pdf")
            .Metadata.OfType<VirusScanMetadata>().Single();
        virus.HasVirus.Should().BeTrue();
        virus.VirusFound.Should().Be("Win.Test.EICAR_HDB-1");

        // --- Raw XML: both scans are recorded, under distinct IDs ---
        var xml = XDocument.Load(metsUri.LocalPath);
        var amdSec = xml.Descendants(MetsNs + "amdSec")
            .Single(a => (string?)a.Attribute("ID") == MetsIds.Adm("objects/document.pdf"));
        var digiprovs = amdSec.Elements(MetsNs + "digiprovMD").ToList();
        digiprovs.Should().HaveCount(2, "the earlier scan is history, not something to overwrite");
        digiprovs.Select(d => (string?)d.Attribute("ID")).Should().OnlyHaveUniqueItems();
    }

    // -----------------------------------------------------------------------
    // ERROR PATHS
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Upload_With_Mismatched_Digests_Returns_BadRequest()
    {
        // Two DigestMetadata entries with different digests are contradictory and indicate
        // a data integrity problem. WorkingFile.GetDigestMetadata() throws MetadataException,
        // which MetadataManager catches and returns as a BadRequest failure.

        var (metsUri, eTag) = await CreateEmptyMets("premis-digest-mismatch.xml");

        var file = new WorkingFile
        {
            LocalPath = "objects/document.pdf",
            Name = "document.pdf",
            Modified = DateTime.UtcNow,
            Metadata =
            [
                new DigestMetadata
                {
                    Source = "S3",
                    Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
                },
                new DigestMetadata
                {
                    Source = "BagIt",
                    Digest = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
                }
            ]
        };

        var result = await metsManager.HandleSingleFileUpload(metsUri, file, eTag);
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.BadRequest);
        result.ErrorMessage.Should().Contain("objects/document.pdf");
    }

    // -----------------------------------------------------------------------
    // ACCESS RESTRICTIONS AND RIGHTS STATEMENT
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Can_Set_And_Get_Objects_Access_Restrictions()
    {
        var (metsUri, eTag) = await CreateEmptyMets("premis-access-restrictions.xml");
        var fullMets = (await metsManager.GetFullMets(metsUri, eTag)).Value!;
        fullMets.Mets.DmdSec.Should().HaveCount(1, "The root dmd section only is created"); 
        
        // Initially empty
        var wrapper = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        wrapper.PhysicalStructure!.AccessRestrictions.Should().BeNull();
        wrapper.PhysicalStructure.Directories.Single(d => d.LocalPath == "objects").AccessRestrictions.Should().BeNull();

        // Set two restrictions on objects and write
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["Restricted", "Leeds only"]);
        await metsManager.WriteMets(fullMets);

        wrapper = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        wrapper.PhysicalStructure!.AccessRestrictions.Should().BeNull();
        var objectsAccessRestrictions = wrapper.PhysicalStructure.Directories
            .Single(d => d.LocalPath == "objects").AccessRestrictions;
        objectsAccessRestrictions.Should().HaveCount(2);
        objectsAccessRestrictions.Should().Contain("Restricted");
        objectsAccessRestrictions.Should().Contain("Leeds only");

        // Replace with a single restriction
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["Open access"]);
        await metsManager.WriteMets(fullMets);
        
        wrapper = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        wrapper.PhysicalStructure!.AccessRestrictions.Should().BeNull();
        objectsAccessRestrictions = wrapper.PhysicalStructure.Directories
            .Single(d => d.LocalPath == "objects").AccessRestrictions;
        objectsAccessRestrictions.Should().ContainSingle().Which.Should().Be("Open access");

        // Clear
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", []);
        await metsManager.WriteMets(fullMets);
        wrapper = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        wrapper.PhysicalStructure!.AccessRestrictions.Should().BeNull();
        objectsAccessRestrictions = wrapper.PhysicalStructure.Directories
            .Single(d => d.LocalPath == "objects").AccessRestrictions;
        objectsAccessRestrictions.Should().BeNull();
    }

    [Fact]
    public async Task Can_Set_And_Get_Root_Rights_Statement()
    {
        var (metsUri, eTag) = await CreateEmptyMets("premis-rights-statement.xml");
        var fullMets = (await metsManager.GetFullMets(metsUri, eTag)).Value!;
    
        // Initially null
        var wrapper = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        wrapper.PhysicalStructure!.RightsStatement.Should().BeNull();
        wrapper.PhysicalStructure.Directories.Single(d => d.LocalPath == "objects").RightsStatement.Should().BeNull();
    
        // Set a rights URI and write
        var ccBy = new Uri("https://creativecommons.org/licenses/by/4.0/");
        metsManager.SetRightsStatementByPath(fullMets, "objects", ccBy);
        await metsManager.WriteMets(fullMets);
        
        wrapper = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        wrapper.PhysicalStructure!.RightsStatement.Should().BeNull();
        var rightsStatement = wrapper.PhysicalStructure.Directories
            .Single(d => d.LocalPath == "objects").RightsStatement;
        rightsStatement.Should().Be(ccBy);
    
        // Update to a different URI
        var inC = new Uri("https://rightsstatements.org/page/InC/1.0/");
        metsManager.SetRightsStatementByPath(fullMets, "objects", inC);
        await metsManager.WriteMets(fullMets);
        
        wrapper = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        wrapper.PhysicalStructure!.RightsStatement.Should().BeNull();
        rightsStatement = wrapper.PhysicalStructure.Directories
            .Single(d => d.LocalPath == "objects").RightsStatement;
        rightsStatement.Should().Be(inC);
        
        // Clear (set to null)
        metsManager.SetRightsStatementByPath(fullMets, "objects", null);
        await metsManager.WriteMets(fullMets);
    
        wrapper = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        wrapper.PhysicalStructure!.RightsStatement.Should().BeNull();
        rightsStatement = wrapper.PhysicalStructure.Directories
            .Single(d => d.LocalPath == "objects").RightsStatement;
        rightsStatement.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // THREAD SAFETY
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Concurrent_Uploads_To_Same_MetsManager_Instance_Do_Not_Cross_Contaminate()
    {
        // MetsManager is registered as a singleton in production.
        // Before this branch's fix, MetadataManager held call-scoped state as instance
        // fields, causing concurrent requests to corrupt each other's PREMIS data.
        // This test verifies the fix: 20 concurrent upload tasks on the same metsManager
        // instance each produce a METS file with their own distinct metadata, with no
        // cross-contamination between tasks.

        const int concurrency = 20;

        var tasks = Enumerable.Range(0, concurrency).Select(async i =>
        {
            // Each task has its own METS file
            var fi = new FileInfo($"Outputs/premis-concurrent-{i:D2}.xml");
            var metsUri = new Uri(fi.FullName);
            var createResult = await metsManager.CreateStandardMets(metsUri, $"Concurrent Test {i}");
            createResult.Success.Should().BeTrue($"task {i} METS creation should succeed");
            var eTag = createResult.Value!.ETag!;

            // Each task uses a distinct PRONOM key and virus definition so cross-contamination
            // would be immediately visible
            var pronomKey = $"fmt/{i:D4}";
            var virusDef = $"ClamAV 1.0/test-{i}";

            // Unique but valid-looking 64-char hex digest per task
            var digest = new string((char)('a' + i % 26), 64);

            var file = new WorkingFile
            {
                LocalPath = "objects/document.pdf",
                Name = "document.pdf",
                ContentType = "application/pdf",
                Digest = digest,
                Size = 1000 + i,
                Modified = DateTime.UtcNow,
                Metadata =
                [
                    new FileFormatMetadata
                    {
                        Source = "Siegfried",
                        PronomKey = pronomKey,
                        FormatName = $"Test Format {i}",
                        Digest = digest,
                        Size = 1000 + i,
                        ContentType = "application/pdf"
                    },
                    new VirusScanMetadata
                    {
                        Source = "ClamAV",
                        HasVirus = false,
                        VirusDefinition = virusDef
                    }
                ]
            };

            var uploadResult = await metsManager.HandleSingleFileUpload(metsUri, file, eTag);
            uploadResult.Success.Should().BeTrue($"task {i} upload should succeed");

            // Verify this task's METS has its own (not a neighbour's) data
            var parseResult = await parser.GetMetsFileWrapper(metsUri);
            parseResult.Success.Should().BeTrue($"task {i} parse should succeed");

            var parsedFile = parseResult.Value!.Files
                .Single(f => f.LocalPath == "objects/document.pdf");

            parsedFile.Metadata.OfType<FileFormatMetadata>().Single()
                .PronomKey.Should().Be(pronomKey,
                    $"task {i} must have its own PRONOM key, not a neighbouring task's");

            parsedFile.Metadata.OfType<VirusScanMetadata>().Single()
                .VirusDefinition.Should().Be(virusDef,
                    $"task {i} must have its own virus definition, not a neighbouring task's");
        });

        await Task.WhenAll(tasks);
    }

    // -----------------------------------------------------------------------
    // METSFILEWRAPPER PARSER ROUND-TRIPS (Name, Agent, Editable,
    // RootAccessConditions, RootRightsStatement)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Parser_Populates_Name_Agent_And_Editable_For_Our_Own_METS()
    {
        // MetsFileWrapper.Name, Agent, and Editable are populated by PopulateFromMets.
        // For METS we created ourselves:
        //   Name   = the agNameFromDeposit passed to CreateStandardMets
        //   Agent  = Constants.MetsCreatorAgent
        //   Editable = true (because Agent matches MetsCreatorAgent)

        var (metsUri, _) = await CreateEmptyMets("parser-name-agent.xml");

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();
        var wrapper = parseResult.Value!;

        wrapper.Name.Should().Be("Test METS");
        wrapper.Agent.Should().Be(Constants.MetsCreatorAgent);
        wrapper.Editable.Should().BeTrue();
    }

    [Fact]
    public async Task Parser_Populates_Access_Conditions_And_Rights_Statement_Via_MetsFileWrapper()
    {
        // MetsManager.SetRootAccessRestrictions / SetRootRightsStatement write MODS
        // accessCondition elements. PopulateFromMets must read these back into
        // ResourceBase.AccessConditions and ResourceBase.RightsStatement.
        // This is the path the UI uses — it reads MetsFileWrapper, not FullMets.

        var (metsUri, eTag) = await CreateEmptyMets("parser-access-conditions.xml");
        var fullMets = (await metsManager.GetFullMets(metsUri, eTag)).Value!;

        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["Restricted", "Leeds only"]);
        metsManager.SetRightsStatementByPath(fullMets, "objects", new Uri("https://creativecommons.org/licenses/by/4.0/"));
        await metsManager.WriteMets(fullMets);

        // Read back via parser (MetsFileWrapper path, not FullMets)
        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();
        var wrapper = parseResult.Value!;

        var objects = wrapper.PhysicalStructure!.Directories.Single(d => d.LocalPath == "objects");
        objects.AccessRestrictions.Should().HaveCount(2);
        objects.AccessRestrictions.Should().Contain("Restricted");
        objects.AccessRestrictions.Should().Contain("Leeds only");
        objects.RightsStatement.Should().Be(new Uri("https://creativecommons.org/licenses/by/4.0/"));

        // Update and re-check
        var fullMets2 = (await metsManager.GetFullMets(metsUri, null)).Value!;
        metsManager.SetAccessRestrictionsByPath(fullMets2, "objects", ["Open access"]);
        metsManager.SetRightsStatementByPath(fullMets2, "objects", null);
        await metsManager.WriteMets(fullMets2);

        var parseResult2 = await parser.GetMetsFileWrapper(metsUri);
        var wrapper2 = parseResult2.Value!;
        objects = wrapper2.PhysicalStructure!.Directories.Single(d => d.LocalPath == "objects");
        objects.AccessRestrictions.Should().ContainSingle().Which.Should().Be("Open access");
        objects.RightsStatement.Should().BeNull();
    }
}
