using DigitalPreservation.Mets;
using FluentAssertions;

namespace XmlGen.Tests;

/// <summary>
/// Unit tests for IdRefs: resolution of METS IDREFS attributes that may be either a single
/// legacy ID containing spaces (split into tokens by the XmlSerializer) or a schema-valid
/// list of complete IDs. See docs/issues/188/issue-188-idrefs-plan.md.
/// </summary>
public class IdRefsTests
{
    private static readonly Dictionary<string, string> Elements = new()
    {
        ["ADM_objects/plain.pdf"] = "plain",
        ["ADM_objects/my file.pdf"] = "legacy-spaced",
        ["ADM_A"] = "a",
        ["ADM_B"] = "b"
    };

    private static string? Lookup(string id) => Elements.GetValueOrDefault(id);

    // The token collections under test, as fields rather than inline array arguments (CA1861).
    private static readonly string[] PlainToken = ["ADM_objects/plain.pdf"];
    private static readonly string[] UnknownToken = ["ADM_nope"];
    private static readonly string[] TokenA = ["ADM_A"];
    private static readonly string[] LegacySpacedTokens = ["ADM_objects/my", "file.pdf"];
    private static readonly string[] GenuinePair = ["ADM_A", "ADM_B"];
    private static readonly string[] SecondResolvesPair = ["ADM_missing", "ADM_B"];
    private static readonly string[] NothingResolvesPair = ["ADM_missing", "ADM_also_missing"];
    private static readonly string[] DuplicateTokens = ["ADM_A", "ADM_A"];

    // -----------------------------------------------------------------------
    // Collection overload (XmlGen side: tokens from the XmlSerializer's IDREFS split)
    // -----------------------------------------------------------------------

    [Fact]
    public void Empty_token_collection_resolves_to_null()
    {
        IdRefs.ResolveSingle(Array.Empty<string>(), Lookup).Should().BeNull();
    }

    [Fact]
    public void Single_token_resolves_directly()
    {
        IdRefs.ResolveSingle(PlainToken, Lookup).Should().Be("plain");
    }

    [Fact]
    public void Single_unmatched_token_resolves_to_null()
    {
        IdRefs.ResolveSingle(UnknownToken, Lookup).Should().BeNull();
    }

    [Fact]
    public void Legacy_spaced_id_split_into_tokens_resolves_via_the_joined_form()
    {
        // "ADM_objects/my file.pdf" arrives from the XmlSerializer as two tokens
        IdRefs.ResolveSingle(LegacySpacedTokens, Lookup)
            .Should().Be("legacy-spaced");
    }

    [Fact]
    public void Genuine_idrefs_list_resolves_to_the_first_matching_token()
    {
        IdRefs.ResolveSingle(GenuinePair, Lookup).Should().Be("a");
    }

    [Fact]
    public void Genuine_idrefs_list_falls_through_to_a_later_token_when_earlier_ones_miss()
    {
        IdRefs.ResolveSingle(SecondResolvesPair, Lookup).Should().Be("b");
    }

    [Fact]
    public void Nothing_matching_resolves_to_null()
    {
        IdRefs.ResolveSingle(NothingResolvesPair, Lookup).Should().BeNull();
    }

    [Fact]
    public void Joined_reconstructs_the_original_attribute_value()
    {
        IdRefs.Joined(LegacySpacedTokens).Should().Be("ADM_objects/my file.pdf");
    }

    // -----------------------------------------------------------------------
    // String overload (XDocument side: the whole attribute value as one string)
    // -----------------------------------------------------------------------

    [Fact]
    public void Whole_attribute_value_resolves_directly()
    {
        IdRefs.ResolveSingle("ADM_A", Lookup).Should().Be("a");
    }

    [Fact]
    public void Legacy_spaced_id_resolves_as_the_whole_attribute_value()
    {
        IdRefs.ResolveSingle("ADM_objects/my file.pdf", Lookup).Should().Be("legacy-spaced");
    }

    [Fact]
    public void Genuine_idrefs_attribute_value_splits_and_resolves_per_token()
    {
        IdRefs.ResolveSingle("ADM_missing ADM_B", Lookup).Should().Be("b");
    }

