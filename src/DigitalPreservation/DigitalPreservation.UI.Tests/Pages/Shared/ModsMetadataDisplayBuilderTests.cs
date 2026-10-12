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
