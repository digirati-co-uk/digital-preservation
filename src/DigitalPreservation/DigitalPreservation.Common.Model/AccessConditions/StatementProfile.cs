using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.AccessConditions;

/// <summary>
/// The extension to the standard access model (issue #433), for an installation whose access and
/// rights rules are richer than "a restriction and a rights statement" - for Leeds, a Protected
/// Access Level and a specific copyright statement, from which an Access Tier is computed.
/// </summary>
/// <remarks>
/// <para>A person chooses a restriction (as in the standard model) and a <b>statement</b> from
/// <see cref="Values"/>, which replaces the rights statement. Each statement:</para>
/// <list type="bullet">
/// <item>falls into one of the <see cref="Categories"/> (for Leeds, the copyright status), which
/// are the columns of the <see cref="Derived"/> matrices;</item>
/// <item>names the <see cref="Iiif"/> entry saying what it publishes as in a IIIF manifest.</item>
/// </list>
/// <para>METS carries only keys: the chosen restriction and statement under their types, and each
/// derived value under its type. Categories and IIIF text are looked up here by whoever needs
/// them - the UI's preview, and iiif-builder reading the same document.</para>
/// </remarks>
public class StatementProfile
{
    /// <summary>The matrix key (row or column) for "no effective value": no restriction, or no statement.</summary>
    public const string NoneKey = "__none__";

    /// <summary>A matrix row or column key that matches anything not matched by a more specific key.</summary>
    public const string Wildcard = "*";

    /// <summary>The dropdown's label, e.g. "Copyright Statement".</summary>
    [JsonPropertyName("label")]
    public string Label { get; set; } = "Statement";

    /// <summary>The <c>mods:accessCondition/@type</c> the chosen statement is written under, e.g. <c>copyright statement</c>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("values")]
    public List<AccessStatement> Values { get; set; } = [];

    [JsonPropertyName("categories")]
    public List<AccessCategory> Categories { get; set; } = [];

    [JsonPropertyName("iiif")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IiifRightsConfig? Iiif { get; set; }

    [JsonPropertyName("derived")]
    public List<DerivedAccessCondition> Derived { get; set; } = [];

    public AccessStatement? Find(string? value) =>
        value == null ? null : Values.FirstOrDefault(s => s.Value == value);

    /// <summary>
    /// The matrix column for a node's effective statement: <see cref="NoneKey"/> when it has none,
    /// its category when the statement is known, and otherwise a key no column has, so that only a
    /// <see cref="Wildcard"/> cell matches (a statement from another source, or one since removed).
    /// </summary>
    public string ColumnFor(string? statement)
    {
        if (statement == null) return NoneKey;
        return Find(statement)?.Category ?? Wildcard;
    }

    /// <summary>What a node with this effective statement publishes as in IIIF, or null when nothing is configured.</summary>
    public IiifRightsEntry? IiifRightsFor(string? statement)
    {
        var key = Find(statement)?.Iiif;
        return key == null ? null : Iiif?.Entries.FirstOrDefault(e => e.Id == key);
    }

    /// <summary>
    /// The derived values for a node, keyed by type, from its EFFECTIVE restriction(s) and
    /// statement. A type is absent when its matrix has no cell for the inputs.
    /// </summary>
    /// <remarks>
    /// Several restrictions cannot be chosen through the UI when anything is derived, but a
    /// third-party or legacy document may carry several: each is looked up, and the value latest in
    /// the derived condition's <c>Values</c> wins, since values are listed least to most restrictive.
    /// </remarks>
    public Dictionary<string, string> ResolveDerived(IReadOnlyList<string> effectiveRestrictions, string? effectiveStatement)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var column = ColumnFor(effectiveStatement);
        IReadOnlyList<string> rows = effectiveRestrictions.Count == 0 ? [NoneKey] : effectiveRestrictions;
        foreach (var derived in Derived)
        {
            var mostRestrictive = rows
                .Select(row => derived.Lookup(row, column))
                .OfType<string>()
                .MaxBy(value => derived.Values.FindIndex(v => v.Value == value));
            if (mostRestrictive != null) result[derived.Type] = mostRestrictive;
        }
        return result;
    }
}

