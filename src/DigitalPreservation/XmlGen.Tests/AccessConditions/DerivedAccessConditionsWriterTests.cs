using System.Xml.Linq;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.AccessConditions;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Mets;
using DigitalPreservation.Mets.AccessConditions;
using DigitalPreservation.Mets.StorageImpl;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XmlGen.Tests.AccessConditions;

/// <summary>
/// The METS writer's half of issue #433: on every write, <see cref="MetsManager.ApplyDerivedAccessConditions"/>
/// rewrites the resolver's managed types across the whole document from each div's EFFECTIVE
/// chosen conditions, and leaves everything else alone.
/// </summary>
public class DerivedAccessConditionsWriterTests
{
    private static readonly XNamespace MetsNs = "http://www.loc.gov/METS/";
    private static readonly XNamespace ModsNs = "http://www.loc.gov/mods/v3";

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
    private readonly StubProvider provider = new(TestProfiles.Leeds(wildcardGroup: true));
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

    private static List<string> TypedConditions(Uri metsUri, string dmdId, string type)
    {
        var doc = XDocument.Load(metsUri.LocalPath);
        var dmd = doc.Descendants(MetsNs + "dmdSec").SingleOrDefault(d => (string?)d.Attribute("ID") == dmdId);
        if (dmd == null) return [];
        return dmd.Descendants(ModsNs + "accessCondition")
            .Where(ac => (string?)ac.Attribute("type") == type)
            .Select(ac => ac.Value).ToList();
    }

    [Fact]
    public async Task Tier_is_written_from_the_effective_PA_and_rights_on_every_div_with_an_explicit_input()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-effective-inputs.xml");

        // PA on objects/, a rights statement only on the child file: the file's tier must come from
        // the INHERITED PA2 and its own InC (PA2 x anything = 3), the folder's from PA2 x none = 3.
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA2"]);
        metsManager.SetRightsStatementByPath(fullMets, "objects/a.tif", InC);
        await metsManager.WriteMets(fullMets);

        TypedConditions(metsUri, "DMD_objects", "tier").Should().Equal("3");
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().Equal("3");
        // c.tif sets nothing itself: no dmdSec of its own, it inherits on read.
        TypedConditions(metsUri, MetsIds.Dmd("objects/c.tif"), "tier").Should().BeEmpty();

