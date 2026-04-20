using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XmlGen.Tests.Experimental.RoundTripping;

/// <summary>
/// Round-trip test for the Response Book scenario: 8 TIFF pages (one landscape) with
/// ExifMetadata, ExtentMetadata (PixelWidth/PixelHeight), VirusScanMetadata, and FileFormatMetadata;
/// an HTR subdirectory linked to each page via smLinks; a logical structMap with three parts where
/// page 5 is referenced as image regions (Rectangle) from two different parts; and RecordInfo /
/// access / rights on the objects/ folder with correct EffectiveRecordInfo inheritance.
/// Builds the METS programmatically, writes it, parses it back, and asserts the parsed output.
/// </summary>
public class RoundTripResponseBook
{
    private readonly MetsManager metsManager;
    private readonly MetsParser parser;

    private const string OutputPath = "Outputs/roundtrip-response-book.xml";

    public RoundTripResponseBook()
    {
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var factory = serviceProvider.GetService<ILoggerFactory>();
        var parserLogger = factory!.CreateLogger<MetsParser>();
        parser = new MetsParser(new FileSystemMetsLoader(), parserLogger);
        var storage = new FileSystemMetsStorage(parser);
        var premisManager = new PremisManager();
        var premisManagerExif = new PremisManagerExif();
        var premisEventManager = new PremisEventManagerVirus();
        var metadataManager = new MetadataManager(premisManager, premisManagerExif, premisEventManager);
        metsManager = new MetsManager(parser, storage, metadataManager);
    }

