using System.Text.Json;
using DigitalPreservation.Common.Model.AccessConditions;
using FluentAssertions;

namespace DigitalPreservation.Common.Model.Tests.AccessConditions;

/// <summary>
/// The access condition profile (issue #433): the standard model, the deployed Leeds document read
/// exactly as the Preservation API reads it and checked cell by cell against Leeds' spreadsheet, and
/// the validation that runs at API startup, case by case.
/// </summary>
public class AccessConditionProfileTests
{
    /// <summary>The Leeds profile as deployed (Preservation.API/AccessConditionProfiles/leeds.json).</summary>
    public static AccessConditionProfile Leeds() =>
        AccessConditionProfile.FromJson(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AccessConditionProfiles", "leeds.json")));

    [Fact]
    public void The_Leeds_profile_is_plain_JSON()
    {
        // Our reader skips comments, but other consumers of the same document (a Python IIIF
        // builder, Sonar's JSON analyser) accept neither comments nor trailing commas.
        var text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AccessConditionProfiles", "leeds.json"));
        var parse = () => JsonDocument.Parse(text);
        parse.Should().NotThrow();
    }

    // ---- The standard model -----------------------------------------------------------------

    [Fact]
    public void The_standard_profile_is_restrictions_and_rights_and_nothing_else()
    {
        var simple = AccessConditionProfile.Simple([new AccessRestriction { Value = "Open", Label = "Open" }]);
        simple.HasStatements.Should().BeFalse();
        simple.Statements.Should().BeNull();
        simple.Validate().Should().BeEmpty();

        AccessConditionProfile.Simple([new AccessRestriction { Value = "Open", Label = "Open" }], allowMultiple: true)
            .Validate().Should().BeEmpty("multi-select is fine when nothing is derived");
        new AccessConditionProfile().Validate().Should().BeEmpty("no configuration at all is a bare but usable profile");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_restriction_value_is_refused(string blank)
    {
        // "" is what the forms post for "(inherit from parent)", so no configured value may be blank.
        var profile = AccessConditionProfile.Simple([new AccessRestriction { Value = blank, Label = "Blank" }]);
        profile.Validate().Should().ContainSingle(e => e.StartsWith("restrictionAxis.values contains a blank value"));
    }

    // ---- The Leeds document, against the spreadsheet ----------------------------------------

    /// <summary>
    /// The tier each statement gives under Protected Access Level 1, restated independently of the
    /// document from "Access Control - MASTER.xlsx": the statement's copyright status column on the
    /// DATA FOR DIGIRATI tab, and that column's PA1 cell in the v1.0 matrix. Under every other
    /// level the tier does not depend on the statement.
    /// </summary>
    private static readonly Dictionary<string, string> Pa1TierByStatement = new()
    {
        ["out-of-copyright"] = "0",            // Out of Copyright
        ["inc-low-risk"] = "1",                // In Copyright: Assessed (Low Risk)
        ["inc-low-risk-orphan"] = "1",
        ["inc-low-risk-orphan-2039"] = "1",
        ["inc-low-risk-uol"] = "1",
        ["inc-medium-risk-terminal"] = "2",    // medium risk
        ["inc-high-risk"] = "2",               // high risk ("restrictions apply" on the tab)
        ["inc-high-risk-orphan"] = "2",
        ["inc-high-risk-orphan-2039"] = "2",
        ["inc-high-risk-uol"] = "2",
        ["inc-contractual"] = "2",
        ["inc-unassessed"] = "2",              // In Copyright (Unassessed)
        ["inc-holder-preservation"] = "3"      // Holder Conditions (High Risk)
    };

    public static TheoryData<string?, string?, string> EveryCell()
    {
        var data = new TheoryData<string?, string?, string>();
        // null restriction: none on the node or above it; "dlip-open": a value outside the vocabulary.
        string?[] restrictions = ["PA1", "PA2", "PA3", "PA4", "PA-unassessed", null, "dlip-open"];
        // null statement: none chosen; "renamed-statement": one outside the vocabulary (another
        // source's, or since removed), which the PA1 row's '*' cell must catch.
        var statements = Pa1TierByStatement.Keys.Cast<string?>().Append(null).Append("renamed-statement");
        foreach (var pa in restrictions)
        {
            foreach (var statement in statements)
            {
                var expected = pa switch
                {
                    "PA1" when statement == null => "2",                       // no statement: treated as unassessed
                    "PA1" => Pa1TierByStatement.GetValueOrDefault(statement!, "3"), // unknown statement: the '*' cell
                    "PA2" or "PA3" => "3",
                    "PA4" => "4",
                    _ => "5" // PA-unassessed, no restriction (the __none__ row), or outside the vocabulary (the '*' row)
                };
                data.Add(pa, statement, expected);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryCell))]
    public void Every_restriction_and_statement_resolves_to_the_agreed_tier(string? pa, string? statement, string expectedTier)
    {
        var derived = Leeds().Statements!.ResolveDerived(pa == null ? [] : [pa], statement);
        derived.Should().Equal(new Dictionary<string, string> { ["tier"] = expectedTier });
    }

    [Fact]
    public void The_deployed_Leeds_document_is_valid_and_covers_every_statement()
    {
        var profile = Leeds();
        profile.Validate().Should().BeEmpty();
        profile.Statements!.Values.Select(s => s.Value).Should().BeEquivalentTo(Pa1TierByStatement.Keys,
            "this test restates the spreadsheet; a statement added to one must be added to the other");
    }

    [Fact]
    public void Every_Leeds_statement_publishes_something_in_IIIF()
    {
        var statements = Leeds().Statements!;
        foreach (var statement in statements.Values)
        {
            var entry = statements.IiifRightsFor(statement.Value);
            entry.Should().NotBeNull($"{statement.Value} must say what it publishes as");
            entry!.RequiredStatement.Should().NotBeNullOrWhiteSpace($"{statement.Value} must have requiredStatement text");
        }
        statements.IiifRightsFor("out-of-copyright")!.Rights.Should().Be("https://creativecommons.org/publicdomain/mark/1.0/");
        statements.IiifRightsFor(null).Should().BeNull();
        statements.IiifRightsFor("renamed-statement").Should().BeNull();
    }

    [Fact]
    public void Statements_that_share_IIIF_text_share_one_entry()
    {
        // The point of referencing entries by key: the same words are stated once.
        var statements = Leeds().Statements!;
        statements.IiifRightsFor("inc-high-risk").Should().BeSameAs(statements.IiifRightsFor("inc-medium-risk-terminal"));
        statements.Iiif!.Entries.Select(e => e.RequiredStatement).Where(text => !text!.StartsWith("[PLACEHOLDER"))
            .Should().OnlyHaveUniqueItems("two entries with the same text should be one entry");
    }

    [Fact]
    public void The_matrix_column_is_the_statements_category()
    {
        var statements = Leeds().Statements!;
        statements.ColumnFor(null).Should().Be(StatementProfile.NoneKey);
        statements.ColumnFor("inc-low-risk-orphan-2039").Should().Be("low-risk");
        statements.ColumnFor("renamed-statement").Should().Be(StatementProfile.Wildcard);
    }

    [Fact]
    public void Several_effective_restrictions_resolve_to_the_most_restrictive_cell()
    {
        // Not reachable through the UI once anything is derived, but a legacy or third-party
        // document may carry several; the latest value in the Values order wins.
        var statements = Leeds().Statements!;
        statements.ResolveDerived(["PA1", "PA4"], "out-of-copyright")["tier"].Should().Be("4");
        statements.ResolveDerived(["PA4", "PA1"], "out-of-copyright")["tier"].Should().Be("4");
    }

    [Fact]
    public void A_restriction_value_outside_the_vocabulary_gets_the_wildcard_row_or_nothing()
    {
        var statements = Leeds().Statements!;
        statements.ResolveDerived(["dlip-open"], "out-of-copyright").Should().Equal(new Dictionary<string, string> { ["tier"] = "5" });
        statements.Derived[0].Matrix.Remove("*");
        statements.ResolveDerived(["dlip-open"], "out-of-copyright").Should().BeEmpty();
    }

    // ---- The document format ----------------------------------------------------------------

    [Fact]
    public void The_served_JSON_is_the_document_format()
    {
        // GET /access-conditions serialises the profile with default options; that output must read
        // back through the strict document reader, so a consumer can treat the two as one format.
        var served = JsonSerializer.Serialize(Leeds());
        var reread = AccessConditionProfile.FromJson(served);
        reread.Validate().Should().BeEmpty();
        JsonSerializer.Serialize(reread).Should().Be(served);
    }

    [Fact]
    public void An_unknown_property_in_the_document_is_an_error()
    {
        var act = () => AccessConditionProfile.FromJson("""{ "restrictionAxis": { "label": "x", "valeus": [] } }""");
        act.Should().Throw<JsonException>().WithMessage("*valeus*");
    }

    [Fact]
    public void The_document_may_carry_comments_and_trailing_commas()
    {
        var profile = AccessConditionProfile.FromJson("""
            // a comment
            { "restrictionAxis": { "label": "Level", "values": [ { "value": "A", "label": "A" }, ], }, }
            """);
        profile.RestrictionAxis.Values.Should().ContainSingle();
    }

    // ---- Validation of the statements extension ---------------------------------------------

    [Fact]
    public void Statements_without_anything_derived_are_valid()
    {
        // An installation may want statements with IIIF text and no tier at all.
        var profile = Leeds();
        var statements = profile.Statements!;
        statements.Derived.Clear();
        statements.Categories.Clear();
        foreach (var statement in statements.Values) statement.Category = null;
        profile.Validate().Should().BeEmpty();
    }

    [Fact]
    public void Multi_select_restrictions_are_refused_alongside_derived_conditions()
    {
        var profile = Leeds();
        profile.AllowMultipleRestrictions = true;
        profile.Validate().Should().ContainSingle(e => e.Contains("allowMultipleRestrictions"));
    }

    [Fact]
    public void Every_reference_between_parts_of_the_profile_must_resolve()
    {
        var profile = Leeds();
        var values = profile.Statements!.Values;
        values[0].Category = "very-low-risk";
        values[1].Iiif = "no-such-entry";
        values[2].Category = null;

        var errors = profile.Validate();
        errors.Should().Contain(e => e.Contains("category 'very-low-risk' is not one of statements.categories"));
        errors.Should().Contain(e => e.Contains("iiif 'no-such-entry' is not one of statements.iiif.entries"));
        errors.Should().Contain(e => e.Contains($"statement '{values[2].Value}' has no category"));
    }

    [Fact]
    public void Statements_need_an_unreserved_type_and_values()
    {
        var profile = Leeds();
        profile.Statements!.Type = "";
        profile.Validate().Should().Contain(e => e.Contains("statements.type"));

        profile = Leeds();
        profile.Statements!.Type = "use and reproduction";
        profile.Validate().Should().Contain(e => e.Contains("reserved for the standard restriction or rights statement"));

        profile = Leeds();
        profile.Statements!.Values.Clear();
        profile.Validate().Should().Contain(e => e.Contains("statements.values is empty"));
    }

    [Fact]
    public void IIIF_entries_must_publish_something_well_formed()
    {
        var profile = Leeds();
        var entries = profile.Statements!.Iiif!.Entries;
        entries.Add(new IiifRightsEntry { Id = "empty" });
        entries.Add(new IiifRightsEntry { Id = "relative", Rights = "/licence" });
        entries.Add(new IiifRightsEntry { Id = "unknown", Rights = "https://example.org/my-licence" });
        entries.Add(new IiifRightsEntry { Id = "blank", RequiredStatement = "  " });
        entries.Add(new IiifRightsEntry { Id = "out-of-copyright", Rights = "https://example.org/" });

        var errors = profile.Validate();
        errors.Should().Contain(e => e.Contains("'empty' has neither rights nor requiredStatement"));
        errors.Should().Contain(e => e.Contains("'/licence' is not one of the platform's rights statements"));
        errors.Should().Contain(e => e.Contains("'https://example.org/my-licence' is not one of the platform's rights statements"),
            "rights is written into METS, so it must be a long-lived statement the platform knows");
        errors.Should().Contain(e => e.Contains("'blank': requiredStatement is blank"));
        errors.Should().Contain(e => e.Contains("statements.iiif.entries contains 'out-of-copyright' more than once"));
    }

    [Fact]
    public void An_external_id_may_belong_to_only_one_value_of_an_axis()
    {
        var profile = Leeds();
        var statements = profile.Statements!;
        statements.Values[0].ExternalIds = new() { ["emu-sprig"] = ["SPRIG-164"] }; // inc-unassessed's
        statements.Derived[0].Values[1].ExternalIds!["emu-sprig"] = ["SPRIG-4006"];  // tier 0's

        var errors = profile.Validate();
        errors.Should().Contain(e => e.Contains("emu-sprig id 'SPRIG-164' belongs to both"));
        errors.Should().Contain(e => e.Contains("emu-sprig id 'SPRIG-4006' belongs to both"));
    }

    [Fact]
    public void Matrix_references_must_name_configured_rows_columns_and_values()
    {
        var profile = Leeds();
        var matrix = profile.Statements!.Derived[0].Matrix;
        matrix["PA9"] = new Dictionary<string, string> { ["public-domain"] = "0" };
        matrix["PA1"]["very-high-risk"] = "2";
        matrix["PA2"]["*"] = "9";

        var errors = profile.Validate();
        errors.Should().Contain(e => e.Contains("row 'PA9'"));
        errors.Should().Contain(e => e.Contains("column 'very-high-risk'"));
        errors.Should().Contain(e => e.Contains("= '9'"));
    }

    [Fact]
    public void A_matrix_must_give_every_restriction_a_value_for_every_category()
    {
        var profile = Leeds();
        var matrix = profile.Statements!.Derived[0].Matrix;
        matrix.Remove("*");
        matrix["PA1"].Remove("low-risk");
        profile.Validate().Should().BeEmpty("the PA1 row's '*' cell covers the gap");
        matrix["PA1"].Remove("*");
        profile.Validate().Should().ContainSingle(e => e.Contains("no value for restriction 'PA1' with category 'low-risk'"));

        profile = Leeds();
        matrix = profile.Statements!.Derived[0].Matrix;
        matrix.Remove("*");
        matrix.Remove("__none__");
        profile.Validate().Should().HaveCount(7, "no restriction at all must resolve for each of the six categories and for no statement")
            .And.AllSatisfy(e => e.Should().Contain("restriction '__none__'"));

        profile = Leeds();
        profile.Statements!.Derived[0].Matrix.Remove("__none__");
        profile.Validate().Should().BeEmpty("the wildcard row covers the gap");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_value_is_refused_on_every_part_of_the_extension(string blank)
    {
        var profile = Leeds();
        profile.Statements!.Values.Add(new AccessStatement { Value = blank, Label = "Blank", Category = "low-risk" });
        profile.Validate().Should().Contain(e => e.StartsWith("statements.values contains a blank value"));

        profile = Leeds();
        profile.Statements!.Categories.Add(new AccessCategory { Value = blank, Label = "Blank" });
        profile.Validate().Should().Contain(e => e.StartsWith("statements.categories contains a blank value"));

        profile = Leeds();
        profile.Statements!.Derived[0].Values.Add(new AccessRestriction { Value = blank, Label = "Blank" });
        profile.Validate().Should().Contain(e => e.StartsWith("derived 'tier' values contains a blank value"));
    }

    [Fact]
    public void A_derived_type_may_not_be_a_chosen_type_and_types_are_distinct()
    {
        var profile = Leeds();
        profile.Statements!.Derived[0].Type = "restriction on access";
        profile.Validate().Should().Contain(e => e.Contains("reserved"));

        profile = Leeds();
        profile.Statements!.Derived[0].Type = profile.Statements.Type;
        profile.Validate().Should().Contain(e => e.Contains("reserved"));

        profile = Leeds();
        profile.Statements!.Derived.Add(Leeds().Statements!.Derived[0]);
        profile.Validate().Should().Contain(e => e.Contains("statements.derived types contains 'tier' more than once"));
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
