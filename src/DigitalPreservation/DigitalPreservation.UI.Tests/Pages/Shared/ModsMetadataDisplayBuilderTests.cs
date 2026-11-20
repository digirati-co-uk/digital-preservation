using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.AccessConditions;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.UI.Pages.Shared;
using FluentAssertions;

namespace DigitalPreservation.UI.Tests.Pages.Shared;

/// <summary>
/// GetDisplayItems used to throw NotSupportedException when an item's explicit value was present but
/// didn't equal its effective value. MetsParser.ComputeEffectiveMetadata always copies an explicit
/// value into the effective one, so the throw is unreachable from any METS it reads today - but it was
/// still the wrong failure mode for a display helper: any future change to inheritance, or in-memory
/// edit that updated one value and not the other, would turn a cosmetic inconsistency into a 500 for
/// the whole page (issue #268 item 2). This pins that every shape - matching, explicit with no
/// effective yet, and the three ways they could diverge - renders without throwing and shows the
/// explicit value, not inherited.
/// </summary>
public class ModsMetadataDisplayBuilderTests
{
    private static readonly Uri RightsA = new("http://rightsstatements.org/vocab/InC/1.0/");
    private static readonly Uri RightsB = new("http://rightsstatements.org/vocab/NoC-US/1.0/");

    private static WorkingFile Resource(
        List<string>? access = null, List<string>? effectiveAccess = null,
        Uri? rights = null, Uri? effectiveRights = null,
        RecordInfo? recordInfo = null, RecordInfo? effectiveRecordInfo = null) => new()
    {
        LocalPath = "objects/page-001.jpg",
        AccessRestrictions = access,
        EffectiveAccessRestrictions = effectiveAccess ?? [],
        RightsStatement = rights,
        EffectiveRightsStatement = effectiveRights,
        RecordInfo = recordInfo,
        EffectiveRecordInfo = effectiveRecordInfo
    };

    private static RecordInfo SingleIdentifier(string value) =>
        new() { RecordIdentifiers = [new RecordIdentifier { Source = "EMu", Value = value }] };

    [Fact]
    public void ExplicitAndEffectiveEqual_OneNonInheritedItemPerField()
    {
        var recordInfo = SingleIdentifier("AA1");
        var resource = Resource(
            access: ["staff-only"], effectiveAccess: ["staff-only"],
            rights: RightsA, effectiveRights: RightsA,
            recordInfo: recordInfo, effectiveRecordInfo: recordInfo);

        var items = ModsMetadataDisplayBuilder.GetDisplayItems(resource);

        items.Should().HaveCount(3);
        items.Should().OnlyContain(i => !i.Inherited);
        items.Should().ContainSingle(i => i.Label == "Access" && i.Text == "staff-only");
        items.Should().ContainSingle(i => i.Label == "Rights" && i.Text == "InC");
        items.Should().ContainSingle(i => i.Label == "Record" && i.Text.Contains("AA1"));
    }

    [Fact]
    public void ExplicitOnly_NoEffectiveValueYet_ReturnsTheExplicitValue_NotInherited()
    {
        var resource = Resource(
            access: ["staff-only"], effectiveAccess: ["staff-only"],
            rights: RightsA, effectiveRights: null,
            recordInfo: SingleIdentifier("AA1"), effectiveRecordInfo: null);

        var items = ModsMetadataDisplayBuilder.GetDisplayItems(resource);

        items.Should().HaveCount(3);
        items.Should().OnlyContain(i => !i.Inherited);
        items.Should().ContainSingle(i => i.Label == "Rights" && i.Text == "InC");
        items.Should().ContainSingle(i => i.Label == "Record" && i.Text.Contains("AA1"));
    }

    [Fact]
    public void MismatchedAccessRestrictions_DoesNotThrow_AndReturnsTheExplicitValue()
    {
        var resource = Resource(access: ["staff-only"], effectiveAccess: ["public"]);

        var act = () => ModsMetadataDisplayBuilder.GetDisplayItems(resource);

        act.Should().NotThrow();
        var items = act();
        var item = items.Should().ContainSingle().Subject;
        item.Label.Should().Be("Access");
        item.Text.Should().Be("staff-only");
        item.Inherited.Should().BeFalse();
    }

    [Fact]
    public void MismatchedRightsStatement_DoesNotThrow_AndReturnsTheExplicitValue()
    {
        var resource = Resource(rights: RightsA, effectiveRights: RightsB);

        var act = () => ModsMetadataDisplayBuilder.GetDisplayItems(resource);

        act.Should().NotThrow();
        var items = act();
        var item = items.Should().ContainSingle().Subject;
        item.Label.Should().Be("Rights");
        item.Text.Should().Be("InC");
        item.Inherited.Should().BeFalse();
    }

