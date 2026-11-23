using System.Xml.Linq;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.AccessConditions;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.AccessConditions;
using DigitalPreservation.Mets.StorageImpl;
using DigitalPreservation.Workspace.Requests;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XmlGen.Tests.AccessConditions;

/// <summary>
/// The METS writer's half of issue #433: on every write, <see cref="MetsManager.ApplyDerivedAccessConditions"/>
/// rewrites the resolver's managed types across the whole document from each div's EFFECTIVE
/// chosen conditions - restriction and statement - and leaves everything else alone.
/// </summary>
public class DerivedAccessConditionsWriterTests
{
    private static readonly XNamespace MetsNs = "http://www.loc.gov/METS/";
    private static readonly XNamespace ModsNs = "http://www.loc.gov/mods/v3";
    private const string Statement = TestProfiles.CopyrightStatementType;

    private static readonly Uri NoCUs = new("http://rightsstatements.org/vocab/NoC-US/1.0/");
    private static readonly Uri InC = new("http://rightsstatements.org/vocab/InC/1.0/");

    private class StubProvider(AccessConditionProfile? profile) : IAccessConditionProfileProvider
    {
        public AccessConditionProfile? Profile { get; set; } = profile;
        public bool Unavailable { get; set; }
        public Task<AccessConditionProfile?> GetProfileAsync(CancellationToken cancellationToken = default) =>
            Unavailable
                ? throw new AccessConditionProfileUnavailableException("API down")
                : Task.FromResult(Profile);
    }

    private readonly MetsParser parser;
    private readonly StubProvider provider = new(TestProfiles.Leeds());
    private readonly MetsManager metsManager;

    public DerivedAccessConditionsWriterTests()
    {
        var factory = new ServiceCollection().AddLogging().BuildServiceProvider().GetRequiredService<ILoggerFactory>();
        parser = new MetsParser(new FileSystemMetsLoader(), factory.CreateLogger<MetsParser>());
        var metsStorage = new FileSystemMetsStorage(parser);
        var metadataManager = new MetadataManager(new PremisManager(), new PremisManagerExif(), new PremisEventManagerVirus());
        metsManager = new MetsManager(parser, metsStorage, metadataManager,
            derivedAccessConditionResolver: new ProfileDerivedAccessConditionResolver(provider));
    }

    private async Task<(Uri metsUri, FullMets fullMets)> CreateMets(string outputFile)
    {
        var metsUri = new Uri(new FileInfo(outputFile).FullName);
        var createResult = await metsManager.CreateStandardMets(metsUri, "Derived Test");
        var fullMets = (await metsManager.GetFullMets(metsUri, createResult.Value!.ETag!)).Value!;
        metsManager.AddToMets(fullMets, new WorkingFile
        {
            LocalPath = "objects/a.tif", Name = "a.tif", ContentType = "image/tiff",
            Digest = "aaaa", Size = 1, Modified = DateTime.UtcNow
        });
        metsManager.AddToMets(fullMets, new WorkingFile
        {
            LocalPath = "objects/c.tif", Name = "c.tif", ContentType = "image/tiff",
            Digest = "bbbb", Size = 1, Modified = DateTime.UtcNow
        });
        return (metsUri, fullMets);
    }

