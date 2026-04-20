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
/// Round-trip tests for the Women of Westminster scenario: mixed audio and Word-document files,
/// a logical structMap with whole-file FilePointers and RecordInfo on each range, access override
/// on an individual file (angela-eagle-redacted.m4a set to "Closed"), smLinks connecting audio
/// files to their transcripts, and a rights statement set directly on a logical div by its ID
/// (using SetRightsStatementByDivId — see note below).
///
/// Note on SetRightsStatementByPath vs SetRightsStatementByDivId:
/// The Extended_Wow_Mets Creating test calls SetRightsStatementByPath(mets, "LOG_0002", ...).
/// However, SetRightsStatementByPath navigates the PHYSICAL structMap by constructing PHYS_-prefixed
/// IDs from path segments, so "LOG_0002" is interpreted as a path component and the call silently
/// resolves to the physical root div rather than the intended logical div. The correct API for
/// addressing a logical div by its ID is SetRightsStatementByDivId. These round-trip tests use
/// SetRightsStatementByDivId to exercise and verify this feature correctly.
/// </summary>
public class RoundTripWomenOfWestminster
{
    private readonly MetsManager metsManager;
    private readonly MetsParser parser;

    private const string OutputPath = "Outputs/roundtrip-wow.xml";

    public RoundTripWomenOfWestminster()
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
    public async Task RoundTrip_WomenOfWestminster_Physical_Files_Only()
    {
        var metsUri = new Uri(new FileInfo(OutputPath).FullName);
        var mets = await BuildWoWBase(metsUri);
        await metsManager.WriteMets(mets);

        var result = await parser.GetMetsFileWrapper(metsUri);

        result.Success.Should().BeTrue();
        result.Value!.Name.Should().Be("Women of Westminster");

        var phys = result.Value.PhysicalStructure!;
        phys.Directories.Should().HaveCount(2);
        var objects = phys.Directories.Single(d => d.Name == "objects");
        objects.Files.Should().HaveCount(5);

        // MetsManager sorts physical divs by Label (case-insensitive).
        // Labels here: "Amber Rudd Transcript.docx" < "Amber Rudd.m4a" < "Angela Eagle - redacted.m4a"
        //            < "Angela Eagle redacted transcript.docx" < "Angela Eagle.m4a"
        objects.Files[0].LocalPath.Should().Be("objects/amber-rudd.docx");
        objects.Files[0].ContentType.Should().Be("application/msword");
        objects.Files[0].Metadata.OfType<FileFormatMetadata>().Single().PronomKey.Should().Be("fmt/200");
        objects.Files[0].Metadata.OfType<FileFormatMetadata>().Single().FormatName.Should().Be("Microsoft Word");

        objects.Files[1].LocalPath.Should().Be("objects/amber-rudd.m4a");
        objects.Files[1].ContentType.Should().Be("audio/m4a");
        objects.Files[1].Digest.Should().Be("abcd1234");
        objects.Files[1].Metadata.OfType<FileFormatMetadata>().Single().PronomKey.Should().Be("fmt/100");

        objects.Files[2].LocalPath.Should().Be("objects/angela-eagle-redacted.m4a");
        objects.Files[3].LocalPath.Should().Be("objects/angela-eagle-transcript.docx");
        objects.Files[4].LocalPath.Should().Be("objects/angela-eagle.m4a");

        // No logical structMap in the basic variant
        result.Value.LogicalStructures.Should().HaveCount(0);

        // No links — smLinks are added in the extended variant
        for (var i = 0; i < 5; i++)
            objects.Files[i].Links.Should().HaveCount(0);
    }

