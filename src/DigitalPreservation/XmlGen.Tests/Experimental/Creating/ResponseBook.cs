using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XmlGen.Tests.Experimental.Creating;

public class ResponseBook
{
    private readonly MetsManager metsManager;

    private const string MetsFilePathBasic = "C:\\git\\uol-dlip\\design\\complex-mets\\response-book.basic.mets.xml";

    public ResponseBook()
    {
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var factory = serviceProvider.GetService<ILoggerFactory>();
        var parserLogger = factory!.CreateLogger<MetsParser>();
        var parser = new MetsParser(new FileSystemMetsLoader(), parserLogger);
        var storage = new FileSystemMetsStorage(parser);
        var premisManager = new PremisManager();
        var premisManagerExif = new PremisManagerExif();
        var premisEventManager = new PremisEventManagerVirus();
        var metadataManager = new MetadataManager(premisManager, premisManagerExif, premisEventManager);
        metsManager = new MetsManager(parser, storage, metadataManager);
    }

    [Fact]
    public async Task Basic_ResponseBook_Mets()
    {
        var metsFi = new FileInfo(MetsFilePathBasic);
        var metsUri = new Uri(metsFi.FullName);
        var mets = await Basic_Response_Book(metsUri);
        await metsManager.WriteMets(mets);
    }

    [Fact(Skip = "Experimental")]
    public async Task Extended_ResponseBook_Mets()
    {
        var metsFi = new FileInfo(MetsFilePathBasic);
        var metsUri = new Uri(metsFi.FullName);
        var mets = await Basic_Response_Book(metsUri);

        // Assign record identifiers to the objects/ folder
        metsManager.SetRecordIdentifier(mets, "objects", "identity-service", "pn67d3ep");
        metsManager.SetRecordIdentifier(mets, "objects", "EMu", "PRI/2/999");

        // Set access condition and rights statement on objects/
        metsManager.SetAccessRestrictions(mets, "objects", ["Level1"]);
        metsManager.SetRightsStatement(mets, "objects", new Uri("http://rightsstatements.org/vocab/InC/1.0/"));

        // Logical structMap: three parts grouping pages, with region pointers on the landscape page 5
        var logSm = new LogicalRange
        {
            Id = "LOG_0000",
            Type = "Collection",
            Name = "Response Book",
            Ranges =
            [
                new LogicalRange
                {
                    Id = "LOG_0001",
                    Type = "Part",
                    Name = "Part 1",
                    RecordInfo = new RecordInfo
                    {
                        RecordIdentifiers =
                        [
                            new RecordIdentifier { Source = "identity-service", Value = "rp4m2q8s" },
                            new RecordIdentifier { Source = "EMu",              Value = "PRI/2/999/a" }
                        ]
                    },
                    Files =
                    [
                        new FilePointer { LocalPath = "objects/001.tif" },
                        new FilePointer { LocalPath = "objects/002.tif" },
                        new FilePointer { LocalPath = "objects/003.tif" }
                    ]
                },
                new LogicalRange
                {
                    Id = "LOG_0002",
                    Type = "Part",
                    Name = "Part 2",
                    RecordInfo = new RecordInfo
                    {
                        RecordIdentifiers =
                        [
                            new RecordIdentifier { Source = "identity-service", Value = "xt7n5k3w" },
                            new RecordIdentifier { Source = "EMu",              Value = "PRI/2/999/b" }
                        ]
                    },
                    Files =
                    [
                        new FilePointer { LocalPath = "objects/004.tif" },
                        // top half of landscape page 5 (6000×4000)
                        new FilePointer { LocalPath = "objects/005.tif", Region = new Rectangle { X1 = 0, Y1 = 0,    X2 = 6000, Y2 = 2000 } }
                    ]
                },
                new LogicalRange
                {
                    Id = "LOG_0003",
                    Type = "Part",
                    Name = "Part 3",
                    RecordInfo = new RecordInfo
                    {
                        RecordIdentifiers =
                        [
                            new RecordIdentifier { Source = "identity-service", Value = "bg9h1j6v" },
                            new RecordIdentifier { Source = "EMu",              Value = "PRI/2/999/c" }
                        ]
                    },
                    Files =
                    [
                        // bottom half of landscape page 5
                        new FilePointer { LocalPath = "objects/005.tif", Region = new Rectangle { X1 = 0, Y1 = 2000, X2 = 6000, Y2 = 4000 } },
                        new FilePointer { LocalPath = "objects/006.tif" },
                        new FilePointer { LocalPath = "objects/007.tif" },
                        new FilePointer { LocalPath = "objects/008.tif" }
                    ]
                }
            ]
        };
        metsManager.SetStructMap(mets, logSm);

        // HTR (handwritten text recognition) XML files — one per page, stored in objects/htr/.
        // These are not referenced in the logical structMap because they are technical derivatives
        // rather than part of the intellectual object in the way that transcripts accompany audio interviews.
        // They inherit access/rights from objects/ but have no logical range to inherit RecordInfo from.
        metsManager.AddToMets(mets, new WorkingDirectory
        {
            LocalPath = "objects/htr",
            Name = "htr"
        });
        foreach (var page in new[] { "001", "002", "003", "004", "005", "006", "007", "008" })
        {
            metsManager.AddToMets(mets, new WorkingFile
            {
                LocalPath = $"objects/htr/{page}.xml",
                Name = $"{page}.xml",
                Digest = $"h{page}xhtr0",
                ContentType = "application/xml",
                Size = 51200,
                Metadata =
                [
                    new FileFormatMetadata
                    {
                        Source = "Brunnhilde",
                        Digest = $"h{page}xhtr0",
                        ContentType = "application/xml",
                        FormatName = "Extensible Markup Language",
                        PronomKey = "fmt/101",
                        Size = 51200
                    }
                ]
            });
        }

        // Link each image page to its HTR XML file as a supplementing resource
        var supplementing = new Uri("http://iiif.io/api/presentation/3#supplementing");
        foreach (var page in new[] { "001", "002", "003", "004", "005", "006", "007", "008" })
        {
            metsManager.LinkFile(mets, $"objects/{page}.tif", $"objects/htr/{page}.xml", supplementing);
        }

        await metsManager.WriteMets(mets);
    }