    private async Task<FullMets> Reload(Uri metsUri) =>
        (await metsManager.GetFullMets(metsUri, (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!)).Value!;

    private static List<string> TypedConditions(Uri metsUri, string dmdId, string type)
    {
        var doc = XDocument.Load(metsUri.LocalPath);
        var dmd = doc.Descendants(MetsNs + "dmdSec").SingleOrDefault(d => (string?)d.Attribute("ID") == dmdId);
        if (dmd == null) return [];
        return dmd.Descendants(ModsNs + "accessCondition")
            .Where(ac => (string?)ac.Attribute("type") == type)
            .Select(ac => ac.Value).ToList();
    }

    private void SetStatement(FullMets fullMets, string path, string? statement) =>
        metsManager.SetAccessStatementByPath(fullMets, path, Statement, statement).Success.Should().BeTrue();

    private void PutAInARange(FullMets fullMets, List<string>? access, string? statement) =>
        metsManager.SetStructMap(fullMets, new LogicalRange
        {
            Id = "LOG_ROOT", Type = "Collection", Name = "Root",
            Ranges =
            [
                new LogicalRange
                {
                    Id = "LOG_ITEM", Type = "Item", Name = "Item",
                    AccessRestrictions = access ?? [],
                    OtherAccessConditions = statement == null ? null : new Dictionary<string, string> { [Statement] = statement },
                    Files = [new FilePointer { LocalPath = "objects/a.tif" }]
                }
            ]
        }, Statement).Success.Should().BeTrue();

    /// <summary>
    /// The invariant the review of #434 asked for: after a write, every node with an effective
    /// input reads back exactly the derived value its EFFECTIVE restriction and statement give.
    /// (A node with no effective input at all carries no derived element: the writer does not
    /// materialise the none x none cell.)
    /// </summary>
    private static void AssertDerivedInvariant(MetsFileWrapper parsed, AccessConditionProfile profile)
    {
        static bool HasInput(ResourceBase r) =>
            r.EffectiveAccessRestrictions.Count > 0 || r.EffectiveOtherAccessConditions.ContainsKey(Statement);
        static Dictionary<string, string> Expected(AccessConditionProfile p, ResourceBase r) =>
            p.Statements!.ResolveDerived(r.EffectiveAccessRestrictions, r.EffectiveOtherAccessConditions.GetValueOrDefault(Statement));
        static Dictionary<string, string> Managed(ResourceBase r) =>
            r.EffectiveOtherAccessConditions.Where(kv => kv.Key == "tier").ToDictionary();

        foreach (var file in parsed.Files.Where(HasInput))
            Managed(file).Should().Equal(Expected(profile, file), "file {0} must read back the tier for its effective inputs", file.LocalPath);
        foreach (var dir in AllDirectories(parsed.PhysicalStructure!).Where(HasInput))
            Managed(dir).Should().Equal(Expected(profile, dir), "directory {0} must read back the tier for its effective inputs", dir.LocalPath);
        foreach (var range in parsed.LogicalStructures.SelectMany(AllRanges).Where(HasInput))
            Managed(range).Should().Equal(Expected(profile, range), "range {0} must read back the tier for its effective inputs", range.Id);
    }

    private static IEnumerable<WorkingDirectory> AllDirectories(WorkingDirectory dir) =>
        new[] { dir }.Concat(dir.Directories.SelectMany(AllDirectories));

    private static IEnumerable<LogicalRange> AllRanges(LogicalRange range) =>
        new[] { range }.Concat(range.Ranges.SelectMany(AllRanges));

    [Fact]
    public async Task Tier_is_written_from_the_effective_PA_and_statement_on_every_div_with_an_explicit_input()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-effective-inputs.xml");

        // PA on objects/, a statement only on the child file: the file's tier must come from the
        // INHERITED PA1 and its own public-domain (0), the folder's from PA1 x none (2).
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA1"]);
        SetStatement(fullMets, "objects/a.tif", TestProfiles.OutOfCopyright);
        await metsManager.WriteMets(fullMets);

        TypedConditions(metsUri, "DMD_objects", "tier").Should().Equal("2");
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().Equal("0");
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), Statement).Should().Equal(TestProfiles.OutOfCopyright);
        // c.tif sets nothing itself: no dmdSec of its own, it inherits on read.
        TypedConditions(metsUri, MetsIds.Dmd("objects/c.tif"), "tier").Should().BeEmpty();

        var parsed = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        var objects = parsed.PhysicalStructure!.Directories.Single(d => d.Name == "objects");
        objects.OtherAccessConditions.Should().Equal(new Dictionary<string, string> { ["tier"] = "2" });
        var c = parsed.Files.Single(f => f.LocalPath == "objects/c.tif");
        c.OtherAccessConditions.Should().BeNull();
        c.EffectiveOtherAccessConditions.Should().Equal(new Dictionary<string, string> { ["tier"] = "2" });
        AssertDerivedInvariant(parsed, provider.Profile!);
    }

    [Fact]
    public async Task The_statement_is_inherited_like_the_restriction()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-statement-inherits.xml");
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA1"]);
        SetStatement(fullMets, "objects", TestProfiles.HolderConditions);       // folder: PA1 x holder-conditions = 3
        SetStatement(fullMets, "objects/a.tif", TestProfiles.LowRisk);  // file overrides: PA1 x low-risk = 1
        await metsManager.WriteMets(fullMets);

        TypedConditions(metsUri, "DMD_objects", Statement).Should().Equal(TestProfiles.HolderConditions);
        TypedConditions(metsUri, "DMD_objects", "tier").Should().Equal("3");
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().Equal("1");

        var parsed = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        parsed.Files.Single(f => f.LocalPath == "objects/c.tif").EffectiveOtherAccessConditions
            .Should().Equal(new Dictionary<string, string> { [Statement] = TestProfiles.HolderConditions, ["tier"] = "3" });
        AssertDerivedInvariant(parsed, provider.Profile!);
    }

    [Fact]
    public async Task The_rights_statement_is_not_an_input()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-rights-not-input.xml");
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA1"]);
        SetStatement(fullMets, "objects", TestProfiles.HolderConditions);
        metsManager.SetRightsStatementByPath(fullMets, "objects/a.tif", NoCUs);   // a public-domain statement...
        await metsManager.WriteMets(fullMets);

        // ...changes nothing: the file has a chosen input so it gets its own tier, for PA1 x holder-conditions.
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().Equal("3");
        AssertDerivedInvariant((await parser.GetMetsFileWrapper(metsUri)).Value!, provider.Profile!);
    }

    [Fact]
    public async Task Changing_an_ancestor_rewrites_a_descendants_stale_tier()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-stale-rewrite.xml");

        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA1"]);
        SetStatement(fullMets, "objects/a.tif", TestProfiles.OutOfCopyright);  // PA1 x public-domain = 0
        await metsManager.WriteMets(fullMets);
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().Equal("0");

        // Only the folder's PA changes; the file's own MODS is not touched by the caller.
        fullMets = await Reload(metsUri);
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA4"]);
        await metsManager.WriteMets(fullMets);

        TypedConditions(metsUri, "DMD_objects", "tier").Should().Equal("4");
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().Equal("4"); // the file's tier follows the inherited PA
    }

    [Fact]
    public async Task Clearing_all_inputs_removes_the_tier_so_the_div_inherits_again()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-clear.xml");

        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA1"]);
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects/a.tif", ["PA4"]);
        await metsManager.WriteMets(fullMets);
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().Equal("4");

        metsManager.SetAccessRestrictionsByPath(fullMets, "objects/a.tif", []);
        await metsManager.WriteMets(fullMets);

        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().BeEmpty();
        var a = (await parser.GetMetsFileWrapper(metsUri)).Value!.Files.Single(f => f.LocalPath == "objects/a.tif");
        a.EffectiveOtherAccessConditions["tier"].Should().Be("2", "PA1 inherited, no statement -> the __none__ column -> 2");
    }

    [Fact]
    public async Task Logical_ranges_derive_from_their_own_inputs_over_the_physical_root()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-logical.xml");

        metsManager.SetAccessRestrictionsByPath(fullMets, "", ["PA1"]);  // physical root
        PutAInARange(fullMets, ["PA4"], null);
        await metsManager.WriteMets(fullMets);

        var parsed = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        var root = parsed.LogicalStructures.Single();
        root.OtherAccessConditions.Should().BeNull("the root range sets nothing itself");
        root.EffectiveOtherAccessConditions["tier"].Should().Be("2", "inherited from the physical root: PA1 x none");
        root.Ranges.Single().OtherAccessConditions!["tier"].Should().Be("4");
    }

    [Fact]
    public async Task A_range_carries_its_chosen_statement_through_SetStructMap()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-range-statement.xml");
        metsManager.SetAccessRestrictionsByPath(fullMets, "", ["PA1"]);
        PutAInARange(fullMets, null, TestProfiles.HolderConditions);
        await metsManager.WriteMets(fullMets);

        TypedConditions(metsUri, MetsIds.DmdFromDivId("LOG_ITEM"), Statement).Should().Equal(TestProfiles.HolderConditions);
        TypedConditions(metsUri, MetsIds.DmdFromDivId("LOG_ITEM"), "tier").Should().Equal("3");
        var parsed = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        parsed.LogicalStructures.Single().Ranges.Single().OtherAccessConditions
            .Should().Equal(new Dictionary<string, string> { [Statement] = TestProfiles.HolderConditions, ["tier"] = "3" });
        AssertDerivedInvariant(parsed, provider.Profile!);
    }

    [Fact]
    public async Task Other_typed_conditions_and_unmanaged_derived_types_are_left_alone()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-unmanaged.xml");
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA1"]);
        await metsManager.WriteMets(fullMets);

        // Plant a third-party typed condition and a value under a type nothing derives any more.
        var doc = XDocument.Load(metsUri.LocalPath);
        var mods = doc.Descendants(MetsNs + "dmdSec").Single(d => (string?)d.Attribute("ID") == "DMD_objects")
            .Descendants(ModsNs + "mods").Single();
        mods.Add(new XElement(ModsNs + "accessCondition", new XAttribute("type", "dz"), "CC-BY-NC"));
        mods.Add(new XElement(ModsNs + "accessCondition", new XAttribute("type", "old-tier"), "9"));
        doc.Save(metsUri.LocalPath);

        fullMets = await Reload(metsUri);
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA2"]);
        await metsManager.WriteMets(fullMets);

        TypedConditions(metsUri, "DMD_objects", "tier").Should().Equal("3");
        TypedConditions(metsUri, "DMD_objects", "dz").Should().Equal("CC-BY-NC");
        TypedConditions(metsUri, "DMD_objects", "old-tier").Should().Equal("9");

        var objects = (await parser.GetMetsFileWrapper(metsUri)).Value!.PhysicalStructure!.Directories.Single(d => d.Name == "objects");
        objects.OtherAccessConditions.Should().Equal(new Dictionary<string, string> { ["dz"] = "CC-BY-NC", ["old-tier"] = "9", ["tier"] = "3" });
    }

    [Fact]
    public async Task Without_a_profile_nothing_is_derived_and_existing_derived_values_survive()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-no-profile.xml");
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA1"]);
        await metsManager.WriteMets(fullMets);
        TypedConditions(metsUri, "DMD_objects", "tier").Should().Equal("2");

        // The profile is switched out (e.g. Derived removed from config): the tier already in the
        // document is neither recomputed nor removed.
        provider.Profile = null;
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA4"]);
        var changed = await metsManager.ApplyDerivedAccessConditions(fullMets);
        changed.Should().BeEmpty();
        await metsManager.WriteMets(fullMets);
        TypedConditions(metsUri, "DMD_objects", "tier").Should().Equal("2");
    }

    [Fact]
    public async Task Setting_a_statement_without_a_statement_axis_is_refused_and_null_is_a_no_op()
    {
        var (_, fullMets) = await CreateMets("Outputs/derived-no-axis.xml");
        metsManager.SetAccessStatementByPath(fullMets, "objects", null, TestProfiles.HolderConditions).ErrorCode.Should().Be(ErrorCodes.BadRequest);
        metsManager.SetAccessStatementByPath(fullMets, "objects", null, null).Success.Should().BeTrue();
    }

    [Fact]
    public async Task ApplyDerivedAccessConditions_reports_only_the_divs_it_changed_and_is_idempotent()
    {
        var (_, fullMets) = await CreateMets("Outputs/derived-idempotent.xml");
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA1"]);
        SetStatement(fullMets, "objects/a.tif", TestProfiles.LowRisk);

        var first = await metsManager.ApplyDerivedAccessConditions(fullMets);
        first.Should().BeEquivalentTo([MetsIds.Phys("objects"), MetsIds.Phys("objects/a.tif")]);

        var second = await metsManager.ApplyDerivedAccessConditions(fullMets);
        second.Should().BeEmpty();
    }

    // ---- The statement's rights statement, written as the standard use and reproduction ------------

    private static readonly Uri Pdm = new("https://creativecommons.org/publicdomain/mark/1.0/");
    private static readonly Uri InCRuu = new("http://rightsstatements.org/vocab/InC-RUU/1.0/");

    [Fact]
    public async Task A_div_with_a_statement_carries_the_statements_rights_statement()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/statement-rights.xml");
        SetStatement(fullMets, "objects", TestProfiles.LowRisk);
        SetStatement(fullMets, "objects/a.tif", TestProfiles.OutOfCopyright);
        await metsManager.WriteMets(fullMets);

        TypedConditions(metsUri, "DMD_objects", "use and reproduction").Should().Equal(InC.ToString());
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "use and reproduction").Should().Equal(Pdm.ToString());

        var parsed = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        parsed.Files.Single(f => f.LocalPath == "objects/a.tif").EffectiveRightsStatement.Should().Be(Pdm);
        parsed.Files.Single(f => f.LocalPath == "objects/c.tif").EffectiveRightsStatement
            .Should().Be(InC, "c.tif inherits the folder's statement, and with it the folder's rights");
    }

    [Fact]
    public async Task The_rights_follow_a_changed_statement_and_go_with_a_cleared_one()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/statement-rights-follow.xml");
        SetStatement(fullMets, "objects/a.tif", TestProfiles.OutOfCopyright);
        await metsManager.WriteMets(fullMets);
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "use and reproduction").Should().Equal(Pdm.ToString());

        fullMets = await Reload(metsUri);
        SetStatement(fullMets, "objects/a.tif", "inc-low-risk-orphan");
        await metsManager.WriteMets(fullMets);
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "use and reproduction").Should().Equal(InCRuu.ToString());

        fullMets = await Reload(metsUri);
        SetStatement(fullMets, "objects/a.tif", null);
        await metsManager.WriteMets(fullMets);
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "use and reproduction")
            .Should().BeEmpty("the rights were written from the statement, so the file inherits both again");
    }

    [Fact]
    public async Task A_statement_the_profile_does_not_know_leaves_the_rights_as_found()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/statement-rights-unknown.xml");
        metsManager.SetRightsStatementByPath(fullMets, "objects/a.tif", NoCUs);
        SetStatement(fullMets, "objects/a.tif", "from-another-installation");
        await metsManager.WriteMets(fullMets);

        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "use and reproduction").Should().Equal(NoCUs.ToString());
    }

    [Fact]
    public async Task A_range_with_a_statement_carries_its_rights_statement()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/statement-rights-range.xml");
        PutAInARange(fullMets, ["PA1"], TestProfiles.HolderConditions);
        await metsManager.WriteMets(fullMets);

        TypedConditions(metsUri, MetsIds.DmdFromDivId("LOG_ITEM"), "use and reproduction").Should().Equal(InC.ToString());
    }

    // ---- Review of #434: the written document must agree with what the parser reads back ------------

    [Fact]
    public async Task A_file_whose_PA_comes_from_its_range_and_statement_from_its_folder_gets_its_own_tier()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-mixed-range-pa.xml");

        // The folder says only public-domain; the range says only PA1. The parser's effective inputs
        // for a.tif are PA1 x public-domain = 0, which it could never inherit from the folder (none x
        // public-domain = 5).
        SetStatement(fullMets, "objects", TestProfiles.OutOfCopyright);
        PutAInARange(fullMets, ["PA1"], null);
        await metsManager.WriteMets(fullMets);

        TypedConditions(metsUri, "DMD_objects", "tier").Should().Equal("5");
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().Equal("0");
        TypedConditions(metsUri, MetsIds.Dmd("objects/c.tif"), "tier").Should().BeEmpty("c.tif is in no range and inherits the folder's");

        var parsed = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        parsed.Files.Single(f => f.LocalPath == "objects/a.tif").EffectiveOtherAccessConditions["tier"].Should().Be("0");
        parsed.Files.Single(f => f.LocalPath == "objects/c.tif").EffectiveOtherAccessConditions["tier"].Should().Be("5");
        AssertDerivedInvariant(parsed, provider.Profile!);
    }

    [Fact]
    public async Task A_file_whose_statement_comes_from_its_range_and_PA_from_its_folder_gets_its_own_tier()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-mixed-range-statement.xml");

        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA1"]);  // folder: PA1 x none = 2
        PutAInARange(fullMets, null, TestProfiles.LowRisk);                               // a.tif: PA1 x low-risk = 1
        await metsManager.WriteMets(fullMets);

        TypedConditions(metsUri, "DMD_objects", "tier").Should().Equal("2");
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().Equal("1");
        AssertDerivedInvariant((await parser.GetMetsFileWrapper(metsUri)).Value!, provider.Profile!);
    }

    [Fact]
    public async Task A_files_own_statement_beats_its_range()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-own-statement-beats-range.xml");

        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA1"]);
        SetStatement(fullMets, "objects/a.tif", TestProfiles.HolderConditions);   // 3
        PutAInARange(fullMets, null, TestProfiles.OutOfCopyright);         // the range's 0 must NOT reach it
        await metsManager.WriteMets(fullMets);

        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().Equal("3");
        AssertDerivedInvariant((await parser.GetMetsFileWrapper(metsUri)).Value!, provider.Profile!);
    }

    [Fact]
    public async Task Taking_a_file_out_of_its_range_removes_the_tier_the_range_gave_it()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-range-removed.xml");
        SetStatement(fullMets, "objects", TestProfiles.OutOfCopyright);
        PutAInARange(fullMets, ["PA1"], null);
        await metsManager.WriteMets(fullMets);
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().Equal("0");

        fullMets = await Reload(metsUri);
        metsManager.SetStructMap(fullMets, new LogicalRange { Id = "LOG_ROOT", Type = "Collection", Name = "Root" }, Statement);
        await metsManager.WriteMets(fullMets);

        // The file's dmdSec was minted by the platform, so the tier that no longer has a source goes.
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().BeEmpty();
        var parsed = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        parsed.Files.Single(f => f.LocalPath == "objects/a.tif").EffectiveOtherAccessConditions["tier"].Should().Be("5");
        AssertDerivedInvariant(parsed, provider.Profile!);
    }

    [Fact]
    public async Task A_third_partys_tier_on_a_div_without_chosen_inputs_is_left_as_found()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-third-party-tier.xml");
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA1"]);
        PutAInARange(fullMets, null, null);
        await metsManager.WriteMets(fullMets);

        // Make LOG_ITEM look like a Goobi export that carries only its resulting tier: a dmdSec the
        // platform did not mint, holding a tier and no chosen elements (Karen Abel on the wiki, 12 Oct).
        var doc = XDocument.Load(metsUri.LocalPath);
        var dmd = doc.Descendants(MetsNs + "dmdSec").Single(d => (string?)d.Attribute("ID") == MetsIds.DmdFromDivId("LOG_ITEM"));
        dmd.SetAttributeValue("ID", "DMDLOG_0001");
        doc.Descendants(MetsNs + "div").Single(d => (string?)d.Attribute("ID") == "LOG_ITEM").SetAttributeValue("DMDID", "DMDLOG_0001");
        dmd.Descendants(ModsNs + "mods").Single().Add(new XElement(ModsNs + "accessCondition", new XAttribute("type", "tier"), "4"));
        doc.Save(metsUri.LocalPath);

        fullMets = await Reload(metsUri);
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA2"]);
        var changed = await metsManager.ApplyDerivedAccessConditions(fullMets);
        await metsManager.WriteMets(fullMets);

        changed.Should().NotContain("LOG_ITEM");
        TypedConditions(metsUri, "DMDLOG_0001", "tier").Should().Equal("4");
        // ...while a platform-minted dmdSec without inputs loses a planted tier.
        TypedConditions(metsUri, MetsIds.DmdFromDivId("LOG_ROOT"), "tier").Should().BeEmpty();
    }

    [Fact]
    public async Task A_legacy_restriction_value_gets_the_wildcard_rows_tier()
    {
        provider.Profile = TestProfiles.Leeds(wildcardRow: true);
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-legacy-value.xml");
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["dlip-open"]);
        await metsManager.WriteMets(fullMets);

        TypedConditions(metsUri, "DMD_objects", "tier").Should().Equal("5");
        TypedConditions(metsUri, "DMD_objects", "restriction on access").Should().Equal("dlip-open"); // the value itself is kept
    }

    [Fact]
    public async Task A_statement_outside_the_vocabulary_gets_the_PA1_rows_wildcard_cell()
    {
        // SetAccessStatementByPath accepts any string, as SetAccessRestrictionsByPath always has; a
        // mistyped or renamed status under PA1 must still derive something (re-review of #434, D).
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-unknown-statement.xml");
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA1"]);
        SetStatement(fullMets, "objects", "renamed-statement");
        await metsManager.WriteMets(fullMets);

        TypedConditions(metsUri, "DMD_objects", "tier").Should().Equal("3");
        TypedConditions(metsUri, "DMD_objects", Statement).Should().Equal("renamed-statement"); // the value itself is kept
        AssertDerivedInvariant((await parser.GetMetsFileWrapper(metsUri)).Value!, provider.Profile!);
    }

    [Fact]
    public async Task An_unavailable_profile_fails_the_write_instead_of_saving_a_stale_tier()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-profile-unavailable.xml");
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA1"]);
        await metsManager.WriteMets(fullMets);
        TypedConditions(metsUri, "DMD_objects", "tier").Should().Equal("2");

        provider.Unavailable = true;
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA4"]);
        var result = await metsManager.WriteMets(fullMets);

        result.Failure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCodes.UpstreamUnavailable);
        TypedConditions(metsUri, "DMD_objects", "restriction on access").Should().Equal("PA1"); // nothing was written
        TypedConditions(metsUri, "DMD_objects", "tier").Should().Equal("2");
    }

    // ---- The UI's two save paths (SetModsInformation, SetLogicalStructMap) --------------------

    private Task<DigitalPreservation.Common.Model.Results.Result> SaveDialogue(Uri metsUri, string path, ModsChanges changes) =>
        new SetModsInformationHandler(metsManager).Handle(new SetModsInformation(metsUri, path, null!, changes), CancellationToken.None);

    [Fact]
    public async Task A_dialogue_rendered_without_the_statement_field_leaves_the_statement_and_its_rights()
    {
        // The page could not fetch the profile when it rendered, so its form had no statement
        // dropdown; by the time it is saved the profile is back. The save must not read the
        // missing field as "clear the statement".
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-statement-not-in-form.xml");
        SetStatement(fullMets, "objects", TestProfiles.HolderConditions);
        await metsManager.WriteMets(fullMets);
        var rights = TypedConditions(metsUri, "DMD_objects", "use and reproduction");
        rights.Should().ContainSingle();

        var changes = new ModsChanges(["PA1"], RightsChoice.LeaveUnchanged, null, [], null, StatementInForm: false);
        (await SaveDialogue(metsUri, "objects", changes)).Success.Should().BeTrue();

        TypedConditions(metsUri, "DMD_objects", Statement).Should().Equal(TestProfiles.HolderConditions);
        TypedConditions(metsUri, "DMD_objects", "use and reproduction").Should().Equal(rights);
        TypedConditions(metsUri, "DMD_objects", "tier").Should().Equal("3");
    }

    [Fact]
    public async Task A_change_that_names_only_the_statement_leaves_the_restrictions_as_they_are()
    {
        // The API's access route (POST deposits/{id}/mets/access) sends only what the caller
        // sent: restrictions and record identifiers left out are null, meaning leave them alone.
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-api-partial-change.xml");
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA1"]);
        await metsManager.WriteMets(fullMets);

        var changes = new ModsChanges(null, RightsChoice.LeaveUnchanged, TestProfiles.LowRisk, null, null);
        (await SaveDialogue(metsUri, "objects", changes)).Success.Should().BeTrue();

        TypedConditions(metsUri, "DMD_objects", "restriction on access").Should().Equal("PA1");
        TypedConditions(metsUri, "DMD_objects", Statement).Should().Equal(TestProfiles.LowRisk);
    }

    [Fact]
    public async Task An_unavailable_profile_refuses_both_save_paths_with_its_reason()
    {
        var (metsUri, _) = await CreateMets("Outputs/derived-profile-unavailable-handlers.xml");
        provider.Unavailable = true;

        var dialogue = await SaveDialogue(metsUri, "objects",
            new ModsChanges(["PA1"], RightsChoice.LeaveUnchanged, TestProfiles.LowRisk, [], null));
        dialogue.ErrorCode.Should().Be(ErrorCodes.UpstreamUnavailable);
        dialogue.ErrorMessage.Should().Contain("API down");

        var structMap = await new SetLogicalStructMapHandler(metsManager).Handle(
            new SetLogicalStructMap(metsUri, null!, new LogicalRange { Id = "LOG_ROOT", Type = "Collection", Name = "Root" }),
            CancellationToken.None);
        structMap.ErrorCode.Should().Be(ErrorCodes.UpstreamUnavailable);
        structMap.ErrorMessage.Should().Contain("API down");

        TypedConditions(metsUri, "DMD_objects", "restriction on access").Should().BeEmpty(); // nothing was written
    }

    // ---- Writer and parser agree on the METS the platform writes ------------------------------
    // The writer only ever edits METS the platform created (GetFullMets refuses any other), so
    // these are the shapes such a METS can take; smLink ranges and Goobi's status element belong
    // to third-party METS, which is read but never written.

    private async Task WriteRangeOverA(FullMets fullMets, List<FilePointer> files)
    {
        metsManager.SetStructMap(fullMets, new LogicalRange
        {
            Id = "LOG_ROOT", Type = "Collection", Name = "Root",
            Ranges =
            [
                new LogicalRange
                {
                    Id = "LOG_ITEM", Type = "Item", Name = "Item",
                    AccessRestrictions = ["PA1"],
                    OtherAccessConditions = new Dictionary<string, string> { [Statement] = TestProfiles.LowRisk },
                    Files = files
                }
            ]
        }, Statement).Success.Should().BeTrue();
        (await metsManager.WriteMets(fullMets)).Success.Should().BeTrue();
    }

    [Fact]
    public async Task A_file_listed_twice_in_one_range_still_takes_its_inputs_from_it()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-range-lists-file-twice.xml");
        await WriteRangeOverA(fullMets,
            [new FilePointer { LocalPath = "objects/a.tif" }, new FilePointer { LocalPath = "objects/a.tif" }]);

        var parsed = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        var a = parsed.Files.Single(f => f.LocalPath == "objects/a.tif");
        a.EffectiveAccessRestrictions.Should().Equal("PA1");
        a.EffectiveOtherAccessConditions.Should().Contain("tier", "1");
        AssertDerivedInvariant(parsed, provider.Profile!);
    }

    [Fact]
    public async Task An_area_that_only_names_its_file_paints_the_whole_file_for_writer_and_parser()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-range-area-without-extent.xml");
        await WriteRangeOverA(fullMets, [new FilePointer { LocalPath = "objects/a.tif" }]);

        // Rewrite the range's whole-file pointer as <mets:fptr><mets:area FILEID="..."/></mets:fptr>,
        // which says the same thing, then let the writer run over it again.
        var doc = XDocument.Load(metsUri.LocalPath);
        var fptr = doc.Descendants(MetsNs + "div").Single(d => (string?)d.Attribute("ID") == "LOG_ITEM").Element(MetsNs + "fptr")!;
        var fileId = (string)fptr.Attribute("FILEID")!;
        fptr.Attribute("FILEID")!.Remove();
        fptr.Add(new XElement(MetsNs + "area", new XAttribute("FILEID", fileId)));
        StripTier(doc, MetsIds.Dmd("objects/a.tif"));
        doc.Save(metsUri.LocalPath);

        var reread = (await metsManager.GetFullMets(metsUri, null)).Value!;
        (await metsManager.WriteMets(reread)).Success.Should().BeTrue();

        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().Equal("1");
        AssertDerivedInvariant((await parser.GetMetsFileWrapper(metsUri)).Value!, provider.Profile!);
    }

    [Fact]
    public async Task An_empty_statement_element_is_no_statement_to_the_parser_as_to_the_writer()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-empty-statement.xml");
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA1"]);
        SetStatement(fullMets, "objects", TestProfiles.OutOfCopyright);
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects/a.tif", ["PA1"]);
        await metsManager.WriteMets(fullMets);

        var doc = XDocument.Load(metsUri.LocalPath);
        var mods = doc.Descendants(MetsNs + "dmdSec").Single(d => (string?)d.Attribute("ID") == MetsIds.Dmd("objects/a.tif"))
            .Descendants(ModsNs + "mods").Single();
        mods.Add(new XElement(ModsNs + "accessCondition", new XAttribute("type", Statement)));
        doc.Save(metsUri.LocalPath);

        var a = (await parser.GetMetsFileWrapper(metsUri)).Value!.Files.Single(f => f.LocalPath == "objects/a.tif");
        a.OtherAccessConditions.Should().NotContainKey(Statement);
        a.EffectiveOtherAccessConditions.Should().Contain(Statement, TestProfiles.OutOfCopyright, "it inherits, as the writer reads it");
    }

    private static void StripTier(XDocument doc, string dmdId)
    {
        doc.Descendants(MetsNs + "dmdSec").Single(d => (string?)d.Attribute("ID") == dmdId)
            .Descendants(ModsNs + "accessCondition").Where(ac => (string?)ac.Attribute("type") == "tier").Remove();
    }

    [Fact]
    public async Task An_area_with_an_untyped_extent_is_part_of_a_file_for_writer_and_parser()
    {
        // BETYPE="BYTE" is kept as an extra attribute, so the writer meets it in AnyAttribute on the
        // same write: it must not read the range as painting the whole file.
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-range-untyped-extent.xml");
        await WriteRangeOverA(fullMets,
        [
            new FilePointer
            {
                LocalPath = "objects/a.tif",
                ExtraAreaAttributes = new Dictionary<string, string> { ["BETYPE"] = "BYTE", ["BEGIN"] = "0", ["END"] = "1024" }
            }
        ]);

        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().BeEmpty();
        var parsed = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        parsed.Files.Single(f => f.LocalPath == "objects/a.tif").EffectiveAccessRestrictions.Should().BeEmpty();
        AssertDerivedInvariant(parsed, provider.Profile!);
    }
}
