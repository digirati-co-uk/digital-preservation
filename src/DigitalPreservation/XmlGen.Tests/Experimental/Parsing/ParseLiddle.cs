using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.StorageImpl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XmlGen.Tests.Experimental.Parsing;

public class ParseLiddle
{

    private readonly MetsParser parser;

    public ParseLiddle()
    {
        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var factory = serviceProvider.GetService<ILoggerFactory>();
        var parserLogger = factory!.CreateLogger<MetsParser>();
        var metsLoader = new FileSystemMetsLoader();
        parser = new MetsParser(metsLoader, parserLogger);
    }

    [Fact]
    public async Task Can_Parse_Liddle()
    {
        var liddleMets = new FileInfo("Samples/liddle.mets.xml");
        var result = await parser.GetMetsFileWrapper(new Uri(liddleMets.FullName));

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Self.Should().NotBeNull();
        result.Value.Self!.Digest.Should().NotBeEmpty();
        var phys = result.Value!.PhysicalStructure;
        phys!.Files.Should().Contain(f => f.Name == "liddle.mets.xml");

        result.Value.Name.Should().Be("Liddle Tapes 1 and 2");
        phys.Directories.Should().HaveCount(2);
        var objects = phys.Directories.Single(d => d.Name == "objects");
        objects.Name.Should().Be(FolderNames.Objects);
        objects.Files.Should().HaveCount(4);

        // Access restrictions and rights set on the objects/ folder
        objects.AccessRestrictions.Should().HaveCount(1);
        objects.AccessRestrictions![0].Should().Be("Level1");
        objects.EffectiveAccessRestrictions.Should().HaveCount(1);
        objects.EffectiveAccessRestrictions[0].Should().Be("Level1");
        var inCopyright = new Uri("http://rightsstatements.org/vocab/InC/1.0/");
        objects.RightsStatement.Should().Be(inCopyright);
        objects.EffectiveRightsStatement.Should().Be(inCopyright);

        // Record identifiers on the objects/ folder (the physical cassette pair, LIDDLE/WW1/TAPES/1-2)
        objects.RecordInfo.Should().NotBeNull();
        objects.RecordInfo!.RecordIdentifiers.Should().HaveCount(2);
        objects.RecordInfo.RecordIdentifiers[0].Source.Should().Be("identity-service");
        objects.RecordInfo.RecordIdentifiers[0].Value.Should().Be("a8n9e4c2");
        objects.RecordInfo.RecordIdentifiers[1].Source.Should().Be("EMu");
        objects.RecordInfo.RecordIdentifiers[1].Value.Should().Be("LIDDLE/WW1/TAPES/1-2");
        objects.EffectiveRecordInfo.Should().NotBeNull();
        objects.EffectiveRecordInfo!.RecordIdentifiers.Should().HaveCount(2);
        objects.EffectiveRecordInfo.RecordIdentifiers[0].Source.Should().Be("identity-service");
        objects.EffectiveRecordInfo.RecordIdentifiers[0].Value.Should().Be("a8n9e4c2");
        objects.EffectiveRecordInfo.RecordIdentifiers[1].Source.Should().Be("EMu");
        objects.EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("LIDDLE/WW1/TAPES/1-2");

        // Physical files: 4 WAV files, one per tape side
        objects.Files[0].LocalPath.Should().Be("objects/tape1side1.wav");
        objects.Files[0].ContentType.Should().Be("audio/x-wav");
        objects.Files[0].Digest.Should().Be("abcd1234");
        objects.Files[0].Metadata.OfType<FileFormatMetadata>().Single().PronomKey.Should().Be("fmt/1");
        objects.Files[0].Metadata.OfType<FileFormatMetadata>().Single().FormatName.Should().Be("Broadcast WAVE 0 Generic");
        objects.Files[0].Metadata.OfType<ExtentMetadata>().Single().Duration.Should().Be(2704.7);

        objects.Files[1].LocalPath.Should().Be("objects/tape1side2.wav");
        objects.Files[1].ContentType.Should().Be("audio/x-wav");
        objects.Files[1].Digest.Should().Be("3e421bb1");
        objects.Files[1].Metadata.OfType<ExtentMetadata>().Single().Duration.Should().Be(2720.1);

        objects.Files[2].LocalPath.Should().Be("objects/tape2side1.wav");
        objects.Files[2].ContentType.Should().Be("audio/x-wav");
        objects.Files[2].Digest.Should().Be("d4d4e3e3");
        objects.Files[2].Metadata.OfType<ExtentMetadata>().Single().Duration.Should().Be(2700.0);

        objects.Files[3].LocalPath.Should().Be("objects/tape2side2.wav");
        objects.Files[3].ContentType.Should().Be("audio/x-wav");
        objects.Files[3].Digest.Should().Be("a2d3e4f5");
        objects.Files[3].Metadata.OfType<ExtentMetadata>().Single().Duration.Should().Be(1400.0);

        // No file-to-file links (no structLink section in this METS)
        objects.Files[0].Links.Should().HaveCount(0);
        objects.Files[1].Links.Should().HaveCount(0);
        objects.Files[2].Links.Should().HaveCount(0);
        objects.Files[3].Links.Should().HaveCount(0);

        // No file has an explicit DMD section - all inherit from objects/
        var tape1side1 = phys.FindFile("objects/tape1side1.wav")!;
        var tape1side2 = phys.FindFile("objects/tape1side2.wav")!;
        var tape2side1 = phys.FindFile("objects/tape2side1.wav")!;
        var tape2side2 = phys.FindFile("objects/tape2side2.wav")!;

        tape1side1.AccessRestrictions.Should().BeNull();
        tape1side1.RightsStatement.Should().BeNull();
        tape1side1.RecordInfo.Should().BeNull();

        tape1side2.AccessRestrictions.Should().BeNull();
        tape1side2.RightsStatement.Should().BeNull();
        tape1side2.RecordInfo.Should().BeNull();

        tape2side1.AccessRestrictions.Should().BeNull();
        tape2side1.RightsStatement.Should().BeNull();
        tape2side1.RecordInfo.Should().BeNull();

        tape2side2.AccessRestrictions.Should().BeNull();
        tape2side2.RightsStatement.Should().BeNull();
        tape2side2.RecordInfo.Should().BeNull();

        // All 4 files inherit access and rights from objects/
        tape1side1.EffectiveAccessRestrictions.Should().HaveCount(1);
        tape1side1.EffectiveAccessRestrictions[0].Should().Be("Level1");
        tape1side1.EffectiveRightsStatement.Should().Be(inCopyright);

        tape1side2.EffectiveAccessRestrictions.Should().HaveCount(1);
        tape1side2.EffectiveAccessRestrictions[0].Should().Be("Level1");
        tape1side2.EffectiveRightsStatement.Should().Be(inCopyright);

        tape2side1.EffectiveAccessRestrictions.Should().HaveCount(1);
        tape2side1.EffectiveAccessRestrictions[0].Should().Be("Level1");
        tape2side1.EffectiveRightsStatement.Should().Be(inCopyright);

        tape2side2.EffectiveAccessRestrictions.Should().HaveCount(1);
        tape2side2.EffectiveAccessRestrictions[0].Should().Be("Level1");
        tape2side2.EffectiveRightsStatement.Should().Be(inCopyright);

        // RecordInfo inheritance for the physical files is different from Women of Westminster.
        // In WoW, files are referenced in the logical structMap as whole-file fptrs, so they inherit
        // RecordInfo from their logical range parent. Here, each logical range references files only
        // as time segments (mets:area elements). A tape side spans MULTIPLE interviews, so it cannot
        // belong to any single interview's logical range. Therefore all 4 files fall back to inheriting
        // RecordInfo from the physical objects/ folder (LIDDLE/WW1/TAPES/1-2), not from any logical range.
        tape1side1.EffectiveRecordInfo.Should().NotBeNull();
        tape1side1.EffectiveRecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        tape1side1.EffectiveRecordInfo.RecordIdentifiers[0].Value.Should().Be("a8n9e4c2");
        tape1side1.EffectiveRecordInfo.RecordIdentifiers[1].Source.Should().Be("EMu");
        tape1side1.EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("LIDDLE/WW1/TAPES/1-2");

        tape1side2.EffectiveRecordInfo.Should().NotBeNull();
        tape1side2.EffectiveRecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        tape1side2.EffectiveRecordInfo.RecordIdentifiers[0].Value.Should().Be("a8n9e4c2");
        tape1side2.EffectiveRecordInfo.RecordIdentifiers[1].Source.Should().Be("EMu");
        tape1side2.EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("LIDDLE/WW1/TAPES/1-2");

        tape2side1.EffectiveRecordInfo.Should().NotBeNull();
        tape2side1.EffectiveRecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        tape2side1.EffectiveRecordInfo.RecordIdentifiers[0].Value.Should().Be("a8n9e4c2");
        tape2side1.EffectiveRecordInfo.RecordIdentifiers[1].Source.Should().Be("EMu");
        tape2side1.EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("LIDDLE/WW1/TAPES/1-2");

        tape2side2.EffectiveRecordInfo.Should().NotBeNull();
        tape2side2.EffectiveRecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        tape2side2.EffectiveRecordInfo.RecordIdentifiers[0].Value.Should().Be("a8n9e4c2");
        tape2side2.EffectiveRecordInfo.RecordIdentifiers[1].Source.Should().Be("EMu");
        tape2side2.EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("LIDDLE/WW1/TAPES/1-2");

        // MetsExtensions IDs
        tape1side1.MetsExtensions!.DivId.Should().Be("PHYS_objects/tape1side1.wav");
        tape1side1.MetsExtensions!.AdmId.Should().Be("ADM_objects/tape1side1.wav");

        // Logical structMap: 1 structMap, root is a Collection with 4 child Item ranges
        result.Value.LogicalStructures.Should().HaveCount(1);
        var logsm = result.Value.LogicalStructures[0];
        logsm.Type.Should().Be("Collection");
        logsm.Name.Should().Be("Tapes 1 and 2");
        logsm.Files.Should().HaveCount(0);
        logsm.Ranges.Should().HaveCount(4);

        // Logical ranges have no access or rights declared, and the physical objects/ root
        // (DMD_PHYS_ROOT) declares no access either, so effective values are empty.
        logsm.Ranges[0].AccessRestrictions.Should().BeNull();
        logsm.Ranges[0].RightsStatement.Should().BeNull();
        logsm.Ranges[0].EffectiveAccessRestrictions.Should().HaveCount(0);
        logsm.Ranges[0].EffectiveRightsStatement.Should().BeNull();

        // Range 0: ADDAMS-WILLIAMS - single time segment on tape 1 side 1
        logsm.Ranges[0].Type.Should().Be("Item");
        logsm.Ranges[0].Name.Should().Be("ADDAMS-WILLIAMS, DONALD ARTHUR");
        logsm.Ranges[0].RecordInfo.Should().NotBeNull();
        logsm.Ranges[0].RecordInfo!.RecordIdentifiers.Should().HaveCount(2);
        logsm.Ranges[0].RecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        logsm.Ranges[0].RecordInfo!.RecordIdentifiers[0].Value.Should().Be("mg56cva7");
        logsm.Ranges[0].RecordInfo!.RecordIdentifiers[1].Source.Should().Be("EMu");
        logsm.Ranges[0].RecordInfo!.RecordIdentifiers[1].Value.Should().Be("LIDDLE/WW1/XXX/1");
        logsm.Ranges[0].EffectiveRecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        logsm.Ranges[0].EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("mg56cva7");
        logsm.Ranges[0].Files.Should().HaveCount(1);
        logsm.Ranges[0].Files[0].LocalPath.Should().Be("objects/tape1side1.wav");
        logsm.Ranges[0].Files[0].BeginTime.Should().Be(15.0);
        logsm.Ranges[0].Files[0].EndTime.Should().Be(2109.5);
        logsm.Ranges[0].Files[0].Region.Should().BeNull();
        logsm.Ranges[0].Ranges.Should().HaveCount(0);

        // Range 1: AITCHISON - spans the end of tape 1 side 1 and the start of tape 1 side 2
        logsm.Ranges[1].Type.Should().Be("Item");
        logsm.Ranges[1].Name.Should().Be("AITCHISON, BERTRAM STEWART");
        logsm.Ranges[1].RecordInfo.Should().NotBeNull();
        logsm.Ranges[1].RecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        logsm.Ranges[1].RecordInfo!.RecordIdentifiers[0].Value.Should().Be("vvdd4433");
        logsm.Ranges[1].RecordInfo!.RecordIdentifiers[1].Source.Should().Be("EMu");
        logsm.Ranges[1].RecordInfo!.RecordIdentifiers[1].Value.Should().Be("LIDDLE/WW1/XXX/2");
        logsm.Ranges[1].Files.Should().HaveCount(2);
        logsm.Ranges[1].Files[0].LocalPath.Should().Be("objects/tape1side1.wav");
        logsm.Ranges[1].Files[0].BeginTime.Should().Be(2200.0);
        logsm.Ranges[1].Files[0].EndTime.Should().Be(2700.0);
        logsm.Ranges[1].Files[1].LocalPath.Should().Be("objects/tape1side2.wav");
        logsm.Ranges[1].Files[1].BeginTime.Should().Be(9.2);
        logsm.Ranges[1].Files[1].EndTime.Should().Be(1209.0);
        logsm.Ranges[1].Ranges.Should().HaveCount(0);

        // Range 2: ALEXANDER - single time segment on tape 1 side 2
        logsm.Ranges[2].Type.Should().Be("Item");
        logsm.Ranges[2].Name.Should().Be("ALEXANDER, C");
        logsm.Ranges[2].RecordInfo.Should().NotBeNull();
        logsm.Ranges[2].RecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        logsm.Ranges[2].RecordInfo!.RecordIdentifiers[0].Value.Should().Be("adfa234d");
        logsm.Ranges[2].RecordInfo!.RecordIdentifiers[1].Source.Should().Be("EMu");
        logsm.Ranges[2].RecordInfo!.RecordIdentifiers[1].Value.Should().Be("LIDDLE/WW1/XXX/3");
        logsm.Ranges[2].Files.Should().HaveCount(1);
        logsm.Ranges[2].Files[0].LocalPath.Should().Be("objects/tape1side2.wav");
        logsm.Ranges[2].Files[0].BeginTime.Should().Be(1250.0);
        logsm.Ranges[2].Files[0].EndTime.Should().Be(1900.0);
        logsm.Ranges[2].Ranges.Should().HaveCount(0);

        // Range 3: ALLANSON - spans most of tape 2 side 1 and the start of tape 2 side 2
        logsm.Ranges[3].Type.Should().Be("Item");
        logsm.Ranges[3].Name.Should().Be("ALLANSON, CECIL JOHN LYONS");
        logsm.Ranges[3].RecordInfo.Should().NotBeNull();
        logsm.Ranges[3].RecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        logsm.Ranges[3].RecordInfo!.RecordIdentifiers[0].Value.Should().Be("e34e2ads");
        logsm.Ranges[3].RecordInfo!.RecordIdentifiers[1].Source.Should().Be("EMu");
        logsm.Ranges[3].RecordInfo!.RecordIdentifiers[1].Value.Should().Be("LIDDLE/WW1/XXX/4");
        logsm.Ranges[3].Files.Should().HaveCount(2);
        logsm.Ranges[3].Files[0].LocalPath.Should().Be("objects/tape2side1.wav");
        logsm.Ranges[3].Files[0].BeginTime.Should().Be(13.9);
        logsm.Ranges[3].Files[0].EndTime.Should().Be(2690.54);
        logsm.Ranges[3].Files[1].LocalPath.Should().Be("objects/tape2side2.wav");
        logsm.Ranges[3].Files[1].BeginTime.Should().Be(20.6);
        logsm.Ranges[3].Files[1].EndTime.Should().Be(1387.51);
        logsm.Ranges[3].Ranges.Should().HaveCount(0);
    }
}