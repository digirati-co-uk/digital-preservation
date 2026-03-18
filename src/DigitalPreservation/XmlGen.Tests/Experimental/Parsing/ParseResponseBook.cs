using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XmlGen.Tests.Experimental.Parsing;

public class ParseResponseBook
{
    private readonly MetsParser parser;

    public ParseResponseBook()
    {
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var factory = serviceProvider.GetService<ILoggerFactory>();
        var parserLogger = factory!.CreateLogger<MetsParser>();
        parser = new MetsParser(new FileSystemMetsLoader(), parserLogger);
    }

    [Fact]
    public async Task Can_Parse_Response_Book()
    {
        var metsFile = new FileInfo("Samples/response-book.mets.xml");
        var result = await parser.GetMetsFileWrapper(new Uri(metsFile.FullName));

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Self.Should().NotBeNull();
        result.Value.Self!.Digest.Should().NotBeEmpty();

        result.Value.Name.Should().Be("Response Book");

        var phys = result.Value.PhysicalStructure!;
        phys.Files.Should().Contain(f => f.Name == "response-book.mets.xml");

        phys.Directories.Should().HaveCount(2); // objects/ and metadata/
        var objects = phys.Directories.Single(d => d.Name == "objects");
        objects.Files.Should().HaveCount(8);
        objects.Directories.Should().HaveCount(1);
        var htr = objects.Directories[0];
        htr.Name.Should().Be("htr");
        htr.Files.Should().HaveCount(8);

        // Access condition and rights statement on objects/
        var inCopyright = new Uri("http://rightsstatements.org/vocab/InC/1.0/");
        objects.AccessRestrictions.Should().HaveCount(1);
        objects.AccessRestrictions[0].Should().Be("Level1");
        objects.EffectiveAccessRestrictions.Should().HaveCount(1);
        objects.EffectiveAccessRestrictions[0].Should().Be("Level1");
        objects.RightsStatement.Should().Be(inCopyright);
        objects.EffectiveRightsStatement.Should().Be(inCopyright);

        // Record identifiers assigned to the objects/ folder
        objects.RecordInfo.Should().NotBeNull();
        objects.RecordInfo!.RecordIdentifiers.Should().HaveCount(2);
        objects.RecordInfo.RecordIdentifiers[0].Source.Should().Be("identity-service");
        objects.RecordInfo.RecordIdentifiers[0].Value.Should().Be("pn67d3ep");
        objects.RecordInfo.RecordIdentifiers[1].Source.Should().Be("EMu");
        objects.RecordInfo.RecordIdentifiers[1].Value.Should().Be("PRI/2/999");
        objects.EffectiveRecordInfo.Should().NotBeNull();
        objects.EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("pn67d3ep");
        objects.EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("PRI/2/999");

        // Logical structMap
        result.Value.LogicalStructures.Should().HaveCount(1);
        var logsm = result.Value.LogicalStructures[0];
        logsm.Type.Should().Be("Collection");
        logsm.Name.Should().Be("Response Book");
        logsm.Files.Should().HaveCount(0);
        logsm.Ranges.Should().HaveCount(3);

        // Part 1: pages 1–3 as whole-file pointers
        var part1 = logsm.Ranges[0];
        part1.Type.Should().Be("Part");
        part1.Name.Should().Be("Part 1");
        part1.RecordInfo.Should().NotBeNull();
        part1.RecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        part1.RecordInfo.RecordIdentifiers[0].Value.Should().Be("rp4m2q8s");
        part1.RecordInfo.RecordIdentifiers[1].Source.Should().Be("EMu");
        part1.RecordInfo.RecordIdentifiers[1].Value.Should().Be("PRI/2/999/a");
        part1.Files.Should().HaveCount(3);
        part1.Files[0].LocalPath.Should().Be("objects/001.tif");
        part1.Files[0].Region.Should().BeNull();
        part1.Files[1].LocalPath.Should().Be("objects/002.tif");
        part1.Files[2].LocalPath.Should().Be("objects/003.tif");

        // Part 2: page 4 whole-file, top half of page 5 via Rectangle
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
        part2.Files[1].Region.Y1.Should().Be(0);
        part2.Files[1].Region.X2.Should().Be(6000);
        part2.Files[1].Region.Y2.Should().Be(2000);

        // Part 3: bottom half of page 5 via Rectangle, then pages 6–8 whole-file
        var part3 = logsm.Ranges[2];
        part3.Type.Should().Be("Part");
        part3.Name.Should().Be("Part 3");
        part3.RecordInfo!.RecordIdentifiers[0].Value.Should().Be("bg9h1j6v");
        part3.RecordInfo.RecordIdentifiers[1].Value.Should().Be("PRI/2/999/c");
        part3.Files.Should().HaveCount(4);
        part3.Files[0].LocalPath.Should().Be("objects/005.tif");
        part3.Files[0].Region.Should().NotBeNull();
        part3.Files[0].Region!.X1.Should().Be(0);
        part3.Files[0].Region.Y1.Should().Be(2000);
        part3.Files[0].Region.X2.Should().Be(6000);
        part3.Files[0].Region.Y2.Should().Be(4000);
        part3.Files[1].LocalPath.Should().Be("objects/006.tif");
        part3.Files[1].Region.Should().BeNull();
        part3.Files[2].LocalPath.Should().Be("objects/007.tif");
        part3.Files[3].LocalPath.Should().Be("objects/008.tif");


        // --- EffectiveRecordInfo inheritance ---
        // Pages 1–3: whole-file fptr from Part 1 → inherit Part 1's record info
        objects.Files[0].EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("rp4m2q8s");
        objects.Files[0].EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("PRI/2/999/a");
        objects.Files[1].EffectiveRecordInfo!.RecordIdentifiers[1].Value.Should().Be("PRI/2/999/a");
        objects.Files[2].EffectiveRecordInfo!.RecordIdentifiers[1].Value.Should().Be("PRI/2/999/a");

        // Page 4: whole-file fptr from Part 2 → inherit Part 2's record info
        objects.Files[3].EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("xt7n5k3w");
        objects.Files[3].EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("PRI/2/999/b");

        // Page 5: referenced via area (region) from two ranges → falls back to physical objects/ record info
        objects.Files[4].EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("pn67d3ep");
        objects.Files[4].EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("PRI/2/999");

        // Pages 6–8: whole-file fptr from Part 3 → inherit Part 3's record info
        objects.Files[5].EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("bg9h1j6v");
        objects.Files[5].EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("PRI/2/999/c");
        objects.Files[6].EffectiveRecordInfo!.RecordIdentifiers[1].Value.Should().Be("PRI/2/999/c");
        objects.Files[7].EffectiveRecordInfo!.RecordIdentifiers[1].Value.Should().Be("PRI/2/999/c");

        // All pages inherit access and rights from physical objects/
        for (var i = 0; i < 8; i++)
        {
            objects.Files[i].EffectiveAccessRestrictions.Should().HaveCount(1);
            objects.Files[i].EffectiveAccessRestrictions[0].Should().Be("Level1");
            objects.Files[i].EffectiveRightsStatement.Should().Be(inCopyright);
        }


        // --- Physical files ---

        var page1 = objects.Files[0];
        page1.LocalPath.Should().Be("objects/001.tif");
        page1.Name.Should().Be("001.tif");
        page1.ContentType.Should().Be("image/tiff");
        page1.Digest.Should().Be("a1b2c3d4");
        page1.Size.Should().Be(72000000);

        // No explicit access / rights / record info on individual files
        page1.AccessRestrictions.Should().HaveCount(0);
        page1.RightsStatement.Should().BeNull();
        page1.RecordInfo.Should().BeNull();

        // FileFormatMetadata
        var fmt1 = page1.Metadata.OfType<FileFormatMetadata>().Single();
        fmt1.FormatName.Should().Be("Tagged Image File Format");
        fmt1.PronomKey.Should().Be("fmt/10");
        fmt1.ContentType.Should().Be("image/tiff");
        fmt1.Digest.Should().Be("a1b2c3d4");
        fmt1.Size.Should().Be(72000000);

        // ExifMetadata — spot-check portrait page 1
        var exif1 = page1.Metadata.OfType<ExifMetadata>().Single();
        exif1.Tags.Should().Contain(t => t.TagName == "FileType" && t.TagValue == "TIFF");
        exif1.Tags.Should().Contain(t => t.TagName == "MIMEType" && t.TagValue == "image/tiff");
        exif1.Tags.Should().Contain(t => t.TagName == "ImageWidth" && t.TagValue == "4000");
        exif1.Tags.Should().Contain(t => t.TagName == "ImageHeight" && t.TagValue == "6000");
        exif1.Tags.Should().Contain(t => t.TagName == "BitsPerSample" && t.TagValue == "8 8 8");
        exif1.Tags.Should().Contain(t => t.TagName == "XResolution" && t.TagValue == "300");
        exif1.Tags.Should().HaveCount(6);

        // ExtentMetadata — portrait
        var ext1 = page1.Metadata.OfType<ExtentMetadata>().Single();
        ext1.PixelWidth.Should().Be(4000);
        ext1.PixelHeight.Should().Be(6000);

        // VirusScanMetadata
        var virus1 = page1.Metadata.OfType<VirusScanMetadata>().Single();
        virus1.HasVirus.Should().BeFalse();
        virus1.VirusDefinition.Should().Be("ClamAV 1.4.3/27944/Wed Mar 18 06:24:13 2026");


        // --- Page 5 is landscape (6000×4000) ---

        var page5 = objects.Files[4];
        page5.LocalPath.Should().Be("objects/005.tif");
        page5.ContentType.Should().Be("image/tiff");
        page5.Digest.Should().Be("e5f6a7b8");

        var exif5 = page5.Metadata.OfType<ExifMetadata>().Single();
        exif5.Tags.Should().Contain(t => t.TagName == "ImageWidth" && t.TagValue == "6000");
        exif5.Tags.Should().Contain(t => t.TagName == "ImageHeight" && t.TagValue == "4000");

        var ext5 = page5.Metadata.OfType<ExtentMetadata>().Single();
        ext5.PixelWidth.Should().Be(6000);
        ext5.PixelHeight.Should().Be(4000);

        var virus5 = page5.Metadata.OfType<VirusScanMetadata>().Single();
        virus5.HasVirus.Should().BeFalse();
        virus5.VirusDefinition.Should().Be("ClamAV 1.4.3/27944/Wed Mar 18 06:24:13 2026");


        // --- Verify all 8 pages have the expected local paths and digests ---

        var expectedPages = new[]
        {
            ("objects/001.tif", "a1b2c3d4"),
            ("objects/002.tif", "b2c3d4e5"),
            ("objects/003.tif", "c3d4e5f6"),
            ("objects/004.tif", "d4e5f6a7"),
            ("objects/005.tif", "e5f6a7b8"),
            ("objects/006.tif", "f6a7b8c9"),
            ("objects/007.tif", "a7b8c9d0"),
            ("objects/008.tif", "b8c9d0e1"),
        };

        for (var i = 0; i < 8; i++)
        {
            objects.Files[i].LocalPath.Should().Be(expectedPages[i].Item1);
            objects.Files[i].Digest.Should().Be(expectedPages[i].Item2);
            objects.Files[i].Metadata.OfType<VirusScanMetadata>().Should().HaveCount(1);
            objects.Files[i].Metadata.OfType<ExtentMetadata>().Should().HaveCount(1);
            objects.Files[i].Metadata.OfType<ExifMetadata>().Should().HaveCount(1);
            objects.Files[i].Metadata.OfType<FileFormatMetadata>().Should().HaveCount(1);
        }

        // All portrait pages (not page 5) should be 4000×6000
        var portraitPages = objects.Files.Where((_, i) => i != 4).ToList();
        foreach (var page in portraitPages)
        {
            var ext = page.Metadata.OfType<ExtentMetadata>().Single();
            ext.PixelWidth.Should().Be(4000);
            ext.PixelHeight.Should().Be(6000);
        }


        // --- smLinks: each image page links to its HTR file ---
        var supplementing = new Uri("http://iiif.io/api/presentation/3#supplementing");
        for (var i = 0; i < 8; i++)
        {
            objects.Files[i].Links.Should().HaveCount(1);
            objects.Files[i].Links[0].To.Should().Be($"objects/htr/00{i + 1}.xml");
            objects.Files[i].Links[0].Role.Should().Be(supplementing);
        }


        // --- HTR files ---
        var expectedHtrPages = new[]
        {
            "objects/htr/001.xml", "objects/htr/002.xml", "objects/htr/003.xml",
            "objects/htr/004.xml", "objects/htr/005.xml", "objects/htr/006.xml",
            "objects/htr/007.xml", "objects/htr/008.xml",
        };

        for (var i = 0; i < 8; i++)
        {
            var htrFile = htr.Files[i];
            htrFile.LocalPath.Should().Be(expectedHtrPages[i]);
            htrFile.ContentType.Should().Be("application/xml");
            htrFile.Metadata.OfType<FileFormatMetadata>().Single().PronomKey.Should().Be("fmt/101");

            // HTR files are not referenced in the logical structMap — they are technical derivatives,
            // not part of the intellectual object. They therefore have no logical range to inherit
            // RecordInfo from and fall back to the physical objects/ RecordInfo.
            htrFile.RecordInfo.Should().BeNull();
            htrFile.EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("pn67d3ep");
            htrFile.EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("PRI/2/999");

            // They do inherit access and rights from objects/
            htrFile.EffectiveAccessRestrictions.Should().HaveCount(1);
            htrFile.EffectiveAccessRestrictions[0].Should().Be("Level1");
            htrFile.EffectiveRightsStatement.Should().Be(inCopyright);

            // HTR files are the targets of smLinks, not the sources — they have no outbound links
            htrFile.Links.Should().HaveCount(0);
        }
    }
}
