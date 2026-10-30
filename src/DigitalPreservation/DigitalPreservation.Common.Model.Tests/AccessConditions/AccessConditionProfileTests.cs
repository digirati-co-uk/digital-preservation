using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.AccessConditions;
using FluentAssertions;

namespace DigitalPreservation.Common.Model.Tests.AccessConditions;

/// <summary>
/// The configured lookup behind derived access conditions (issue #433): the Leeds matrix of
/// Protected Access Level x rights-statement group -> Access Tier, and the profile validation
/// that runs at Preservation API startup.
/// </summary>
public class AccessConditionProfileTests
{
    private static readonly Uri NoCUs = new("http://rightsstatements.org/vocab/NoC-US/1.0/");
    private static readonly Uri InCEdu = new("http://rightsstatements.org/vocab/InC-EDU/1.0/");
    private static readonly Uri InC = new("http://rightsstatements.org/vocab/InC/1.0/");
    private static readonly Uri Cne = new("http://rightsstatements.org/vocab/CNE/1.0/");
    private static readonly Uri CcBy = new("https://creativecommons.org/licenses/by/4.0/");

    /// <summary>The Leeds profile as deployed to dev, minus the wildcard rights group.</summary>
    public static AccessConditionProfile Leeds(bool wildcardGroup = false) => new()
    {
        RestrictionAxis = new AccessAxis
        {
            Label = "Protected Access Level",
            Values =
            [
                new AccessRestriction { Value = "PA1", Label = "Level 1" },
                new AccessRestriction { Value = "PA2", Label = "Level 2" },
                new AccessRestriction { Value = "PA3", Label = "Level 3" },
                new AccessRestriction { Value = "PA4", Label = "Level 4" },
                new AccessRestriction { Value = "PA-unassessed", Label = "Unassessed" }
            ]
        },
        RightsAxis = new RightsAxis
        {
            Label = "Copyright Statement",
            Groups =
            [
                new RightsGroup { Key = "public-domain", Label = "Out of copyright", Members = ["NoC-US", "NKC", "CC0", "CC-BY"] },
                new RightsGroup { Key = "low-risk", Label = "In copyright, low risk", Members = ["InC-EDU", "InC-NC"] },
                new RightsGroup { Key = "restricted", Label = "In copyright (restrictions apply)", Members = ["InC"] },
                new RightsGroup
                {
                    Key = "unassessed", Label = "Unassessed",
                    Members = wildcardGroup ? ["CNE", "__none__", "*"] : ["CNE", "__none__"]
                }
            ]
        },
        Derived =
        [
            new DerivedAccessCondition
            {
                Type = "tier",
                Label = "Access Tier",
                Values =
                [
                    new AccessRestriction { Value = "0", Label = "Tier 0 - Open" },
                    new AccessRestriction { Value = "1", Label = "Tier 1 - Open" },
                    new AccessRestriction { Value = "2", Label = "Tier 2 - Restricted" },
                    new AccessRestriction { Value = "3", Label = "Tier 3 - Protected" },
                    new AccessRestriction { Value = "4", Label = "Tier 4 - Closed" },
                    new AccessRestriction { Value = "5", Label = "Tier 5 - Unassessed" }
                ],
                Matrix = new Dictionary<string, Dictionary<string, string>>
                {
                    ["PA1"] = new() { ["public-domain"] = "0", ["low-risk"] = "1", ["restricted"] = "2", ["unassessed"] = "2" },
                    ["PA2"] = new() { ["*"] = "3" },
                    ["PA3"] = new() { ["*"] = "3" },
                    ["PA4"] = new() { ["*"] = "4" },
                    ["PA-unassessed"] = new() { ["*"] = "5" },
                    ["__none__"] = new() { ["*"] = "5" }
                }
            }
        ]
    };

    [Theory]
    [InlineData("PA1", "http://rightsstatements.org/vocab/NoC-US/1.0/", "0")]
    [InlineData("PA1", "https://creativecommons.org/licenses/by/4.0/", "0")]
    [InlineData("PA1", "http://rightsstatements.org/vocab/InC-EDU/1.0/", "1")]
    [InlineData("PA1", "http://rightsstatements.org/vocab/InC/1.0/", "2")]
    [InlineData("PA1", "http://rightsstatements.org/vocab/CNE/1.0/", "2")]
    [InlineData("PA1", null, "2")]
    [InlineData("PA2", "http://rightsstatements.org/vocab/NoC-US/1.0/", "3")]
    [InlineData("PA3", "http://rightsstatements.org/vocab/InC/1.0/", "3")]
    [InlineData("PA4", null, "4")]
    [InlineData("PA-unassessed", "http://rightsstatements.org/vocab/NoC-US/1.0/", "5")]
    public void Every_cell_of_the_Leeds_matrix_resolves_to_the_agreed_tier(string pa, string? rights, string expectedTier)
    {
        var derived = Leeds().ResolveDerived([pa], rights == null ? null : new Uri(rights));
        derived.Should().Equal(new Dictionary<string, string> { ["tier"] = expectedTier });
    }

    [Fact]
    public void No_effective_restriction_uses_the_none_row()
    {
        Leeds().ResolveDerived([], NoCUs).Should().Equal(new Dictionary<string, string> { ["tier"] = "5" });
    }

