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
        IdRefs.ResolveSingle(new[] { "ADM_objects/plain.pdf" }, Lookup).Should().Be("plain");
    }

    [Fact]
    public void Single_unmatched_token_resolves_to_null()
    {
        IdRefs.ResolveSingle(new[] { "ADM_nope" }, Lookup).Should().BeNull();
    }

    [Fact]
    public void Legacy_spaced_id_split_into_tokens_resolves_via_the_joined_form()
    {
        // "ADM_objects/my file.pdf" arrives from the XmlSerializer as two tokens
        IdRefs.ResolveSingle(new[] { "ADM_objects/my", "file.pdf" }, Lookup)
            .Should().Be("legacy-spaced");
    }

    [Fact]
    public void Genuine_idrefs_list_resolves_to_the_first_matching_token()
    {
        IdRefs.ResolveSingle(new[] { "ADM_A", "ADM_B" }, Lookup).Should().Be("a");
    }

    [Fact]
    public void Genuine_idrefs_list_falls_through_to_a_later_token_when_earlier_ones_miss()
    {
        IdRefs.ResolveSingle(new[] { "ADM_missing", "ADM_B" }, Lookup).Should().Be("b");
    }

    [Fact]
    public void Nothing_matching_resolves_to_null()
    {
        IdRefs.ResolveSingle(new[] { "ADM_missing", "ADM_also_missing" }, Lookup).Should().BeNull();
    }

    [Fact]
    public void Joined_reconstructs_the_original_attribute_value()
    {
        IdRefs.Joined(new[] { "ADM_objects/my", "file.pdf" }).Should().Be("ADM_objects/my file.pdf");
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
}
