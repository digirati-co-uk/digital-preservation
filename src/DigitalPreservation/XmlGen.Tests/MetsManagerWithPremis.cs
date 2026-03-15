using System.Xml.Linq;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Storage.Repository.Common.Mets;
using Storage.Repository.Common.Mets.StorageImpl;

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
            .Single(f => (string?)f.Attribute("ID") == "FILE_objects/document.pdf");
        fileEl.Attribute("MIMETYPE")?.Value.Should().Be("application/pdf");

        // amdSec: has techMD with PREMIS, but no digiprovMD (no virus event)
        var amdSec = xml.Descendants(MetsNs + "amdSec")
            .Single(a => (string?)a.Attribute("ID") == "ADM_objects/document.pdf");
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
            .Single(a => (string?)a.Attribute("ID") == "ADM_objects/document.pdf");
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
            .Single(a => (string?)a.Attribute("ID") == "ADM_objects/document.pdf")
            .Descendants(PremisNs + "object").Single();
        premisObject.Descendants(PremisNs + "format").Should().HaveCount(1);

        // --- Also: only one amdSec for this file ---
        xml.Descendants(MetsNs + "amdSec")
            .Where(a => ((string?)a.Attribute("ID"))!.Contains("objects/document.pdf"))
            .Should().HaveCount(1);
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
            .Single(a => (string?)a.Attribute("ID") == "ADM_objects/document.pdf");

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
            .Single(a => (string?)a.Attribute("ID") == "ADM_objects/suspect.exe")
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
            .Single(a => (string?)a.Attribute("ID") == "ADM_objects/photo.tif")
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
            .Single(a => (string?)a.Attribute("ID") == "ADM_objects/recording.mp3")
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
            .Single(a => (string?)a.Attribute("ID") == "ADM_objects/photo.tif");
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
        var xml = XDocument.Load(metsUri.LocalPath);
        xml.Descendants(MetsNs + "amdSec")
            .Where(a => ((string?)a.Attribute("ID"))?.Contains("objects/document.pdf") == true)
            .Should().HaveCount(1, "overwriting a file must not create a second amdSec");
    }

    [Fact]
    public async Task Overwriting_File_Updates_Virus_Scan_From_Clean_To_Infected()
    {
        // A file previously scanned as clean is re-scanned and found infected
        // (e.g., updated virus definitions). The digiprovMD must be updated in place,
        // not duplicated.

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

        // --- Raw XML: still exactly one digiprovMD (not two) ---
        var xml = XDocument.Load(metsUri.LocalPath);
        var amdSec = xml.Descendants(MetsNs + "amdSec")
            .Single(a => (string?)a.Attribute("ID") == "ADM_objects/document.pdf");
        amdSec.Elements(MetsNs + "digiprovMD").Should().HaveCount(1);
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
    public async Task Can_Set_And_Get_Root_Access_Restrictions()
    {
        var (metsUri, eTag) = await CreateEmptyMets("premis-access-restrictions.xml");
        var fullMets = (await metsManager.GetFullMets(metsUri, eTag)).Value!;

        // Initially empty
        metsManager.GetRootAccessRestrictions(fullMets).Should().BeEmpty();

        // Set two restrictions and write
        metsManager.SetRootAccessRestrictions(fullMets, ["Restricted", "Leeds only"]);
        await metsManager.WriteMets(fullMets);

        // Read back — passing null for eTag bypasses the ETag check after our own write
        var fullMets2 = (await metsManager.GetFullMets(metsUri, null)).Value!;
        var restrictions = metsManager.GetRootAccessRestrictions(fullMets2);
        restrictions.Should().HaveCount(2);
        restrictions.Should().Contain("Restricted");
        restrictions.Should().Contain("Leeds only");

        // Replace with a single restriction
        metsManager.SetRootAccessRestrictions(fullMets2, ["Open access"]);
        await metsManager.WriteMets(fullMets2);

        var fullMets3 = (await metsManager.GetFullMets(metsUri, null)).Value!;
        var restrictions2 = metsManager.GetRootAccessRestrictions(fullMets3);
        restrictions2.Should().ContainSingle().Which.Should().Be("Open access");

        // Clear
        metsManager.SetRootAccessRestrictions(fullMets3, []);
        await metsManager.WriteMets(fullMets3);

        var fullMets4 = (await metsManager.GetFullMets(metsUri, null)).Value!;
        metsManager.GetRootAccessRestrictions(fullMets4).Should().BeEmpty();
    }

    [Fact]
    public async Task Can_Set_And_Get_Root_Rights_Statement()
    {
        var (metsUri, eTag) = await CreateEmptyMets("premis-rights-statement.xml");
        var fullMets = (await metsManager.GetFullMets(metsUri, eTag)).Value!;

        // Initially null
        metsManager.GetRootRightsStatement(fullMets).Should().BeNull();

        // Set a rights URI and write
        var ccBy = new Uri("https://creativecommons.org/licenses/by/4.0/");
        metsManager.SetRootRightsStatement(fullMets, ccBy);
        await metsManager.WriteMets(fullMets);

        var fullMets2 = (await metsManager.GetFullMets(metsUri, null)).Value!;
        metsManager.GetRootRightsStatement(fullMets2).Should().Be(ccBy);

        // Update to a different URI
        var inC = new Uri("https://rightsstatements.org/page/InC/1.0/");
        metsManager.SetRootRightsStatement(fullMets2, inC);
        await metsManager.WriteMets(fullMets2);

        var fullMets3 = (await metsManager.GetFullMets(metsUri, null)).Value!;
        metsManager.GetRootRightsStatement(fullMets3).Should().Be(inC);

        // Clear (set to null)
        metsManager.SetRootRightsStatement(fullMets3, null);
        await metsManager.WriteMets(fullMets3);

        var fullMets4 = (await metsManager.GetFullMets(metsUri, null)).Value!;
        metsManager.GetRootRightsStatement(fullMets4).Should().BeNull();
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
        // MetsFileWrapper.RootAccessConditions and RootRightsStatement.
        // This is the path the UI uses — it reads MetsFileWrapper, not FullMets.

        var (metsUri, eTag) = await CreateEmptyMets("parser-access-conditions.xml");
        var fullMets = (await metsManager.GetFullMets(metsUri, eTag)).Value!;

        metsManager.SetRootAccessRestrictions(fullMets, ["Restricted", "Leeds only"]);
        metsManager.SetRootRightsStatement(fullMets, new Uri("https://creativecommons.org/licenses/by/4.0/"));
        await metsManager.WriteMets(fullMets);

        // Read back via parser (MetsFileWrapper path, not FullMets)
        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();
        var wrapper = parseResult.Value!;

        wrapper.RootAccessConditions.Should().HaveCount(2);
        wrapper.RootAccessConditions.Should().Contain("Restricted");
        wrapper.RootAccessConditions.Should().Contain("Leeds only");
        wrapper.RootRightsStatement.Should().Be(new Uri("https://creativecommons.org/licenses/by/4.0/"));

        // Update and re-check
        var fullMets2 = (await metsManager.GetFullMets(metsUri, null)).Value!;
        metsManager.SetRootAccessRestrictions(fullMets2, ["Open access"]);
        metsManager.SetRootRightsStatement(fullMets2, null);
        await metsManager.WriteMets(fullMets2);

        var parseResult2 = await parser.GetMetsFileWrapper(metsUri);
        var wrapper2 = parseResult2.Value!;
        wrapper2.RootAccessConditions.Should().ContainSingle().Which.Should().Be("Open access");
        wrapper2.RootRightsStatement.Should().BeNull();
    }
}