    [Fact]
    public void Unmatched_attribute_value_without_spaces_resolves_to_null()
    {
        IdRefs.ResolveSingle("ADM_nope", Lookup).Should().BeNull();
    }

    [Fact]
    public void Unmatched_attribute_value_with_spaces_resolves_to_null()
    {
        IdRefs.ResolveSingle("ADM_nope ADM_also_nope", Lookup).Should().BeNull();
    }

    [Theory]
    [InlineData("ADM_missing\tADM_B")]
    [InlineData("ADM_missing\nADM_B")]
    [InlineData("ADM_missing\r\nADM_B")]
    public void Idrefs_attribute_value_splits_on_any_xml_whitespace(string attributeValue)
    {
        // XML permits tab/newline/CR as IDREFS separators, not just spaces
        IdRefs.ResolveSingle(attributeValue, Lookup).Should().Be("b");
    }

    // -----------------------------------------------------------------------
    // ResolveAll (deletion sites: every referenced element, not just the first)
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveAll_of_an_empty_collection_is_empty()
    {
        IdRefs.ResolveAll(Array.Empty<string>(), Lookup).Should().BeEmpty();
    }

    [Fact]
    public void ResolveAll_of_a_single_token_resolves_that_element()
    {
        IdRefs.ResolveAll(TokenA, Lookup).Should().Equal("a");
    }

    [Fact]
    public void ResolveAll_of_legacy_spaced_tokens_is_exactly_the_one_legacy_element()
    {
        IdRefs.ResolveAll(LegacySpacedTokens, Lookup)
            .Should().Equal("legacy-spaced");
    }

    [Fact]
    public void ResolveAll_of_a_genuine_idrefs_list_resolves_every_token()
    {
        IdRefs.ResolveAll(GenuinePair, Lookup).Should().Equal("a", "b");
    }

    [Fact]
    public void ResolveAll_skips_tokens_that_do_not_resolve()
    {
        IdRefs.ResolveAll(SecondResolvesPair, Lookup).Should().Equal("b");
    }

    [Fact]
    public void ResolveAll_returns_distinct_elements_for_duplicate_tokens()
    {
        IdRefs.ResolveAll(DuplicateTokens, Lookup).Should().Equal("a");
    }

    // -----------------------------------------------------------------------
    // References / RemoveReference (pure token-collection bookkeeping)
    // -----------------------------------------------------------------------

    [Fact]
    public void References_matches_a_single_token()
    {
        IdRefs.References(TokenA, "ADM_A").Should().BeTrue();
    }

    [Fact]
    public void References_matches_one_token_of_a_genuine_list()
    {
        IdRefs.References(GenuinePair, "ADM_B").Should().BeTrue();
    }

    [Fact]
    public void References_matches_the_joined_legacy_form()
    {
        IdRefs.References(LegacySpacedTokens, "ADM_objects/my file.pdf")
            .Should().BeTrue();
    }

    [Fact]
    public void References_does_not_match_an_unrelated_id()
    {
        IdRefs.References(GenuinePair, "ADM_C").Should().BeFalse();
    }

    [Fact]
    public void RemoveReference_clears_all_tokens_of_a_legacy_spaced_id()
    {
        var tokens = new List<string> { "DMD_objects/my", "file.pdf" };
        IdRefs.RemoveReference(tokens, "DMD_objects/my file.pdf");
        tokens.Should().BeEmpty();
    }

    [Fact]
    public void RemoveReference_removes_only_the_matching_token_of_a_genuine_list()
    {
        var tokens = new List<string> { "DMD_A", "DMD_B" };
        IdRefs.RemoveReference(tokens, "DMD_A");
        tokens.Should().Equal("DMD_B");
    }

    [Fact]
    public void RemoveReference_leaves_tokens_untouched_when_nothing_matches()
    {
        var tokens = new List<string> { "DMD_A", "DMD_B" };
        IdRefs.RemoveReference(tokens, "DMD_C");
        tokens.Should().Equal("DMD_A", "DMD_B");
    }
}
