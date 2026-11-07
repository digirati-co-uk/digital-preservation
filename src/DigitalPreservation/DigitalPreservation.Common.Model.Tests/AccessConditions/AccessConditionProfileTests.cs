using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.AccessConditions;
using FluentAssertions;

namespace DigitalPreservation.Common.Model.Tests.AccessConditions;

/// <summary>
/// The configured lookup behind derived access conditions (issue #433): the Leeds master matrix of
/// Protected Access Level x copyright status -> Access Tier, and the profile validation that runs
/// at Preservation API startup.
/// </summary>
public class AccessConditionProfileTests
{
    private static readonly Uri InC = new("http://rightsstatements.org/vocab/InC/1.0/");
    private static readonly Uri Cc0 = new("https://creativecommons.org/publicdomain/zero/1.0/");

    /// <summary>The Leeds profile as deployed to dev (Preservation.API/appsettings.json).</summary>
    public static AccessConditionProfile Leeds() => new()
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
        CategoryAxis = new CategoryAxis
        {
            Label = "Copyright status",
            Type = "copyright status",
            Values =
            [
                new AccessCategory { Value = "public-domain", Label = "Out of Copyright (Public Domain / CC0)", RightsStatements = ["NoC-US", "NoC-NC", "NoC-OKLR", "NoC-CR", "NKC", "CC0"] },
                new AccessCategory { Value = "low-risk", Label = "In Copyright: Assessed (Low Risk)", RightsStatements = ["InC", "InC-EDU", "InC-NC", "InC-RUU", "InC-OW-EU", "CC-BY", "CC-BY-SA", "CC-BY-ND", "CC-BY-NC", "CC-BY-NC-SA", "CC-BY-NC-ND"] },
                new AccessCategory { Value = "restrictions-apply", Label = "In Copyright (restrictions apply)", RightsStatements = ["InC", "InC-OW-EU", "InC-RUU", "NoC-OKLR", "NoC-CR"] },
                new AccessCategory { Value = "medium-risk", Label = "In Copyright (medium risk)", RightsStatements = ["InC", "InC-OW-EU", "InC-RUU"] },
                new AccessCategory { Value = "unassessed", Label = "In Copyright (Unassessed)", RightsStatements = ["CNE", "UND"] },
                new AccessCategory { Value = "high-risk", Label = "In Copyright (High Risk)", RightsStatements = ["InC", "InC-OW-EU", "InC-RUU"] }
            ]
        },
        RightsAxis = new RightsAxis { Label = "Copyright Statement" },
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
                    ["PA1"] = new()
                    {
                        ["public-domain"] = "0", ["low-risk"] = "1", ["restrictions-apply"] = "2",
                        ["medium-risk"] = "2", ["unassessed"] = "2", ["high-risk"] = "3", ["__none__"] = "2", ["*"] = "3"
                    },
                    ["PA2"] = new() { ["*"] = "3" },
                    ["PA3"] = new() { ["*"] = "3" },
                    ["PA4"] = new() { ["*"] = "4" },
                    ["PA-unassessed"] = new() { ["*"] = "5" },
                    ["__none__"] = new() { ["*"] = "5" },
                    ["*"] = new() { ["*"] = "5" }
                }
            }
        ]
    };

    /// <summary>
    /// Jodie's master matrix (wiki, 16 Oct), restated here independently of the profile so that a
    /// change to either fails loudly.
    /// </summary>
    public static IEnumerable<object?[]> EveryCell()
    {
        string?[] restrictions = ["PA1", "PA2", "PA3", "PA4", "PA-unassessed", null, "dlip-open"];
        // "renamed-status" stands for a category value outside the vocabulary - mistyped by an API caller,
        // or left behind by a rename - which the PA1 row's '*' cell must catch (re-review of #434, D).
        string?[] categories = ["public-domain", "low-risk", "restrictions-apply", "medium-risk", "unassessed", "high-risk", null, "renamed-status"];
        var pa1 = new Dictionary<string, string>
        {
            ["public-domain"] = "0", ["low-risk"] = "1", ["restrictions-apply"] = "2",
            ["medium-risk"] = "2", ["unassessed"] = "2", ["high-risk"] = "3"
        };
        foreach (var pa in restrictions)
        {
            foreach (var category in categories)
            {
                var expected = pa switch
                {
                    "PA1" => category == null ? "2" : pa1.GetValueOrDefault(category, "3"), // unknown status: the '*' cell
                    "PA2" or "PA3" => "3",
                    "PA4" => "4",
                    _ => "5" // PA-unassessed, no restriction (the __none__ row), or a value outside the vocabulary (the '*' row)
                };
                yield return [pa, category, expected];
            }
        }
    }

    [Theory]
    [MemberData(nameof(EveryCell))]
    public void Every_cell_of_the_Leeds_master_matrix_resolves_to_the_agreed_tier(string? pa, string? category, string expectedTier)
    {
        var derived = Leeds().ResolveDerived(pa == null ? [] : [pa], category);
        derived.Should().Equal(new Dictionary<string, string> { ["tier"] = expectedTier });
    }

    [Fact]
    public void The_rights_statement_is_not_an_input_to_the_matrix_but_suggests_a_category()
    {
        // The statement is the public claim, the status is the assessment: InC can sit under four
        // statuses with different tiers, so it can only suggest, never decide.
        Leeds().ResolveDerived(["PA1"], "high-risk").Should().Equal(new Dictionary<string, string> { ["tier"] = "3" });
        Leeds().CategoriesPermitting(InC).Should().BeEquivalentTo(["low-risk", "restrictions-apply", "medium-risk", "high-risk"]);
        Leeds().CategoriesPermitting(Cc0).Should().Equal("public-domain");
        Leeds().CategoriesPermitting(new Uri("https://example.org/licence")).Should().BeEmpty();
        Leeds().CategoriesPermitting(null).Should().BeEmpty();
    }

    [Fact]
    public void Every_rights_statement_is_permitted_under_at_least_one_Leeds_category()
    {
        var permitted = Leeds().CategoryAxis!.Values.SelectMany(c => c.RightsStatements).ToHashSet();
        permitted.Should().BeEquivalentTo(RightsStatement.ByShortLabel.Keys);
    }

    [Fact]
    public void Several_effective_restrictions_resolve_to_the_most_restrictive_cell()
    {
        // Not reachable through the UI once anything is derived, but a legacy or third-party
        // document may carry several; the latest value in the Values order wins.
        Leeds().ResolveDerived(["PA1", "PA4"], "public-domain")["tier"].Should().Be("4");
        Leeds().ResolveDerived(["PA4", "PA1"], "public-domain")["tier"].Should().Be("4");
    }

    [Fact]
    public void A_restriction_value_outside_the_vocabulary_gets_the_wildcard_row_or_nothing()
    {
        var profile = Leeds();
        profile.ResolveDerived(["dlip-open"], "public-domain").Should().Equal(new Dictionary<string, string> { ["tier"] = "5" });
        profile.Derived[0].Matrix.Remove("*");
        profile.ResolveDerived(["dlip-open"], "public-domain").Should().BeEmpty();
    }

    [Fact]
    public void The_standard_profile_derives_nothing_and_is_valid()
    {
        var simple = AccessConditionProfile.Simple([new AccessRestriction { Value = "Open", Label = "Open" }]);
        simple.CategoryAxis.Should().BeNull();
        simple.ResolveDerived(["Open"], null).Should().BeEmpty();
        simple.Validate().Should().BeEmpty();
        simple.CategoriesPermitting(InC).Should().BeEmpty();

        AccessConditionProfile.Simple([new AccessRestriction { Value = "Open", Label = "Open" }], allowMultiple: true)
            .Validate().Should().BeEmpty("multi-select is fine when nothing is derived");
        new AccessConditionProfile().Validate().Should().BeEmpty("an absent section is a bare but usable profile");
    }

    [Fact]
    public void The_Leeds_profile_is_valid()
    {
        Leeds().Validate().Should().BeEmpty();
    }

    [Fact]
    public void Multi_select_restrictions_are_refused_alongside_derived_conditions()
    {
        var profile = Leeds();
        profile.AllowMultipleRestrictions = true;
        profile.Validate().Should().ContainSingle(e => e.Contains("AllowMultipleRestrictions"));
    }

    [Fact]
    public void A_category_axis_and_derived_conditions_go_together()
    {
        var profile = Leeds();
        profile.Derived.Clear();
        profile.Validate().Should().ContainSingle(e => e.Contains("use the standard 'AccessConditions' list"));

        profile = Leeds();
        profile.CategoryAxis = null;
        profile.Validate().Should().Contain(e => e.Contains("need a CategoryAxis"));
    }

    [Fact]
    public void A_category_axis_needs_a_type_and_known_rights_statements()
    {
        var profile = Leeds();
        profile.CategoryAxis!.Type = "";
        profile.CategoryAxis.Values[0].RightsStatements.Add("Not-A-Statement");
        profile.CategoryAxis.Values[0].RightsStatements.Add("CC0"); // already listed there

        var errors = profile.Validate();
        errors.Should().Contain(e => e.Contains("CategoryAxis.Type"));
        errors.Should().Contain(e => e.Contains("'Not-A-Statement'"));
        errors.Should().Contain(e => e.Contains("contains 'CC0' more than once"));

        profile = Leeds();
        profile.CategoryAxis!.Type = "use and reproduction";
        profile.Validate().Should().Contain(e => e.Contains("reserved for the restriction or rights statement"));
    }

    [Fact]
    public void Matrix_references_must_name_configured_rows_columns_and_values()
    {
        var profile = Leeds();
        profile.Derived[0].Matrix["PA9"] = new Dictionary<string, string> { ["public-domain"] = "0" };
        profile.Derived[0].Matrix["PA1"]["very-high-risk"] = "2";
        profile.Derived[0].Matrix["PA2"]["*"] = "9";

        var errors = profile.Validate();
        errors.Should().Contain(e => e.Contains("row 'PA9'"));
        errors.Should().Contain(e => e.Contains("column 'very-high-risk'"));
        errors.Should().Contain(e => e.Contains("= '9'"));
    }

    [Fact]
    public void A_matrix_must_give_every_restriction_a_value_for_every_category()
    {
        var profile = Leeds();
        profile.Derived[0].Matrix.Remove("*");
        profile.Derived[0].Matrix["PA1"].Remove("low-risk");
        profile.Validate().Should().BeEmpty("the PA1 row's '*' cell covers the gap");
        profile.Derived[0].Matrix["PA1"].Remove("*");
        profile.Validate().Should().ContainSingle(e => e.Contains("no value for restriction 'PA1' with category 'low-risk'"));

        profile = Leeds();
        profile.Derived[0].Matrix.Remove("*");
        profile.Derived[0].Matrix.Remove("__none__");
        profile.Validate().Should().HaveCount(7, "no restriction at all must resolve for each of the six categories and for no category")
            .And.AllSatisfy(e => e.Should().Contain("restriction '__none__'"));

        profile = Leeds();
        profile.Derived[0].Matrix.Remove("__none__");
        profile.Validate().Should().BeEmpty("the wildcard row covers the gap");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_value_is_refused_on_every_axis(string blank)
    {
        // "" is what the forms post for "(inherit from parent)", so no configured value may be blank.
        var profile = Leeds();
        profile.RestrictionAxis.Values.Add(new AccessRestriction { Value = blank, Label = "Blank" });
        profile.Validate().Should().Contain(e => e.StartsWith("RestrictionAxis.Values contains a blank value"));

        profile = Leeds();
        profile.CategoryAxis!.Values.Add(new AccessCategory { Value = blank, Label = "Blank" });
        profile.Validate().Should().Contain(e => e.StartsWith("CategoryAxis.Values contains a blank value"));

        profile = Leeds();
        profile.Derived[0].Values.Add(new AccessRestriction { Value = blank, Label = "Blank" });
        profile.Validate().Should().Contain(e => e.StartsWith("Derived 'tier' Values contains a blank value"));
    }

    [Fact]
    public void A_derived_type_may_not_be_a_chosen_type_and_types_are_distinct()
    {
        var profile = Leeds();
        profile.Derived[0].Type = "restriction on access";
        profile.Validate().Should().Contain(e => e.Contains("reserved"));

        profile = Leeds();
        profile.Derived[0].Type = profile.CategoryAxis!.Type;
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
        derived.Lookup("r9", "g9").Should().Be("a");
        derived.Lookup("r9", "g1").Should().Be("b");
        derived.Lookup("r1", "g1").Should().Be("c", "a specific row wins even when the wildcard row has the specific column");
        derived.Lookup("r1", null).Should().Be("c");
    }
}