    [Fact]
    public async Task RoundTrip_ResponseBook_Full()
    {
        var metsUri = new Uri(new FileInfo(OutputPath).FullName);

        var result = await metsManager.CreateStandardMets(metsUri, "Response Book");
        result.Success.Should().BeTrue();
        var metsResult = await metsManager.GetFullMets(metsUri, result.Value!.ETag!);
        var mets = metsResult.Value!;

        // 8 pages — portrait (4000×6000) except page 5 which is landscape (6000×4000)
        metsManager.AddToMets(mets, Page("001.tif", "a1b2c3d4", 72000000, 4000, 6000));
        metsManager.AddToMets(mets, Page("002.tif", "b2c3d4e5", 71982000, 4000, 6000));
        metsManager.AddToMets(mets, Page("003.tif", "c3d4e5f6", 72011000, 4000, 6000));
        metsManager.AddToMets(mets, Page("004.tif", "d4e5f6a7", 71993000, 4000, 6000));
        metsManager.AddToMets(mets, Page("005.tif", "e5f6a7b8", 72004000, 6000, 4000));
        metsManager.AddToMets(mets, Page("006.tif", "f6a7b8c9", 71987000, 4000, 6000));
        metsManager.AddToMets(mets, Page("007.tif", "a7b8c9d0", 72018000, 4000, 6000));
        metsManager.AddToMets(mets, Page("008.tif", "b8c9d0e1", 71996000, 4000, 6000));

        // RecordInfo, access, rights on objects/
        metsManager.SetRecordInfoByPath(mets, "objects", new RecordInfo
        {
            RecordIdentifiers =
            [
                new RecordIdentifier { Source = "identity-service", Value = "pn67d3ep" },
                new RecordIdentifier { Source = "EMu",              Value = "PRI/2/999" },
            ]
        });
        metsManager.SetAccessRestrictionsByPath(mets, "objects", ["Level1"]);
        metsManager.SetRightsStatementByPath(mets, "objects", new Uri("http://rightsstatements.org/vocab/InC/1.0/"));

        // Logical structMap
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
                        new FilePointer { LocalPath = "objects/005.tif", Region = new Rectangle { X1 = 0, Y1 = 2000, X2 = 6000, Y2 = 4000 } },
                        new FilePointer { LocalPath = "objects/006.tif" },
                        new FilePointer { LocalPath = "objects/007.tif" },
                        new FilePointer { LocalPath = "objects/008.tif" }
                    ]
                }
            ]
        };
        metsManager.SetStructMap(mets, logSm);

        // HTR XML files — one per page, in objects/htr/
        metsManager.AddToMets(mets, new WorkingDirectory { LocalPath = "objects/htr", Name = "htr" });
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
                        Source = "Brunnhilde", Digest = $"h{page}xhtr0", ContentType = "application/xml",
                        FormatName = "Extensible Markup Language", PronomKey = "fmt/101", Size = 51200
                    }
                ]
            });
        }

        // smLinks: each image page links to its HTR XML file as a transcript
        var transcript = FileLinkRoles.FromIiifProvides("transcript");
        foreach (var page in new[] { "001", "002", "003", "004", "005", "006", "007", "008" })
        {
            metsManager.LinkFile(mets, $"objects/{page}.tif", $"objects/htr/{page}.xml", transcript);
        }

        await metsManager.WriteMets(mets);

        // --- Parse back ---

        var parseResult = await parser.GetMetsFileWrapper(metsUri);
        parseResult.Success.Should().BeTrue();
        var wrapper = parseResult.Value!;

        wrapper.Name.Should().Be("Response Book");

        var phys = wrapper.PhysicalStructure!;
        phys.Directories.Should().HaveCount(2);
        var objects = phys.Directories.Single(d => d.Name == "objects");
        objects.Files.Should().HaveCount(8);
        objects.Directories.Should().HaveCount(1);
        var htr = objects.Directories[0];
        htr.Name.Should().Be("htr");
        htr.Files.Should().HaveCount(8);

        // Access, rights, RecordInfo on objects/
        var inCopyright = new Uri("http://rightsstatements.org/vocab/InC/1.0/");
        objects.AccessRestrictions!.Should().HaveCount(1);
        objects.AccessRestrictions![0].Should().Be("Level1");
        objects.EffectiveAccessRestrictions.Should().HaveCount(1);
        objects.RightsStatement.Should().Be(inCopyright);
        objects.EffectiveRightsStatement.Should().Be(inCopyright);
        objects.RecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        objects.RecordInfo.RecordIdentifiers[0].Value.Should().Be("pn67d3ep");
        objects.RecordInfo.RecordIdentifiers[1].Source.Should().Be("EMu");
        objects.RecordInfo.RecordIdentifiers[1].Value.Should().Be("PRI/2/999");
        objects.EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("pn67d3ep");

        // All 8 pages: verify local path, digest, content type, and all four metadata types
        var expectedPages = new[]
        {
            ("objects/001.tif", "a1b2c3d4", 72000000L),
            ("objects/002.tif", "b2c3d4e5", 71982000L),
            ("objects/003.tif", "c3d4e5f6", 72011000L),
            ("objects/004.tif", "d4e5f6a7", 71993000L),
            ("objects/005.tif", "e5f6a7b8", 72004000L),
            ("objects/006.tif", "f6a7b8c9", 71987000L),
            ("objects/007.tif", "a7b8c9d0", 72018000L),
            ("objects/008.tif", "b8c9d0e1", 71996000L),
        };
        for (var i = 0; i < 8; i++)
        {
            var page = objects.Files[i];
            page.LocalPath.Should().Be(expectedPages[i].Item1);
            page.Digest.Should().Be(expectedPages[i].Item2);
            page.Size.Should().Be(expectedPages[i].Item3);
            page.ContentType.Should().Be("image/tiff");
            page.Metadata.OfType<FileFormatMetadata>().Should().HaveCount(1);
            page.Metadata.OfType<ExifMetadata>().Should().HaveCount(1);
            page.Metadata.OfType<ExtentMetadata>().Should().HaveCount(1);
            page.Metadata.OfType<VirusScanMetadata>().Should().HaveCount(1);
        }

        // Spot-check page 1 metadata in detail
        var page1 = objects.Files[0];
        var fmt1 = page1.Metadata.OfType<FileFormatMetadata>().Single();
        fmt1.FormatName.Should().Be("Tagged Image File Format");
        fmt1.PronomKey.Should().Be("fmt/10");
        fmt1.Digest.Should().Be("a1b2c3d4");

        var exif1 = page1.Metadata.OfType<ExifMetadata>().Single();
        exif1.Tags.Should().Contain(t => t.TagName == "FileType"      && t.TagValue == "TIFF");
        exif1.Tags.Should().Contain(t => t.TagName == "MIMEType"      && t.TagValue == "image/tiff");
        exif1.Tags.Should().Contain(t => t.TagName == "ImageWidth"    && t.TagValue == "4000");
        exif1.Tags.Should().Contain(t => t.TagName == "ImageHeight"   && t.TagValue == "6000");
        exif1.Tags.Should().Contain(t => t.TagName == "BitsPerSample" && t.TagValue == "8 8 8");
        exif1.Tags.Should().Contain(t => t.TagName == "XResolution"   && t.TagValue == "300");
        exif1.Tags.Should().HaveCount(6);

        var ext1 = page1.Metadata.OfType<ExtentMetadata>().Single();
        ext1.PixelWidth.Should().Be(4000);
        ext1.PixelHeight.Should().Be(6000);

        var virus1 = page1.Metadata.OfType<VirusScanMetadata>().Single();
        virus1.HasVirus.Should().BeFalse();
        virus1.VirusDefinition.Should().Be("ClamAV 1.4.3/27944/Wed Mar 18 06:24:13 2026");

        // Page 5 is landscape (6000×4000)
        var page5 = objects.Files[4];
        page5.Metadata.OfType<ExifMetadata>().Single().Tags
            .Should().Contain(t => t.TagName == "ImageWidth" && t.TagValue == "6000");
        page5.Metadata.OfType<ExtentMetadata>().Single().PixelWidth.Should().Be(6000);
        page5.Metadata.OfType<ExtentMetadata>().Single().PixelHeight.Should().Be(4000);

        // All portrait pages (not page 5) are 4000×6000
        for (var i = 0; i < 8; i++)
        {
            if (i == 4) continue;
            var ext = objects.Files[i].Metadata.OfType<ExtentMetadata>().Single();
            ext.PixelWidth.Should().Be(4000);
            ext.PixelHeight.Should().Be(6000);
        }

        // The Page() helper supplies BOTH ExifMetadata (ImageWidth/ImageHeight tags) AND an explicit
        // ExtentMetadata (PixelWidth/PixelHeight). Both sources give the same values, so they are accepted
        // and combined into a single set of premis:significantProperties. A conflict would throw at write time.

        // smLinks: each page file links to its HTR file
        for (var i = 0; i < 8; i++)
        {
            objects.Files[i].Links.Should().HaveCount(1);
            objects.Files[i].Links[0].To.Should().Be($"objects/htr/00{i + 1}.xml");
            objects.Files[i].Links[0].Role.Should().Be(transcript);
        }

        // HTR files: no outbound links, inherit RecordInfo from objects/ (not in any logical range)
        for (var i = 0; i < 8; i++)
        {
            var htrFile = htr.Files[i];
            htrFile.ContentType.Should().Be("application/xml");
            htrFile.Metadata.OfType<FileFormatMetadata>().Single().PronomKey.Should().Be("fmt/101");
            htrFile.Links.Should().HaveCount(0);
            htrFile.RecordInfo.Should().BeNull();
            htrFile.EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("pn67d3ep");
            htrFile.EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("PRI/2/999");
            htrFile.EffectiveAccessRestrictions.Should().HaveCount(1);
            htrFile.EffectiveAccessRestrictions[0].Should().Be("Level1");
            htrFile.EffectiveRightsStatement.Should().Be(inCopyright);
        }

        // All pages inherit access and rights from objects/ (none have explicit overrides)
        for (var i = 0; i < 8; i++)
        {
            objects.Files[i].AccessRestrictions.Should().BeNull();
            objects.Files[i].RightsStatement.Should().BeNull();
            objects.Files[i].EffectiveAccessRestrictions.Should().HaveCount(1);
            objects.Files[i].EffectiveAccessRestrictions[0].Should().Be("Level1");
            objects.Files[i].EffectiveRightsStatement.Should().Be(inCopyright);
        }

        // Logical structMap
        wrapper.LogicalStructures.Should().HaveCount(1);
        var logsm = wrapper.LogicalStructures[0];
        logsm.Type.Should().Be("Collection");
        logsm.Name.Should().Be("Response Book");
        logsm.Files.Should().HaveCount(0);
        logsm.Ranges.Should().HaveCount(3);

        var part1 = logsm.Ranges[0];
        part1.Type.Should().Be("Part");
        part1.Name.Should().Be("Part 1");
        part1.RecordInfo!.RecordIdentifiers[0].Value.Should().Be("rp4m2q8s");
        part1.RecordInfo.RecordIdentifiers[1].Value.Should().Be("PRI/2/999/a");
        part1.Files.Should().HaveCount(3);
        part1.Files[0].LocalPath.Should().Be("objects/001.tif");
        part1.Files[0].Region.Should().BeNull();
        part1.Files[1].LocalPath.Should().Be("objects/002.tif");
        part1.Files[2].LocalPath.Should().Be("objects/003.tif");

        var part2 = logsm.Ranges[1];
        part2.Type.Should().Be("Part");
        part2.Name.Should().Be("Part 2");
        part2.RecordInfo!.RecordIdentifiers[0].Value.Should().Be("xt7n5k3w");
        part2.RecordInfo.RecordIdentifiers[1].Value.Should().Be("PRI/2/999/b");
        part2.Files.Should().HaveCount(2);
        part2.Files[0].LocalPath.Should().Be("objects/004.tif");
        part2.Files[0].Region.Should().BeNull();
        part2.Files[1].LocalPath.Should().Be("objects/005.tif");
        part2.Files[1].Region.Should().NotBeNull();
        part2.Files[1].Region!.X1.Should().Be(0);
        part2.Files[1].Region!.Y1.Should().Be(0);
        part2.Files[1].Region!.X2.Should().Be(6000);
        part2.Files[1].Region!.Y2.Should().Be(2000);

        var part3 = logsm.Ranges[2];
        part3.Type.Should().Be("Part");
        part3.Name.Should().Be("Part 3");
        part3.RecordInfo!.RecordIdentifiers[0].Value.Should().Be("bg9h1j6v");
        part3.RecordInfo.RecordIdentifiers[1].Value.Should().Be("PRI/2/999/c");
        part3.Files.Should().HaveCount(4);
        part3.Files[0].LocalPath.Should().Be("objects/005.tif");
        part3.Files[0].Region!.X1.Should().Be(0);
        part3.Files[0].Region!.Y1.Should().Be(2000);
        part3.Files[0].Region!.X2.Should().Be(6000);
        part3.Files[0].Region!.Y2.Should().Be(4000);
        part3.Files[1].LocalPath.Should().Be("objects/006.tif");
        part3.Files[2].LocalPath.Should().Be("objects/007.tif");
        part3.Files[3].LocalPath.Should().Be("objects/008.tif");

        // EffectiveRecordInfo inheritance:
        // Pages 1–3 are whole-file fptrs from Part 1 → inherit Part 1's record info
        objects.Files[0].EffectiveRecordInfo!.RecordIdentifiers[1].Value.Should().Be("PRI/2/999/a");
        objects.Files[1].EffectiveRecordInfo!.RecordIdentifiers[1].Value.Should().Be("PRI/2/999/a");
        objects.Files[2].EffectiveRecordInfo!.RecordIdentifiers[1].Value.Should().Be("PRI/2/999/a");

        // Page 4 is a whole-file fptr from Part 2 → inherits Part 2's record info
        objects.Files[3].EffectiveRecordInfo!.RecordIdentifiers[1].Value.Should().Be("PRI/2/999/b");

        // Page 5 is referenced via area (region) from two ranges → falls back to physical objects/ record info
        objects.Files[4].EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("pn67d3ep");
        objects.Files[4].EffectiveRecordInfo!.RecordIdentifiers[1].Value.Should().Be("PRI/2/999");

        // Pages 6–8 are whole-file fptrs from Part 3 → inherit Part 3's record info
        objects.Files[5].EffectiveRecordInfo!.RecordIdentifiers[1].Value.Should().Be("PRI/2/999/c");
        objects.Files[6].EffectiveRecordInfo!.RecordIdentifiers[1].Value.Should().Be("PRI/2/999/c");
        objects.Files[7].EffectiveRecordInfo!.RecordIdentifiers[1].Value.Should().Be("PRI/2/999/c");
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
                    Source = "Brunnhilde", Digest = digest, ContentType = "image/tiff",
                    FormatName = "Tagged Image File Format", PronomKey = "fmt/10", Size = size
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
                new ExtentMetadata { Source = "ExifTool", PixelWidth = width, PixelHeight = height },
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