/// <summary>One choosable statement.</summary>
public class AccessStatement
{
    /// <summary>The key written to METS. Must never change once used: preserved METS holds it.</summary>
    [JsonPropertyName("value")]
    public required string Value { get; set; }

    /// <summary>What a person sees in the dropdown. Free to change.</summary>
    [JsonPropertyName("label")]
    public required string Label { get; set; }

    /// <summary>The <see cref="AccessCategory.Value"/> this statement falls into: its matrix column.</summary>
    [JsonPropertyName("category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; set; }

    /// <summary>The <see cref="IiifRightsEntry.Id"/> it publishes as, or null when it publishes nothing.</summary>
    [JsonPropertyName("iiif")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Iiif { get; set; }

    /// <summary>When to use it; shown to a person choosing.</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    /// <summary>Identifiers for the same statement in other systems, by scheme (e.g. EMu Rights SPRIGs).</summary>
    [JsonPropertyName("externalIds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, List<string>>? ExternalIds { get; set; }
}

/// <summary>A group of statements, and a column of the derived matrices. Never written to METS.</summary>
public class AccessCategory
{
    [JsonPropertyName("value")]
    public required string Value { get; set; }

    [JsonPropertyName("label")]
    public required string Label { get; set; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
}

/// <summary>
/// What statements publish as in a IIIF manifest. Each entry is defined once and referenced by key
/// from every statement that shares it, so the same words cannot drift apart.
/// </summary>
public class IiifRightsConfig
{
    /// <summary>The <c>requiredStatement</c> label for entries that do not give their own.</summary>
    [JsonPropertyName("requiredStatementLabel")]
    public string RequiredStatementLabel { get; set; } = "Rights";

    [JsonPropertyName("entries")]
    public List<IiifRightsEntry> Entries { get; set; } = [];
}

/// <summary>One published form: the manifest's <c>rights</c> URI and/or its <c>requiredStatement</c>.</summary>
public class IiifRightsEntry
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>The IIIF <c>rights</c> value: a Creative Commons or rightsstatements.org URI.</summary>
    [JsonPropertyName("rights")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Rights { get; set; }

    /// <summary>The IIIF <c>requiredStatement</c> value, as plain text.</summary>
    [JsonPropertyName("requiredStatement")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequiredStatement { get; set; }

    /// <summary>Overrides <see cref="IiifRightsConfig.RequiredStatementLabel"/> for this entry.</summary>
    [JsonPropertyName("requiredStatementLabel")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequiredStatementLabel { get; set; }
}

/// <summary>
/// One derived access condition (for Leeds, the Access Tier): never chosen, always computed from a
/// node's effective restriction (matrix row) and its statement's category (matrix column), and
/// written as <c>mods:accessCondition type="{Type}"</c>.
/// </summary>
public class DerivedAccessCondition
{
    /// <summary>The <c>@type</c> written to METS, e.g. <c>tier</c>; configurable to match other producers (Goobi).</summary>
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    /// <summary>The possible values, least to most restrictive.</summary>
    [JsonPropertyName("values")]
    public List<AccessRestriction> Values { get; set; } = [];

    /// <summary>
    /// Restriction value (or <see cref="StatementProfile.NoneKey"/> / <see cref="StatementProfile.Wildcard"/>)
    /// -> category value (or the same reserved keys) -> derived value.
    /// </summary>
    [JsonPropertyName("matrix")]
    public Dictionary<string, Dictionary<string, string>> Matrix { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The cell for a row and column, a specific key beating the wildcard at each level.</summary>
    public string? Lookup(string row, string? column)
    {
        var cells = Matrix.GetValueOrDefault(row) ?? Matrix.GetValueOrDefault(StatementProfile.Wildcard);
        if (cells == null) return null;
        if (column != null && cells.TryGetValue(column, out var exact)) return exact;
        return cells.GetValueOrDefault(StatementProfile.Wildcard);
    }

    public string? LabelFor(string value) => Values.FirstOrDefault(v => v.Value == value)?.Label;
}
