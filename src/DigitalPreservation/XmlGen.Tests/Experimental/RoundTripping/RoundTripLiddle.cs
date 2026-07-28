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
/// Round-trip test for the Liddle archive scenario: 4 WAV tape sides with ExtentMetadata (Duration),
/// RecordInfo / access / rights on the objects/ folder, and a logical structMap containing
/// time-coded FilePointers (BeginTime / EndTime) — including a range that spans two tape sides.
/// Builds the METS programmatically, writes it, then parses it back and asserts the parsed values
/// exactly match what was written.
/// </summary>
public class RoundTripLiddle
{
    private readonly MetsManager metsManager;
    private readonly MetsParser parser;

    private const string OutputPath = "Outputs/roundtrip-liddle.xml";

    public RoundTripLiddle()
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
    public async Task RoundTrip_Liddle_Physical_Files_With_Duration_Metadata()
    {
        var metsUri = new Uri(new FileInfo(OutputPath).FullName);
        var mets = await BuildLiddleBase(metsUri);
        await metsManager.WriteMets(mets);

        var result = await parser.GetMetsFileWrapper(metsUri);

        result.Success.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Name.Should().Be("Liddle Tapes 1 and 2");

        var phys = result.Value.PhysicalStructure!;
        phys.Directories.Should().HaveCount(2);
        var objects = phys.Directories.Single(d => d.Name == "objects");
        objects.Files.Should().HaveCount(4);

        objects.Files[0].LocalPath.Should().Be("objects/tape1side1.wav");
        objects.Files[0].ContentType.Should().Be("audio/x-wav");
        objects.Files[0].Digest.Should().Be("abcd1234");
        objects.Files[0].Size.Should().Be(500000);
        objects.Files[0].Metadata.OfType<FileFormatMetadata>().Single().PronomKey.Should().Be("fmt/1");
        objects.Files[0].Metadata.OfType<FileFormatMetadata>().Single().FormatName.Should().Be("Broadcast WAVE 0 Generic");
        objects.Files[0].Metadata.OfType<ExtentMetadata>().Single().Duration.Should().Be(2704.7);

        objects.Files[1].LocalPath.Should().Be("objects/tape1side2.wav");
        objects.Files[1].Digest.Should().Be("3e421bb1");
        objects.Files[1].Metadata.OfType<ExtentMetadata>().Single().Duration.Should().Be(2720.1);

        objects.Files[2].LocalPath.Should().Be("objects/tape2side1.wav");
        objects.Files[2].Digest.Should().Be("d4d4e3e3");
        objects.Files[2].Metadata.OfType<ExtentMetadata>().Single().Duration.Should().Be(2700.0);

        objects.Files[3].LocalPath.Should().Be("objects/tape2side2.wav");
        objects.Files[3].Digest.Should().Be("a2d3e4f5");
        objects.Files[3].Metadata.OfType<ExtentMetadata>().Single().Duration.Should().Be(1400.0);

        // No file-to-file links (no structLink in this METS)
        for (var i = 0; i < 4; i++)
            objects.Files[i].Links.Should().HaveCount(0);

        // No logical structMap in the basic variant
        result.Value.LogicalStructures.Should().HaveCount(0);
    }