    private async Task<FullMets> Basic_Response_Book(Uri metsUri)
    {
        var name = "Response Book";
        var result = await metsManager.CreateStandardMets(metsUri, name);

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();
        var metsWrapper = result.Value!;
        var metsResult = await metsManager.GetFullMets(metsUri, metsWrapper.ETag!);
        var mets = metsResult.Value!;
        mets.Should().NotBeNull();

        // 8 pages: portrait (4000x6000) except page 5 which is landscape (6000x4000)
        metsManager.AddToMets(mets, Page("001.tif", "a1b2c3d4", 72000000, 4000, 6000));
        metsManager.AddToMets(mets, Page("002.tif", "b2c3d4e5", 71982000, 4000, 6000));
        metsManager.AddToMets(mets, Page("003.tif", "c3d4e5f6", 72011000, 4000, 6000));
        metsManager.AddToMets(mets, Page("004.tif", "d4e5f6a7", 71993000, 4000, 6000));
        metsManager.AddToMets(mets, Page("005.tif", "e5f6a7b8", 72004000, 6000, 4000)); // landscape
        metsManager.AddToMets(mets, Page("006.tif", "f6a7b8c9", 71987000, 4000, 6000));
        metsManager.AddToMets(mets, Page("007.tif", "a7b8c9d0", 72018000, 4000, 6000));
        metsManager.AddToMets(mets, Page("008.tif", "b8c9d0e1", 71996000, 4000, 6000));

        return mets;
    }

    private static WorkingFile Page(string fileName, string digest, long size, int width, int height)
    {
        return new WorkingFile
        {
            LocalPath = $"objects/{fileName}",
            Name = fileName,
            Digest = digest,
            ContentType = "image/tiff",
            Size = size,
            Metadata =
            [
                new FileFormatMetadata
                {
                    Source = "Brunnhilde",
                    Digest = digest,
                    ContentType = "image/tiff",
                    FormatName = "Tagged Image File Format",
                    PronomKey = "fmt/10",
                    Size = size
                },
                new ExifMetadata
                {
                    Source = "ExifTool",
                    Tags =
                    [
                        new ExifTag { TagName = "FileType",      TagValue = "TIFF" },
                        new ExifTag { TagName = "MIMEType",      TagValue = "image/tiff" },
                        new ExifTag { TagName = "ImageWidth",    TagValue = width.ToString() },
                        new ExifTag { TagName = "ImageHeight",   TagValue = height.ToString() },
                        new ExifTag { TagName = "BitsPerSample", TagValue = "8 8 8" },
                        new ExifTag { TagName = "XResolution",   TagValue = "300" }
                    ]
                },
                // Not necessary to supply ExtentMetadata, but will validate it matches EXIF if supplied
                // It will go in premis:significantProperties from either source.
                new ExtentMetadata 
                {
                    Source = "ExifTool",
                    PixelWidth = width,
                    PixelHeight = height
                },
                new VirusScanMetadata
                {
                    Source = "ClamAV",
                    HasVirus = false,
                    VirusDefinition = "ClamAV 1.4.3/27944/Wed Mar 18 06:24:13 2026"
                }
            ]
        };
    }
}