    [Fact]
    public async Task RoundTrip_WomenOfWestminster_Extended()
    {
        var metsUri = new Uri(new FileInfo(OutputPath).FullName);
        var mets = await BuildWoWBase(metsUri);

        // RecordInfo, access, rights on objects/
        metsManager.SetRecordInfoByPath(mets, "objects", new RecordInfo
        {
            RecordIdentifiers =
            [
                new RecordIdentifier { Source = "identity-service", Value = "b6n9e4c2" },
                new RecordIdentifier { Source = "EMu",              Value = "MS 2249" },
            ]
        });
        metsManager.SetAccessRestrictionsByPath(mets, "objects", ["Level1"]);
        metsManager.SetRightsStatementByPath(mets, "objects", new Uri("http://rightsstatements.org/vocab/InC/1.0/"));

        // The redacted audio is "Closed"; the rights statement is explicitly cleared (null)
        metsManager.SetAccessRestrictionsByPath(mets, "objects/angela-eagle-redacted.m4a", ["Closed"]);
        metsManager.SuppressRightsInheritanceByPath(mets, "objects/angela-eagle-redacted.m4a");

        // Logical structMap
        var logSm = new LogicalRange
        {
            Id = "LOG_0000",
            Name = "Women of Westminster",
            Type = "Collection",
            Ranges =
            [
                new LogicalRange
                {
                    Id = "LOG_0001",
                    Type = "Item",
                    Name = "Amber Rudd",
                    RecordInfo = new RecordInfo
                    {
                        RecordIdentifiers =
                        [
                            new RecordIdentifier { Source = "identity-service", Value = "mg56cva7" },
                            new RecordIdentifier { Source = "EMu",              Value = "MS 2249/1" }
                        ]
                    },
                    Files =
                    [
                        new FilePointer { LocalPath = "objects/amber-rudd.m4a" },
                        new FilePointer { LocalPath = "objects/amber-rudd.docx" }
                    ]
                },
                new LogicalRange
                {
                    Id = "LOG_0002",
                    Type = "Item",
                    Name = "Angela Eagle",
                    RecordInfo = new RecordInfo
                    {
                        RecordIdentifiers =
                        [
                            new RecordIdentifier { Source = "identity-service", Value = "hh43pd32" },
                            new RecordIdentifier { Source = "EMu",              Value = "MS 2249/2" }
                        ]
                    },
                    Files =
                    [
                        new FilePointer { LocalPath = "objects/angela-eagle-redacted.m4a" },
                        new FilePointer { LocalPath = "objects/angela-eagle-transcript.docx" }
                    ]
                }
            ]
        };
        metsManager.SetStructMap(mets, logSm);

        // Rights statement on logical div LOG_0002 directly, using ByDivId (not ByPath).
        // SetRightsStatementByPath("LOG_0002", ...) would silently address the physical root
        // because LocateMetsDivByLocalPath only navigates the PHYSICAL structMap.
        metsManager.SetRightsStatementByDivId(mets, "LOG_0002", new Uri("http://rightsstatements.org/vocab/InC/1.0/"));

        // smLinks
        var transcript = FileLinkRoles.FromIiifProvides("transcript");
        metsManager.LinkFile(mets, "objects/amber-rudd.m4a",          "objects/amber-rudd.docx",                 transcript);
        metsManager.LinkFile(mets, "objects/angela-eagle-redacted.m4a", "objects/angela-eagle-transcript.docx",  transcript);

        await metsManager.WriteMets(mets);

        // --- Parse back ---

        var result = await parser.GetMetsFileWrapper(metsUri);
        result.Success.Should().BeTrue();
        var wrapper = result.Value!;
        wrapper.Name.Should().Be("Women of Westminster");

        var phys = wrapper.PhysicalStructure!;
        var objects = phys.Directories.Single(d => d.Name == "objects");
        objects.Files.Should().HaveCount(5);

        // Access, rights, RecordInfo on objects/
        var inCopyright = new Uri("http://rightsstatements.org/vocab/InC/1.0/");
        objects.AccessRestrictions!.Should().HaveCount(1);
        objects.AccessRestrictions![0].Should().Be("Level1");
        objects.EffectiveAccessRestrictions.Should().HaveCount(1);
        objects.RightsStatement.Should().Be(inCopyright);
        objects.EffectiveRightsStatement.Should().Be(inCopyright);
        objects.RecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        objects.RecordInfo.RecordIdentifiers[0].Value.Should().Be("b6n9e4c2");
        objects.RecordInfo.RecordIdentifiers[1].Source.Should().Be("EMu");
        objects.RecordInfo.RecordIdentifiers[1].Value.Should().Be("MS 2249");

        // Physical file order: sorted by Label (case-insensitive) by MetsManager.
        // "Amber Rudd Transcript.docx" < "Amber Rudd.m4a" < "Angela Eagle - redacted.m4a"
        // < "Angela Eagle redacted transcript.docx" < "Angela Eagle.m4a"
        objects.Files[0].LocalPath.Should().Be("objects/amber-rudd.docx");
        objects.Files[1].LocalPath.Should().Be("objects/amber-rudd.m4a");
        objects.Files[2].LocalPath.Should().Be("objects/angela-eagle-redacted.m4a");
        objects.Files[3].LocalPath.Should().Be("objects/angela-eagle-transcript.docx");
        objects.Files[4].LocalPath.Should().Be("objects/angela-eagle.m4a");

        // angela-eagle.m4a is NOT in the logical structMap and has no explicit DMD entry
        var eagleAudio       = phys.FindFile("objects/angela-eagle.m4a")!;
        var ruddAudio        = phys.FindFile("objects/amber-rudd.m4a")!;
        var ruddTranscript   = phys.FindFile("objects/amber-rudd.docx")!;
        var eagleRedacted    = phys.FindFile("objects/angela-eagle-redacted.m4a")!;
        var eagleTranscript  = phys.FindFile("objects/angela-eagle-transcript.docx")!;

        // No explicit DMD on most files
        ruddAudio.RecordInfo.Should().BeNull();
        ruddAudio.AccessRestrictions.Should().BeNull();
        ruddAudio.RightsStatement.Should().BeNull();

        ruddTranscript.RecordInfo.Should().BeNull();
        eagleAudio.RecordInfo.Should().BeNull();
        eagleTranscript.RecordInfo.Should().BeNull();

        // angela-eagle-redacted.m4a has explicit access override and explicit null rights
        eagleRedacted.RecordInfo.Should().BeNull();
        eagleRedacted.AccessRestrictions.Should().HaveCount(1);
        eagleRedacted.AccessRestrictions![0].Should().Be("Closed");
        eagleRedacted.RightsStatement.Should().BeNull();

        // Effective values: amber-rudd files inherit RecordInfo from logical range LOG_0001 (whole-file fptr)
        ruddAudio.EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("mg56cva7");
        ruddAudio.EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("MS 2249/1");
        ruddAudio.EffectiveAccessRestrictions.Should().HaveCount(1);
        ruddAudio.EffectiveAccessRestrictions[0].Should().Be("Level1");
        ruddAudio.EffectiveRightsStatement.Should().Be(inCopyright);

        ruddTranscript.EffectiveRecordInfo!.RecordIdentifiers[1].Value.Should().Be("MS 2249/1");
        ruddTranscript.EffectiveAccessRestrictions[0].Should().Be("Level1");
        ruddTranscript.EffectiveRightsStatement.Should().Be(inCopyright);

        // angela-eagle-redacted and its transcript inherit RecordInfo from logical range LOG_0002
        eagleRedacted.EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("hh43pd32");
        eagleRedacted.EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("MS 2249/2");
        // Access is the explicitly set "Closed" override
        eagleRedacted.EffectiveAccessRestrictions.Should().HaveCount(1);
        eagleRedacted.EffectiveAccessRestrictions[0].Should().Be("Closed");
        // Rights was explicitly set to null — effective is also null
        eagleRedacted.EffectiveRightsStatement.Should().BeNull();

        eagleTranscript.EffectiveRecordInfo!.RecordIdentifiers[1].Value.Should().Be("MS 2249/2");
        eagleTranscript.EffectiveAccessRestrictions[0].Should().Be("Level1");
        eagleTranscript.EffectiveRightsStatement.Should().Be(inCopyright);

        // angela-eagle.m4a is not a whole-file fptr in any logical range, so RecordInfo falls back
        // to the physical objects/ folder (MS 2249)
        eagleAudio.EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("b6n9e4c2");
        eagleAudio.EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("MS 2249");
        eagleAudio.EffectiveAccessRestrictions.Should().HaveCount(1);
        eagleAudio.EffectiveAccessRestrictions[0].Should().Be("Level1");
        eagleAudio.EffectiveRightsStatement.Should().Be(inCopyright);

        // smLinks
        ruddAudio.Links.Should().HaveCount(1);
        ruddAudio.Links[0].To.Should().Be("objects/amber-rudd.docx");
        ruddAudio.Links[0].Role.Should().Be(transcript);

        ruddTranscript.Links.Should().HaveCount(0);
        eagleAudio.Links.Should().HaveCount(0);

        eagleRedacted.Links.Should().HaveCount(1);
        eagleRedacted.Links[0].To.Should().Be("objects/angela-eagle-transcript.docx");
        eagleRedacted.Links[0].Role.Should().Be(transcript);

        eagleTranscript.Links.Should().HaveCount(0);

        // Logical structMap
        wrapper.LogicalStructures.Should().HaveCount(1);
        var logsm = wrapper.LogicalStructures[0];
        logsm.Type.Should().Be("Collection");
        logsm.Name.Should().Be("Women of Westminster");
        // Name is the value set in LogicalRange.Name, written as both LABEL and MODS title;
        // the parser prefers MODS title, so this is "Women of Westminster" (not a sample-file title)
        logsm.Files.Should().HaveCount(0);
        logsm.Ranges.Should().HaveCount(2);

        // Range 0: Amber Rudd
        var range0 = logsm.Ranges[0];
        range0.Type.Should().Be("Item");
        range0.Name.Should().Be("Amber Rudd");
        range0.RecordInfo!.RecordIdentifiers[0].Source.Should().Be("identity-service");
        range0.RecordInfo.RecordIdentifiers[0].Value.Should().Be("mg56cva7");
        range0.RecordInfo.RecordIdentifiers[1].Source.Should().Be("EMu");
        range0.RecordInfo.RecordIdentifiers[1].Value.Should().Be("MS 2249/1");
        range0.EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("mg56cva7");
        range0.Files.Should().HaveCount(2);
        range0.Files[0].LocalPath.Should().Be("objects/amber-rudd.m4a");
        range0.Files[0].BeginTime.Should().BeNull();
        range0.Files[0].EndTime.Should().BeNull();
        range0.Files[0].Region.Should().BeNull();
        range0.Files[1].LocalPath.Should().Be("objects/amber-rudd.docx");
        range0.Ranges.Should().HaveCount(0);
        // Logical ranges have no explicit access/rights — effective is empty from this level
        range0.AccessRestrictions.Should().BeNull();
        range0.EffectiveAccessRestrictions.Should().HaveCount(0);
        range0.EffectiveRightsStatement.Should().BeNull();

        // Range 1: Angela Eagle — has a rights statement set directly on the logical div
        var range1 = logsm.Ranges[1];
        range1.Type.Should().Be("Item");
        range1.Name.Should().Be("Angela Eagle");
        range1.RecordInfo!.RecordIdentifiers[0].Value.Should().Be("hh43pd32");
        range1.RecordInfo.RecordIdentifiers[1].Value.Should().Be("MS 2249/2");
        range1.Files.Should().HaveCount(2);
        range1.Files[0].LocalPath.Should().Be("objects/angela-eagle-redacted.m4a");
        range1.Files[1].LocalPath.Should().Be("objects/angela-eagle-transcript.docx");
        range1.Ranges.Should().HaveCount(0);
        // Rights set explicitly on LOG_0002 via SetRightsStatementByDivId
        range1.RightsStatement.Should().Be(inCopyright);
        range1.EffectiveRightsStatement.Should().Be(inCopyright);
        // No access restrictions were set on this logical range
        range1.AccessRestrictions.Should().BeNull();
        range1.EffectiveAccessRestrictions.Should().HaveCount(0);
    }