    [Fact]
    public async Task RoundTrip_Liddle_RecordInfo_Access_Rights_And_Logical_StructMap()
    {
        var metsUri = new Uri(new FileInfo(OutputPath).FullName);
        var mets = await BuildLiddleBase(metsUri);

        var recordInfo = new RecordInfo
        {
            RecordIdentifiers =
            [
                new RecordIdentifier { Source = "identity-service", Value = "a8n9e4c2" },
                new RecordIdentifier { Source = "EMu",              Value = "LIDDLE/WW1/TAPES/1-2" },
            ]
        };
        metsManager.SetRecordInfoByPath(mets, "objects", recordInfo);
        metsManager.SetAccessRestrictionsByPath(mets, "objects", ["Level1"]);
        metsManager.SetRightsStatementByPath(mets, "objects", new Uri("http://rightsstatements.org/vocab/InC/1.0/"));

        var logSm = new LogicalRange
        {
            Id = "LOG_0000",
            Name = "Tapes 1 and 2",
            Type = "Collection",
            Ranges =
            [
                new LogicalRange
                {
                    Id = "LOG_0001",
                    Type = "Item",
                    Name = "ADDAMS-WILLIAMS, DONALD ARTHUR",
                    RecordInfo = new RecordInfo
                    {
                        RecordIdentifiers = [
                            new RecordIdentifier { Source = "identity-service", Value = "mg56cva7" },
                            new RecordIdentifier { Source = "EMu",              Value = "LIDDLE/WW1/XXX/1" }
                        ]
                    },
                    Files = [
                        new FilePointer { LocalPath = "objects/tape1side1.wav", BeginTime = 15.0, EndTime = 2109.5 }
                    ]
                },
                new LogicalRange
                {
                    Id = "LOG_0002",
                    Type = "Item",
                    Name = "AITCHISON, BERTRAM STEWART",
                    RecordInfo = new RecordInfo
                    {
                        RecordIdentifiers = [
                            new RecordIdentifier { Source = "identity-service", Value = "vvdd4433" },
                            new RecordIdentifier { Source = "EMu",              Value = "LIDDLE/WW1/XXX/2" }
                        ]
                    },
                    Files = [
                        new FilePointer { LocalPath = "objects/tape1side1.wav", BeginTime = 2200,  EndTime = 2700  },
                        new FilePointer { LocalPath = "objects/tape1side2.wav", BeginTime = 9.2,   EndTime = 1209  }
                    ]
                },
                new LogicalRange
                {
                    Id = "LOG_0003",
                    Type = "Item",
                    Name = "ALEXANDER, C",
                    RecordInfo = new RecordInfo
                    {
                        RecordIdentifiers = [
                            new RecordIdentifier { Source = "identity-service", Value = "adfa234d" },
                            new RecordIdentifier { Source = "EMu",              Value = "LIDDLE/WW1/XXX/3" }
                        ]
                    },
                    Files = [
                        new FilePointer { LocalPath = "objects/tape1side2.wav", BeginTime = 1250, EndTime = 1900 }
                    ]
                },
                new LogicalRange
                {
                    Id = "LOG_0004",
                    Type = "Item",
                    Name = "ALLANSON, CECIL JOHN LYONS",
                    RecordInfo = new RecordInfo
                    {
                        RecordIdentifiers = [
                            new RecordIdentifier { Source = "identity-service", Value = "e34e2ads" },
                            new RecordIdentifier { Source = "EMu",              Value = "LIDDLE/WW1/XXX/4" }
                        ]
                    },
                    Files = [
                        new FilePointer { LocalPath = "objects/tape2side1.wav", BeginTime = 13.9,  EndTime = 2690.54 },
                        new FilePointer { LocalPath = "objects/tape2side2.wav", BeginTime = 20.6,  EndTime = 1387.51 }
                    ]
                }
            ]
        };
        metsManager.SetStructMap(mets, logSm);
        await metsManager.WriteMets(mets);

        var result = await parser.GetMetsFileWrapper(metsUri);

        result.Success.Should().BeTrue();
        var phys = result.Value!.PhysicalStructure!;
        var objects = phys.Directories.Single(d => d.Name == "objects");
        var inCopyright = new Uri("http://rightsstatements.org/vocab/InC/1.0/");

        // Access restrictions and rights on objects/
        objects.AccessRestrictions.Should().HaveCount(1);
        objects.AccessRestrictions![0].Should().Be("Level1");
        objects.EffectiveAccessRestrictions.Should().HaveCount(1);
        objects.EffectiveAccessRestrictions[0].Should().Be("Level1");
        objects.RightsStatement.Should().Be(inCopyright);
        objects.EffectiveRightsStatement.Should().Be(inCopyright);

        // RecordInfo on objects/
        objects.RecordInfo.Should().NotBeNull();
        objects.RecordInfo!.RecordIdentifiers.Should().HaveCount(2);
        objects.RecordInfo.RecordIdentifiers[0].Source.Should().Be("identity-service");
        objects.RecordInfo.RecordIdentifiers[0].Value.Should().Be("a8n9e4c2");
        objects.RecordInfo.RecordIdentifiers[1].Source.Should().Be("EMu");
        objects.RecordInfo.RecordIdentifiers[1].Value.Should().Be("LIDDLE/WW1/TAPES/1-2");
        objects.EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("a8n9e4c2");

        // Individual files have no explicit DMD — they inherit everything from objects/
        var tape1side1 = phys.FindFile("objects/tape1side1.wav")!;
        var tape1side2 = phys.FindFile("objects/tape1side2.wav")!;
        var tape2side1 = phys.FindFile("objects/tape2side1.wav")!;
        var tape2side2 = phys.FindFile("objects/tape2side2.wav")!;

        tape1side1.AccessRestrictions.Should().BeNull();
        tape1side1.RightsStatement.Should().BeNull();
        tape1side1.RecordInfo.Should().BeNull();

        // All 4 tape sides inherit access and rights from objects/
        foreach (var tape in new[] { tape1side1, tape1side2, tape2side1, tape2side2 })
        {
            tape.EffectiveAccessRestrictions.Should().HaveCount(1);
            tape.EffectiveAccessRestrictions[0].Should().Be("Level1");
            tape.EffectiveRightsStatement.Should().Be(inCopyright);
        }

        // Each tape side is referenced only as a time segment (mets:area), not as a whole-file fptr,
        // so no single logical range "owns" any tape side. RecordInfo therefore falls back to objects/.
        tape1side1.EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("a8n9e4c2");
        tape1side1.EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("LIDDLE/WW1/TAPES/1-2");
        tape2side2.EffectiveRecordInfo!.RecordIdentifiers[1].Value.Should().Be("LIDDLE/WW1/TAPES/1-2");

        // Logical structMap: 1 structMap, root Collection with 4 Item ranges
        result.Value.LogicalStructures.Should().HaveCount(1);
        var logsm = result.Value.LogicalStructures[0];
        logsm.Type.Should().Be("Collection");
        logsm.Name.Should().Be("Tapes 1 and 2");
        logsm.Ranges.Should().HaveCount(4);
        logsm.Files.Should().HaveCount(0);

        // Range 0: ADDAMS-WILLIAMS — single time segment on tape 1 side 1
        logsm.Ranges[0].Type.Should().Be("Item");
        logsm.Ranges[0].Name.Should().Be("ADDAMS-WILLIAMS, DONALD ARTHUR");
        logsm.Ranges[0].RecordInfo!.RecordIdentifiers[0].Value.Should().Be("mg56cva7");
        logsm.Ranges[0].RecordInfo!.RecordIdentifiers[1].Value.Should().Be("LIDDLE/WW1/XXX/1");
        logsm.Ranges[0].Files.Should().HaveCount(1);
        logsm.Ranges[0].Files[0].LocalPath.Should().Be("objects/tape1side1.wav");
        logsm.Ranges[0].Files[0].BeginTime.Should().Be(15.0);
        logsm.Ranges[0].Files[0].EndTime.Should().Be(2109.5);
        logsm.Ranges[0].Files[0].Region.Should().BeNull();
        logsm.Ranges[0].Ranges.Should().HaveCount(0);

        // Range 1: AITCHISON — spans end of tape 1 side 1 and start of tape 1 side 2
        logsm.Ranges[1].Type.Should().Be("Item");
        logsm.Ranges[1].Name.Should().Be("AITCHISON, BERTRAM STEWART");
        logsm.Ranges[1].RecordInfo!.RecordIdentifiers[0].Value.Should().Be("vvdd4433");
        logsm.Ranges[1].RecordInfo!.RecordIdentifiers[1].Value.Should().Be("LIDDLE/WW1/XXX/2");
        logsm.Ranges[1].Files.Should().HaveCount(2);
        logsm.Ranges[1].Files[0].LocalPath.Should().Be("objects/tape1side1.wav");
        logsm.Ranges[1].Files[0].BeginTime.Should().Be(2200.0);
        logsm.Ranges[1].Files[0].EndTime.Should().Be(2700.0);
        logsm.Ranges[1].Files[1].LocalPath.Should().Be("objects/tape1side2.wav");
        logsm.Ranges[1].Files[1].BeginTime.Should().Be(9.2);
        logsm.Ranges[1].Files[1].EndTime.Should().Be(1209.0);

        // Range 2: ALEXANDER — single time segment on tape 1 side 2
        logsm.Ranges[2].Name.Should().Be("ALEXANDER, C");
        logsm.Ranges[2].RecordInfo!.RecordIdentifiers[0].Value.Should().Be("adfa234d");
        logsm.Ranges[2].Files[0].LocalPath.Should().Be("objects/tape1side2.wav");
        logsm.Ranges[2].Files[0].BeginTime.Should().Be(1250.0);
        logsm.Ranges[2].Files[0].EndTime.Should().Be(1900.0);

        // Range 3: ALLANSON — spans most of tape 2 side 1 and start of tape 2 side 2
        logsm.Ranges[3].Name.Should().Be("ALLANSON, CECIL JOHN LYONS");
        logsm.Ranges[3].RecordInfo!.RecordIdentifiers[0].Value.Should().Be("e34e2ads");
        logsm.Ranges[3].Files.Should().HaveCount(2);
        logsm.Ranges[3].Files[0].LocalPath.Should().Be("objects/tape2side1.wav");
        logsm.Ranges[3].Files[0].BeginTime.Should().Be(13.9);
        logsm.Ranges[3].Files[0].EndTime.Should().Be(2690.54);
        logsm.Ranges[3].Files[1].LocalPath.Should().Be("objects/tape2side2.wav");
        logsm.Ranges[3].Files[1].BeginTime.Should().Be(20.6);
        logsm.Ranges[3].Files[1].EndTime.Should().Be(1387.51);

        // Logical ranges have no explicit access/rights and logical roots declare none,
        // so effective values are empty for all ranges
        foreach (var range in logsm.Ranges)
        {
            range.AccessRestrictions.Should().BeNull();
            range.RightsStatement.Should().BeNull();
            range.EffectiveAccessRestrictions.Should().HaveCount(0);
            range.EffectiveRightsStatement.Should().BeNull();
        }
    }