    [Fact]
    public void A_rights_statement_in_no_group_has_no_derived_value_without_a_wildcard_group()
    {
        // CC-BY-SA is a real statement that this profile does not place in any group.
        var ccBySa = new Uri("https://creativecommons.org/licenses/by-sa/4.0/");
        Leeds().ResolveDerived(["PA1"], ccBySa).Should().BeEmpty();
        Leeds(wildcardGroup: true).ResolveDerived(["PA1"], ccBySa).Should().Equal(new Dictionary<string, string> { ["tier"] = "2" });
    }

    [Fact]
    public void An_unknown_URI_only_matches_a_wildcard_group()
    {
        var unknown = new Uri("https://example.org/licence");
        Leeds().ResolveDerived(["PA1"], unknown).Should().BeEmpty();
        Leeds(wildcardGroup: true).ResolveDerived(["PA1"], unknown)["tier"].Should().Be("2");
    }

    [Fact]
    public void A_restriction_value_outside_the_vocabulary_has_no_row_and_no_derived_value()
    {
        // Legacy content written before the vocabulary changed: the matrix has no row for it.
        Leeds().ResolveDerived(["dlip-open"], NoCUs).Should().BeEmpty();
    }

    [Fact]
    public void Several_effective_restrictions_resolve_to_the_most_restrictive_cell()
    {
        // Not reachable through the UI once anything is derived, but a legacy or third-party
        // document may carry several; the latest value in the Values order wins.
        Leeds().ResolveDerived(["PA1", "PA4"], NoCUs)["tier"].Should().Be("4");
        Leeds().ResolveDerived(["PA4", "PA1"], NoCUs)["tier"].Should().Be("4");
    }

    [Fact]
    public void Nothing_derived_resolves_to_nothing()
    {
        var simple = new AccessConditionProfile
        {
            RestrictionAxis = new AccessAxis { Values = [new AccessRestriction { Value = "Open", Label = "Open" }] }
        };
        simple.ResolveDerived(["Open"], InC).Should().BeEmpty();
        simple.Validate().Should().BeEmpty();
    }

    [Fact]
    public void The_Leeds_profile_and_an_empty_profile_are_valid()
    {
        Leeds().Validate().Should().BeEmpty();
        Leeds(wildcardGroup: true).Validate().Should().BeEmpty();
        new AccessConditionProfile().Validate().Should().BeEmpty("an absent section is a bare but usable profile");
    }

    [Fact]
    public void Multi_select_restrictions_are_refused_alongside_derived_conditions()
    {
        var profile = Leeds();
        profile.AllowMultipleRestrictions = true;
        profile.Validate().Should().ContainSingle(e => e.Contains("AllowMultipleRestrictions"));

        var simple = new AccessConditionProfile
        {
            AllowMultipleRestrictions = true,
            RestrictionAxis = new AccessAxis { Values = [new AccessRestriction { Value = "Open", Label = "Open" }] }
        };
        simple.Validate().Should().BeEmpty("multi-select is fine when nothing is derived");
    }

    [Fact]
    public void Matrix_references_must_name_configured_rows_columns_and_values()
    {
        var profile = Leeds();
        profile.Derived[0].Matrix["PA9"] = new Dictionary<string, string> { ["public-domain"] = "0" };
        profile.Derived[0].Matrix["PA1"]["medium-risk"] = "2";
        profile.Derived[0].Matrix["PA2"]["*"] = "9";

        var errors = profile.Validate();
        errors.Should().Contain(e => e.Contains("row 'PA9'"));
        errors.Should().Contain(e => e.Contains("column 'medium-risk'"));
        errors.Should().Contain(e => e.Contains("= '9'"));
    }

    [Fact]
    public void Rights_group_members_must_be_known_and_claimed_once()
    {
        var profile = Leeds();
        profile.RightsAxis.Groups[0].Members.Add("Not-A-Statement");
        profile.RightsAxis.Groups[1].Members.Add("InC"); // already in "restricted"

        var errors = profile.Validate();
        errors.Should().Contain(e => e.Contains("'Not-A-Statement'"));
        errors.Should().Contain(e => e.Contains("'InC' appears in more than one"));
    }

    [Fact]
    public void A_derived_type_may_not_be_a_chosen_type_and_types_are_distinct()
    {
        var profile = Leeds();
        profile.Derived[0].Type = "restriction on access";
        profile.Validate().Should().Contain(e => e.Contains("reserved"));

        profile = Leeds();
        profile.Derived.Add(Leeds().Derived[0]);
        profile.Validate().Should().Contain(e => e.Contains("Derived types contains 'tier' more than once"));
    }

    [Fact]
    public void Lookup_prefers_the_specific_key_over_the_wildcard_at_each_level()
    {
        var derived = new DerivedAccessCondition
        {
            Type = "t",
            Values = [new AccessRestriction { Value = "a", Label = "a" }, new AccessRestriction { Value = "b", Label = "b" }, new AccessRestriction { Value = "c", Label = "c" }],
            Matrix = new Dictionary<string, Dictionary<string, string>>
            {
                ["*"] = new() { ["*"] = "a", ["g1"] = "b" },
                ["r1"] = new() { ["*"] = "c" }
            }
        };
        derived.Lookup("r1", "g1").Should().Be("c", "the specific row wins even though the wildcard row has the specific column");
        derived.Lookup("r2", "g1").Should().Be("b");
        derived.Lookup("r2", "g9").Should().Be("a");
        derived.Lookup("r2", null).Should().Be("a");
        derived.LabelFor("b").Should().Be("b");
        derived.LabelFor("zzz").Should().BeNull();
    }
}