    [Fact]
    public void MismatchedRecordInfo_DoesNotThrow_AndReturnsTheExplicitValue()
    {
        var resource = Resource(recordInfo: SingleIdentifier("AA1"), effectiveRecordInfo: SingleIdentifier("BB2"));

        var act = () => ModsMetadataDisplayBuilder.GetDisplayItems(resource);

        act.Should().NotThrow();
        var items = act();
        var item = items.Should().ContainSingle().Subject;
        item.Label.Should().Be("Record");
        item.Text.Should().Contain("AA1");
        item.Inherited.Should().BeFalse();
    }
}

/// <summary>
/// With a statement profile (issue #433) a row shows short labels, with the full ones in the
/// tooltip, and does not repeat the rights statement its statement implies.
/// </summary>
public class ModsMetadataDisplayBuilderStatementTests
{
    private static readonly Uri InC = new("http://rightsstatements.org/vocab/InC/1.0/");
    private static readonly Uri NoCUs = new("http://rightsstatements.org/vocab/NoC-US/1.0/");

    private static readonly AccessConditionProfile Profile = AccessConditionProfile.FromJson("""
        {
          "name": "Test",
          "restrictionAxis": { "label": "Level", "values": [ { "value": "PA1", "label": "Level 1", "shortLabel": "PA1" } ] },
          "statements": {
            "label": "Copyright Statement",
            "type": "copyright statement",
            "values": [ { "value": "inc", "label": "In Copyright: Assessed - LOW RISK", "shortLabel": "InC low risk", "category": "c", "iiif": "e" } ],
            "categories": [ { "value": "c", "label": "C" } ],
            "iiif": { "entries": [ { "id": "e", "rights": "http://rightsstatements.org/vocab/InC/1.0/" } ] },
            "derived": [ {
              "type": "tier", "label": "Access Tier",
              "values": [ { "value": "1", "label": "Tier 1 - Open", "shortLabel": "Tier 1" } ],
              "matrix": { "*": { "*": "1" } }
            } ]
          }
        }
        """);

    private static WorkingFile Resource(Uri? rights) => new()
    {
        LocalPath = "objects/page-001.jpg",
        AccessRestrictions = ["PA1"],
        EffectiveAccessRestrictions = ["PA1"],
        RightsStatement = rights,
        EffectiveRightsStatement = rights,
        OtherAccessConditions = new() { ["copyright statement"] = "inc", ["tier"] = "1" },
        EffectiveOtherAccessConditions = new() { ["copyright statement"] = "inc", ["tier"] = "1" }
    };

    [Fact]
    public void A_row_shows_short_labels_and_the_tooltip_the_full_ones()
    {
        var items = ModsMetadataDisplayBuilder.GetDisplayItems(Resource(InC), profile: Profile);

        items.Select(i => i.Text).Should().Equal("PA1", "InC low risk", "Tier 1");
        items[0].Detail.Should().Be("Level 1");
        items[1].Detail.Should().StartWith("In Copyright: Assessed - LOW RISK\nIIIF rights: " + InC);
        items[2].Detail.Should().Be("Tier 1 - Open");
    }

    [Fact]
    public void The_rights_statement_a_statement_implies_is_not_repeated_but_a_different_one_is_shown()
    {
        ModsMetadataDisplayBuilder.GetDisplayItems(Resource(InC), profile: Profile)
            .Should().NotContain(i => i.Label == "Rights");
        ModsMetadataDisplayBuilder.GetDisplayItems(Resource(NoCUs), profile: Profile)
            .Should().ContainSingle(i => i.Label == "Rights" && i.Text == "NoC-US", "a value left from before is worth seeing");
    }

    [Fact]
    public void Typed_conditions_the_profile_does_not_name_are_shown_raw()
    {
        // A standard profile (Browse always has one now), or a Leeds one meeting a third party's type:
        // what the parser found is still shown, under its raw type.
        var resource = Resource(InC);
        resource.OtherAccessConditions!["goobi type"] = "x";
        resource.EffectiveOtherAccessConditions["goobi type"] = "x";

        var standard = ModsMetadataDisplayBuilder.GetDisplayItems(resource,
            profile: AccessConditionProfile.Simple([new AccessRestriction { Value = "PA1", Label = "Level 1" }]));
        standard.Select(i => (i.Label, i.Text)).Should().Contain(new[]
        {
            ("copyright statement", "inc"), ("tier", "1"), ("goobi type", "x")
        });

        var leeds = ModsMetadataDisplayBuilder.GetDisplayItems(resource, profile: Profile);
        leeds.Select(i => i.Text).Should().Equal("PA1", "InC low risk", "Tier 1", "x");
    }
}