    private async Task<FullMets> BuildLiddleBase(Uri metsUri)
    {
        var result = await metsManager.CreateStandardMets(metsUri, "Liddle Tapes 1 and 2");
        result.Success.Should().BeTrue();
        var metsResult = await metsManager.GetFullMets(metsUri, result.Value!.ETag!);
        var mets = metsResult.Value!;

        metsManager.AddToMets(mets, new WorkingFile
        {
            LocalPath = "objects/tape1side1.wav",
            Digest = "abcd1234",
            ContentType = "audio/x-wav",
            Name = "Tape 1 Side 1",
            Size = 500000,
            Metadata =
            [
                new FileFormatMetadata
                {
                    Source = "Brunnhilde", Digest = "abcd1234", ContentType = "audio/x-wav",
                    FormatName = "Broadcast WAVE 0 Generic", PronomKey = "fmt/1", Size = 500000
                },
                new ExtentMetadata { Source = "FFProbe", Duration = 2704.7 }
            ]
        });
        metsManager.AddToMets(mets, new WorkingFile
        {
            LocalPath = "objects/tape1side2.wav",
            Digest = "3e421bb1",
            ContentType = "audio/x-wav",
            Name = "Tape 1 Side 2",
            Size = 500001,
            Metadata =
            [
                new FileFormatMetadata
                {
                    Source = "Brunnhilde", Digest = "3e421bb1", ContentType = "audio/x-wav",
                    FormatName = "Broadcast WAVE 0 Generic", PronomKey = "fmt/1", Size = 500001
                },
                new ExtentMetadata { Source = "FFProbe", Duration = 2720.1 }
            ]
        });
        metsManager.AddToMets(mets, new WorkingFile
        {
            LocalPath = "objects/tape2side1.wav",
            Digest = "d4d4e3e3",
            ContentType = "audio/x-wav",
            Name = "Tape 2 Side 1",
            Size = 499999,
            Metadata =
            [
                new FileFormatMetadata
                {
                    Source = "Brunnhilde", Digest = "d4d4e3e3", ContentType = "audio/x-wav",
                    FormatName = "Broadcast WAVE 0 Generic", PronomKey = "fmt/1", Size = 499999
                },
                new ExtentMetadata { Source = "FFProbe", Duration = 2700 }
            ]
        });
        metsManager.AddToMets(mets, new WorkingFile
        {
            LocalPath = "objects/tape2side2.wav",
            Digest = "a2d3e4f5",
            ContentType = "audio/x-wav",
            Name = "Tape 2 Side 2",
            Size = 500100,
            Metadata =
            [
                new FileFormatMetadata
                {
                    Source = "Brunnhilde", Digest = "a2d3e4f5", ContentType = "audio/x-wav",
                    FormatName = "Broadcast WAVE 0 Generic", PronomKey = "fmt/1", Size = 500100
                },
                new ExtentMetadata { Source = "FFProbe", Duration = 1400 }
            ]
        });
        return mets;
    }
}
