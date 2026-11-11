using DigitalPreservation.Common.Model.AccessConditions;
using Microsoft.Extensions.Configuration;
using Preservation.API.Features.AccessConditions;

namespace Preservation.API.Tests.AccessConditions;

/// <summary>
/// The two configuration forms behind <c>GET /access-conditions</c> (issue #433): the standard
/// <c>AccessConditions</c> list, and the two-axis <c>AccessConditionProfile</c> section for an
/// installation that derives something. Never both.
/// </summary>
public class AccessConditionConfigurationTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    private static AccessConditionProfile Apply(IConfiguration configuration)
    {
        var profile = new AccessConditionProfile();
        ServiceCollectionX.ApplyConfiguration(profile, configuration);
        return profile;
    }

    [Fact]
    public void The_standard_list_fills_only_the_restriction_axis()
    {
        var profile = Apply(Config(
            ("AccessConditions:0:Value", "Open"), ("AccessConditions:0:Label", "Open"),
            ("AccessConditions:1:Value", "Closed"), ("AccessConditions:1:Label", "Closed")));

        profile.RestrictionAxis.Values.Select(v => v.Value).Should().Equal("Open", "Closed");
        profile.RestrictionAxis.Label.Should().Be("Access Restriction(s)");
        profile.AllowMultipleRestrictions.Should().BeFalse();
        profile.CategoryAxis.Should().BeNull();
        profile.Derived.Should().BeEmpty();
        profile.Validate().Should().BeEmpty();
    }

    [Fact]
    public void AllowMultipleAccessConditions_goes_with_the_standard_list()
    {
        var profile = Apply(Config(
            ("AccessConditions:0:Value", "Open"), ("AccessConditions:0:Label", "Open"),
            ("AllowMultipleAccessConditions", "true")));
        profile.AllowMultipleRestrictions.Should().BeTrue();
        profile.Validate().Should().BeEmpty();
    }

    [Fact]
    public void No_configuration_at_all_is_a_bare_but_valid_profile()
    {
        var profile = Apply(Config());
        profile.RestrictionAxis.Values.Should().BeEmpty();
        profile.Validate().Should().BeEmpty();
    }

    [Fact]
    public void The_two_axis_section_is_bound_as_a_whole()
    {
        var profile = Apply(Config(
            ("AccessConditionProfile:RestrictionAxis:Label", "Protected Access Level"),
            ("AccessConditionProfile:RestrictionAxis:Values:0:Value", "PA1"),
            ("AccessConditionProfile:RestrictionAxis:Values:0:Label", "Level 1"),
            ("AccessConditionProfile:CategoryAxis:Label", "Copyright status"),
            ("AccessConditionProfile:CategoryAxis:Type", "copyright status"),
            ("AccessConditionProfile:CategoryAxis:Values:0:Value", "public-domain"),
            ("AccessConditionProfile:CategoryAxis:Values:0:Label", "Out of copyright"),
            ("AccessConditionProfile:CategoryAxis:Values:0:RightsStatements:0", "CC0"),
            ("AccessConditionProfile:Derived:0:Type", "tier"),
            ("AccessConditionProfile:Derived:0:Values:0:Value", "0"),
            ("AccessConditionProfile:Derived:0:Values:0:Label", "Open"),
            ("AccessConditionProfile:Derived:0:Matrix:*:*", "0")));

        profile.RestrictionAxis.Label.Should().Be("Protected Access Level");
        profile.CategoryAxis!.Type.Should().Be("copyright status");
        profile.CategoryAxis.Values.Single().RightsStatements.Should().Equal("CC0");
        profile.Derived.Single().Lookup("PA1", "public-domain").Should().Be("0");
        profile.Validate().Should().BeEmpty();
    }

    [Fact]
    public void Both_forms_at_once_is_a_configuration_error()
    {
        var config = Config(
            ("AccessConditions:0:Value", "Open"), ("AccessConditions:0:Label", "Open"),
            ("AccessConditionProfile:RestrictionAxis:Values:0:Value", "PA1"),
            ("AccessConditionProfile:RestrictionAxis:Values:0:Label", "Level 1"));
        var act = () => Apply(config);
        act.Should().Throw<InvalidOperationException>().WithMessage("*not both*");
    }

    [Fact]
    public void AllowMultipleAccessConditions_beside_the_two_axis_section_is_a_configuration_error()
    {
        var config = Config(
            ("AllowMultipleAccessConditions", "true"),
            ("AccessConditionProfile:RestrictionAxis:Values:0:Value", "PA1"),
            ("AccessConditionProfile:RestrictionAxis:Values:0:Label", "Level 1"));
        var act = () => Apply(config);
        act.Should().Throw<InvalidOperationException>().WithMessage("*AllowMultipleAccessConditions*");
    }
}