        var parsed = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        var objects = parsed.PhysicalStructure!.Directories.Single(d => d.Name == "objects");
        objects.DerivedAccessConditions.Should().Equal(new Dictionary<string, string> { ["tier"] = "3" });
        var c = parsed.Files.Single(f => f.LocalPath == "objects/c.tif");
        c.DerivedAccessConditions.Should().BeNull();
        c.EffectiveDerivedAccessConditions.Should().Equal(new Dictionary<string, string> { ["tier"] = "3" });
    }

    [Fact]
    public async Task Changing_an_ancestor_rewrites_a_descendants_stale_tier()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-stale-rewrite.xml");

        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA1"]);
        metsManager.SetRightsStatementByPath(fullMets, "objects/a.tif", NoCUs);  // PA1 x public-domain = 0
        await metsManager.WriteMets(fullMets);
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().Equal("0");

        // Only the folder's PA changes; the file's own MODS is not touched by the caller.
        fullMets = (await metsManager.GetFullMets(metsUri, (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!)).Value!;
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA4"]);
        await metsManager.WriteMets(fullMets);

        TypedConditions(metsUri, "DMD_objects", "tier").Should().Equal("4");
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().Equal("4"); // the file's tier follows the inherited PA
    }

    [Fact]
    public async Task Clearing_both_inputs_removes_the_tier_so_the_div_inherits_again()
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
        a.EffectiveDerivedAccessConditions["tier"].Should().Be("2", "PA1 inherited, no rights -> unassessed column -> 2");
    }

    [Fact]
    public async Task Explicitly_no_rights_is_the_none_column_and_stops_inheriting()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-suppressed-rights.xml");

        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA1"]);
        metsManager.SetRightsStatementByPath(fullMets, "objects", NoCUs);            // folder: PA1 x public-domain = 0
        metsManager.SuppressRightsInheritanceByPath(fullMets, "objects/a.tif");      // file: PA1 x none = 2
        await metsManager.WriteMets(fullMets);

        TypedConditions(metsUri, "DMD_objects", "tier").Should().Equal("0");
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().Equal("2");
    }

    [Fact]
    public async Task Logical_ranges_derive_from_their_own_inputs_over_the_physical_root()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-logical.xml");

        metsManager.SetAccessRestrictionsByPath(fullMets, "", ["PA1"]);  // physical root
        metsManager.SetStructMap(fullMets, new LogicalRange
        {
            Id = "LOG_ROOT", Type = "Collection", Name = "Root",
            Ranges =
            [
                new LogicalRange
                {
                    Id = "LOG_ITEM", Type = "Item", Name = "Closed item",
                    AccessRestrictions = ["PA4"],
                    Files = [new FilePointer { LocalPath = "objects/a.tif" }]
                }
            ]
        });
        await metsManager.WriteMets(fullMets);

        var parsed = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        var root = parsed.LogicalStructures.Single();
        root.DerivedAccessConditions.Should().BeNull("the root range sets nothing itself");
        root.EffectiveDerivedAccessConditions["tier"].Should().Be("2", "inherited from the physical root: PA1 x none");
        root.Ranges.Single().DerivedAccessConditions!["tier"].Should().Be("4");
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

        var etag = (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!;
        fullMets = (await metsManager.GetFullMets(metsUri, etag)).Value!;
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA2"]);
        await metsManager.WriteMets(fullMets);

        TypedConditions(metsUri, "DMD_objects", "tier").Should().Equal("3");
        TypedConditions(metsUri, "DMD_objects", "dz").Should().Equal("CC-BY-NC");
        TypedConditions(metsUri, "DMD_objects", "old-tier").Should().Equal("9");

        var objects = (await parser.GetMetsFileWrapper(metsUri)).Value!.PhysicalStructure!.Directories.Single(d => d.Name == "objects");
        objects.DerivedAccessConditions.Should().Equal(new Dictionary<string, string> { ["dz"] = "CC-BY-NC", ["old-tier"] = "9", ["tier"] = "3" });
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
    public async Task ApplyDerivedAccessConditions_reports_only_the_divs_it_changed_and_is_idempotent()
    {
        var (_, fullMets) = await CreateMets("Outputs/derived-idempotent.xml");
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA1"]);
        metsManager.SetRightsStatementByPath(fullMets, "objects/a.tif", InC);

        var first = await metsManager.ApplyDerivedAccessConditions(fullMets);
        first.Should().BeEquivalentTo([MetsIds.Phys("objects"), MetsIds.Phys("objects/a.tif")]);

        var second = await metsManager.ApplyDerivedAccessConditions(fullMets);
        second.Should().BeEmpty();
    }

    // ---- Review of #434: the written document must agree with what the parser reads back ------------

    private static readonly Uri InCEdu = new("http://rightsstatements.org/vocab/InC-EDU/1.0/");

    /// <summary>
    /// The invariant finding 2 of the review asked for: after a write, every file, folder and range
    /// reads back with exactly the derived value its EFFECTIVE inputs give.
    /// </summary>
    private static void AssertDerivedInvariant(MetsFileWrapper parsed, AccessConditionProfile profile)
    {
        // A node with no effective input at all (nothing chosen on or above it) carries no derived
        // element - the writer does not materialise the none x none cell - so the invariant is
        // stated for every node that has at least one effective input.
        foreach (var file in parsed.Files.Where(f => f.EffectiveAccessRestrictions.Count > 0 || f.EffectiveRightsStatement != null))
        {
            file.EffectiveDerivedAccessConditions.Should().Equal(
                profile.ResolveDerived(file.EffectiveAccessRestrictions, file.EffectiveRightsStatement),
                "file {0} must read back the tier for its effective inputs", file.LocalPath);
        }
        foreach (var dir in AllDirectories(parsed.PhysicalStructure!).Where(d => d.EffectiveAccessRestrictions.Count > 0 || d.EffectiveRightsStatement != null))
        {
            dir.EffectiveDerivedAccessConditions.Should().Equal(
                profile.ResolveDerived(dir.EffectiveAccessRestrictions, dir.EffectiveRightsStatement),
                "directory {0} must read back the tier for its effective inputs", dir.LocalPath);
        }
        foreach (var range in parsed.LogicalStructures.SelectMany(AllRanges).Where(r => r.EffectiveAccessRestrictions.Count > 0 || r.EffectiveRightsStatement != null))
        {
            range.EffectiveDerivedAccessConditions.Should().Equal(
                profile.ResolveDerived(range.EffectiveAccessRestrictions, range.EffectiveRightsStatement),
                "range {0} must read back the tier for its effective inputs", range.Id);
        }
    }

    private static IEnumerable<WorkingDirectory> AllDirectories(WorkingDirectory dir) =>
        new[] { dir }.Concat(dir.Directories.SelectMany(AllDirectories));

    private static IEnumerable<LogicalRange> AllRanges(LogicalRange range) =>
        new[] { range }.Concat(range.Ranges.SelectMany(AllRanges));

    private void PutAInARange(FullMets fullMets, List<string>? access, Uri? rights) =>
        metsManager.SetStructMap(fullMets, new LogicalRange
        {
            Id = "LOG_ROOT", Type = "Collection", Name = "Root",
            Ranges =
            [
                new LogicalRange
                {
                    Id = "LOG_ITEM", Type = "Item", Name = "Item",
                    AccessRestrictions = access ?? [], RightsStatement = rights,
                    Files = [new FilePointer { LocalPath = "objects/a.tif" }]
                }
            ]
        });

    [Fact]
    public async Task A_file_whose_PA_comes_from_its_range_and_rights_from_its_folder_gets_its_own_tier()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-mixed-range-pa.xml");

        // The folder says only NoC-US; the range says only PA1. The parser's effective inputs for
        // a.tif are PA1 x NoC-US = 0, which it could never inherit from the folder (none x NoC-US = 5).
        metsManager.SetRightsStatementByPath(fullMets, "objects", NoCUs);
        PutAInARange(fullMets, ["PA1"], null);
        await metsManager.WriteMets(fullMets);

        TypedConditions(metsUri, "DMD_objects", "tier").Should().Equal("5");
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().Equal("0");
        TypedConditions(metsUri, MetsIds.Dmd("objects/c.tif"), "tier").Should().BeEmpty("c.tif is in no range and inherits the folder's");

        var parsed = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        parsed.Files.Single(f => f.LocalPath == "objects/a.tif").EffectiveDerivedAccessConditions["tier"].Should().Be("0");
        parsed.Files.Single(f => f.LocalPath == "objects/c.tif").EffectiveDerivedAccessConditions["tier"].Should().Be("5");
        AssertDerivedInvariant(parsed, provider.Profile!);
    }

    [Fact]
    public async Task A_file_whose_rights_come_from_its_range_and_PA_from_its_folder_gets_its_own_tier()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-mixed-range-rights.xml");

        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA1"]);   // folder: PA1 x none = 2
        PutAInARange(fullMets, null, InCEdu);                                    // a.tif: PA1 x low-risk = 1
        await metsManager.WriteMets(fullMets);

        TypedConditions(metsUri, "DMD_objects", "tier").Should().Equal("2");
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().Equal("1");
        AssertDerivedInvariant((await parser.GetMetsFileWrapper(metsUri)).Value!, provider.Profile!);
    }

    [Fact]
    public async Task A_files_own_rights_sentinel_beats_its_range()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-sentinel-beats-range.xml");

        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["PA1"]);
        metsManager.SuppressRightsInheritanceByPath(fullMets, "objects/a.tif");  // explicitly none
        PutAInARange(fullMets, null, NoCUs);                                     // the range's NoC-US must NOT reach it
        await metsManager.WriteMets(fullMets);

        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().Equal("2");
        AssertDerivedInvariant((await parser.GetMetsFileWrapper(metsUri)).Value!, provider.Profile!);
    }

    [Fact]
    public async Task Taking_a_file_out_of_its_range_removes_the_tier_the_range_gave_it()
    {
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-range-removed.xml");
        metsManager.SetRightsStatementByPath(fullMets, "objects", NoCUs);
        PutAInARange(fullMets, ["PA1"], null);
        await metsManager.WriteMets(fullMets);
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().Equal("0");

        fullMets = (await metsManager.GetFullMets(metsUri, (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!)).Value!;
        metsManager.SetStructMap(fullMets, new LogicalRange { Id = "LOG_ROOT", Type = "Collection", Name = "Root" });
        await metsManager.WriteMets(fullMets);

        // The file's dmdSec was minted by the platform, so the tier that no longer has a source goes.
        TypedConditions(metsUri, MetsIds.Dmd("objects/a.tif"), "tier").Should().BeEmpty();
        var parsed = (await parser.GetMetsFileWrapper(metsUri)).Value!;
        parsed.Files.Single(f => f.LocalPath == "objects/a.tif").EffectiveDerivedAccessConditions["tier"].Should().Be("5");
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

        fullMets = (await metsManager.GetFullMets(metsUri, (await parser.GetMetsFileWrapper(metsUri)).Value!.ETag!)).Value!;
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
        provider.Profile = TestProfiles.Leeds(wildcardGroup: true, wildcardRow: true);
        var (metsUri, fullMets) = await CreateMets("Outputs/derived-legacy-value.xml");
        metsManager.SetAccessRestrictionsByPath(fullMets, "objects", ["dlip-open"]);
        await metsManager.WriteMets(fullMets);

        TypedConditions(metsUri, "DMD_objects", "tier").Should().Equal("5");
        TypedConditions(metsUri, "DMD_objects", "restriction on access").Should().Equal("dlip-open"); // the value itself is kept
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
}