    /// <summary>
    /// Exercises two scenarios not covered by the other WoW round-trip tests:
    ///
    /// 1. Adding a WorkingDirectory and WorkingFile to an already-written METS (session 2 adds
    ///    objects/supplementary/ to a METS that was fully set up and saved in session 1).
    ///
    /// 2. The "parse → modify LogicalRange in memory → GetFullMets → SetStructMap → write" cycle:
    ///    session 3 reads the parsed structMap back from disk, appends a new child range that
    ///    points to the session-2 file, then re-applies the whole structMap via SetStructMap.
    /// </summary>
    [Fact]
    public async Task RoundTrip_WomenOfWestminster_MultiSession()
    {
        var metsUri = new Uri(new FileInfo("Outputs/roundtrip-wow-multisession.xml").FullName);
        var transcript = FileLinkRoles.FromIiifProvides("transcript");
        var inCopyright = new Uri("http://rightsstatements.org/vocab/InC/1.0/");

        // ── Session 1: build the full extended WoW state and write ──────────────────

        var mets = await BuildWoWBase(metsUri);

        metsManager.SetRecordInfoByPath(mets, "objects", new RecordInfo
        {
            RecordIdentifiers =
            [
                new RecordIdentifier { Source = "identity-service", Value = "b6n9e4c2" },
                new RecordIdentifier { Source = "EMu",              Value = "MS 2249" },
            ]
        });
        metsManager.SetAccessRestrictionsByPath(mets, "objects", ["Level1"]);
        metsManager.SetRightsStatementByPath(mets, "objects", inCopyright);
        metsManager.SetAccessRestrictionsByPath(mets, "objects/angela-eagle-redacted.m4a", ["Closed"]);
        metsManager.SuppressRightsInheritanceByPath(mets, "objects/angela-eagle-redacted.m4a");

        var logSm = new LogicalRange
        {
            Id = "LOG_0000",
            Name = "Women of Westminster",
            Type = "Collection",
            Ranges =
            [
                new LogicalRange
                {
                    Id = "LOG_0001",
                    Type = "Item",
                    Name = "Amber Rudd",
                    RecordInfo = new RecordInfo
                    {
                        RecordIdentifiers =
                        [
                            new RecordIdentifier { Source = "identity-service", Value = "mg56cva7" },
                            new RecordIdentifier { Source = "EMu",              Value = "MS 2249/1" }
                        ]
                    },
                    Files = [ new FilePointer { LocalPath = "objects/amber-rudd.m4a" } ]
                },
                new LogicalRange
                {
                    Id = "LOG_0002",
                    Type = "Item",
                    Name = "Angela Eagle",
                    RecordInfo = new RecordInfo
                    {
                        RecordIdentifiers =
                        [
                            new RecordIdentifier { Source = "identity-service", Value = "hh43pd32" },
                            new RecordIdentifier { Source = "EMu",              Value = "MS 2249/2" }
                        ]
                    },
                    Files = [ new FilePointer { LocalPath = "objects/angela-eagle-redacted.m4a" } ]
                }
            ]
        };
        metsManager.SetStructMap(mets, logSm);
        metsManager.LinkFile(mets, "objects/amber-rudd.m4a", "objects/amber-rudd.docx", transcript);
        await metsManager.WriteMets(mets);

        // ── Session 2: reload from disk and add a new folder + file ─────────────────

        var session2 = (await metsManager.GetFullMets(metsUri, null)).Value!;

        metsManager.AddToMets(session2, new WorkingDirectory
        {
            LocalPath = "objects/supplementary",
            Name = "Supplementary"
        });
        metsManager.AddToMets(session2, new WorkingFile
        {
            LocalPath = "objects/supplementary/bercow-notes.txt",
            Digest = "b42a6e9c",
            ContentType = "text/plain",
            Name = "Speaker notes.txt",
            Size = 200,
            Metadata =
            [
                new FileFormatMetadata
                {
                    Source = "Brunnhilde", Digest = "b42a6e9c", ContentType = "text/plain",
                    FormatName = "Plain Text", PronomKey = "x-fmt/111", Size = 200
                }
            ]
        });
        await metsManager.WriteMets(session2);

        // ── Verify session 2: original files intact, new folder + file visible ──────

        var parse2 = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        var objects2 = parse2.PhysicalStructure!.Directories.Single(d => d.Name == "objects");
        objects2.Files.Should().HaveCount(5);
        objects2.Directories.Should().HaveCount(1);
        var supplementary = objects2.Directories[0];
        supplementary.Name.Should().Be("Supplementary");
        supplementary.Files.Should().HaveCount(1);
        supplementary.Files[0].LocalPath.Should().Be("objects/supplementary/bercow-notes.txt");
        supplementary.Files[0].ContentType.Should().Be("text/plain");
        supplementary.Files[0].Metadata.OfType<FileFormatMetadata>().Single().PronomKey.Should().Be("x-fmt/111");

        // The session-1 structMap is still intact after the session-2 write
        parse2.LogicalStructures.Should().HaveCount(1);
        parse2.LogicalStructures[0].Ranges.Should().HaveCount(2);

        // New file inherits access/rights from objects/ since no explicit override
        supplementary.Files[0].EffectiveAccessRestrictions.Should().HaveCount(1);
        supplementary.Files[0].EffectiveAccessRestrictions[0].Should().Be("Level1");
        supplementary.Files[0].EffectiveRightsStatement.Should().Be(inCopyright);

        // ── Session 3: parse structMap, add new range, re-apply via SetStructMap ────

        // Obtain the current logical structMap from the parser model, then add a new child range
        // that points to the session-2 file. This exercises the "parse → mutate → re-apply" cycle.
        var parsedLogSm = parse2.LogicalStructures[0];
        parsedLogSm.Ranges.Add(new LogicalRange
        {
            Id = "LOG_0003",
            Type = "Item",
            Name = "Speaker Notes",
            RecordInfo = new RecordInfo
            {
                RecordIdentifiers =
                [
                    new RecordIdentifier { Source = "identity-service", Value = "a2c4e5b1" },
                    new RecordIdentifier { Source = "EMu",              Value = "MS 2249/SN" }
                ]
            },
            Files = [ new FilePointer { LocalPath = "objects/supplementary/bercow-notes.txt" } ]
        });

        var session3 = (await metsManager.GetFullMets(metsUri, null)).Value!;
        metsManager.SetStructMap(session3, parsedLogSm);
        await metsManager.WriteMets(session3);

        // ── Verify session 3: updated structMap with three ranges ─────────────────────

        var parse3 = (await parser.GetMetsFileWrapper(metsUri)).Value!;

        // Physical structure unchanged
        var objects3 = parse3.PhysicalStructure!.Directories.Single(d => d.Name == "objects");
        objects3.Files.Should().HaveCount(5);
        objects3.Directories.Should().HaveCount(1);
        objects3.Directories[0].Files.Should().HaveCount(1);

        // Three logical ranges
        parse3.LogicalStructures.Should().HaveCount(1);
        var logsm3 = parse3.LogicalStructures[0];
        logsm3.Ranges.Should().HaveCount(3);

        logsm3.Ranges[0].Name.Should().Be("Amber Rudd");
        logsm3.Ranges[0].RecordInfo!.RecordIdentifiers[0].Value.Should().Be("mg56cva7");

        logsm3.Ranges[1].Name.Should().Be("Angela Eagle");
        logsm3.Ranges[1].RecordInfo!.RecordIdentifiers[0].Value.Should().Be("hh43pd32");

        var speakerNotes = logsm3.Ranges[2];
        speakerNotes.Name.Should().Be("Speaker Notes");
        speakerNotes.RecordInfo!.RecordIdentifiers[0].Value.Should().Be("a2c4e5b1");
        speakerNotes.RecordInfo.RecordIdentifiers[1].Value.Should().Be("MS 2249/SN");
        speakerNotes.Files.Should().HaveCount(1);
        speakerNotes.Files[0].LocalPath.Should().Be("objects/supplementary/bercow-notes.txt");

        // bercow-notes.txt now has effective RecordInfo from the Speaker Notes logical range
        var bercow = objects3.Directories[0].Files[0];
        bercow.EffectiveRecordInfo!.RecordIdentifiers[0].Value.Should().Be("a2c4e5b1");
        bercow.EffectiveRecordInfo.RecordIdentifiers[1].Value.Should().Be("MS 2249/SN");
        bercow.EffectiveAccessRestrictions.Should().HaveCount(1);
        bercow.EffectiveAccessRestrictions[0].Should().Be("Level1");
        bercow.EffectiveRightsStatement.Should().Be(inCopyright);
    }

    private async Task<FullMets> BuildWoWBase(Uri metsUri)
    {
        var result = await metsManager.CreateStandardMets(metsUri, "Women of Westminster");
        result.Success.Should().BeTrue();
        var metsResult = await metsManager.GetFullMets(metsUri, result.Value!.ETag!);
        var mets = metsResult.Value!;

        metsManager.AddToMets(mets, new WorkingFile
        {
            LocalPath = "objects/amber-rudd.m4a",
            Digest = "abcd1234",
            ContentType = "audio/m4a",
            Name = "Amber Rudd.m4a",
            Size = 1000,
            Metadata =
            [
                new FileFormatMetadata
                {
                    Source = "Brunnhilde", Digest = "abcd1234", ContentType = "audio/m4a",
                    FormatName = "M4A Audio", PronomKey = "fmt/100", Size = 1000
                }
            ]
        });
        metsManager.AddToMets(mets, new WorkingFile
        {
            LocalPath = "objects/amber-rudd.docx",
            Digest = "1234abcd",
            ContentType = "application/msword",
            Name = "Amber Rudd Transcript.docx",
            Size = 500,
            Metadata =
            [
                new FileFormatMetadata
                {
                    Source = "Brunnhilde", Digest = "1234abcd", ContentType = "application/msword",
                    FormatName = "Microsoft Word", PronomKey = "fmt/200", Size = 500
                }
            ]
        });
        metsManager.AddToMets(mets, new WorkingFile
        {
            LocalPath = "objects/angela-eagle.m4a",
            Digest = "aabbccdd",
            ContentType = "audio/m4a",
            Name = "Angela Eagle.m4a",
            Size = 2000,
            Metadata =
            [
                new FileFormatMetadata
                {
                    Source = "Brunnhilde", Digest = "aabbccdd", ContentType = "audio/m4a",
                    FormatName = "M4A Audio", PronomKey = "fmt/100", Size = 2000
                }
            ]
        });
        metsManager.AddToMets(mets, new WorkingFile
        {
            LocalPath = "objects/angela-eagle-redacted.m4a",
            Digest = "99887766",
            ContentType = "audio/m4a",
            Name = "Angela Eagle - redacted.m4a",
            Size = 1500,
            Metadata =
            [
                new FileFormatMetadata
                {
                    Source = "Brunnhilde", Digest = "99887766", ContentType = "audio/m4a",
                    FormatName = "M4A Audio", PronomKey = "fmt/100", Size = 1500
                }
            ]
        });
        metsManager.AddToMets(mets, new WorkingFile
        {
            LocalPath = "objects/angela-eagle-transcript.docx",
            Digest = "a1b2c3d4",
            ContentType = "application/msword",
            Name = "Angela Eagle redacted transcript.docx",
            Size = 600,
            Metadata =
            [
                new FileFormatMetadata
                {
                    Source = "Brunnhilde", Digest = "a1b2c3d4", ContentType = "application/msword",
                    FormatName = "Microsoft Word", PronomKey = "fmt/200", Size = 600
                }
            ]
        });
        return mets;
    }
}
