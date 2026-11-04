using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.AccessConditions;

/// <summary>
/// An installation's access-condition vocabulary (issue #433), as served by <c>GET /access-conditions</c>.
/// A node's access metadata is a small set of typed <c>mods:accessCondition</c> values, two of which
/// every installation has - the restriction (<c>restriction on access</c>) and the rights statement
/// (<c>use and reproduction</c>), both chosen by a person. An installation that needs a value
/// <em>computed</em> from its choices (for Leeds, the Access Tier) configures a third chosen axis -
/// the <see cref="CategoryAxis"/>, for Leeds the copyright status - and derives from restriction x
/// category by a matrix.
/// </summary>
/// <remarks>
/// Two configuration forms produce this one object (see the Preservation API's
/// <c>AddAccessConditionProfile</c>): the standard <c>AccessConditions</c> list of restriction values,
/// which fills only <see cref="RestrictionAxis"/>; and the <c>AccessConditionProfile</c> section, which
/// an installation uses only when it derives something. <see cref="Validate"/> is run at startup.
/// </remarks>
public class AccessConditionProfile
{
    /// <summary>The configuration section for the two-axis, derived form.</summary>
    public const string SectionName = "AccessConditionProfile";

    /// <summary>The configuration key for the standard form: a plain list of restriction values.</summary>
    public const string SimpleSectionName = "AccessConditions";

    /// <summary>The configuration key that, with the standard form, allows several restrictions per node.</summary>
    public const string AllowMultipleKey = "AllowMultipleAccessConditions";

    /// <summary>
    /// The matrix key (row or column) that stands for "no effective value": no restriction, or no
    /// category, on or above the node.
    /// </summary>
    public const string NoneKey = "__none__";

    /// <summary>A matrix row or column key that matches anything not matched by a more specific key.</summary>
    public const string Wildcard = "*";

    /// <summary>
    /// When true the restriction dropdown is a multi-select, as it was before #433. Rejected by
    /// <see cref="Validate"/> when anything is <see cref="Derived"/>: a derived value is one cell
    /// of a matrix, and a set of rows has no single cell.
    /// </summary>
    [JsonPropertyName("allowMultipleRestrictions")]
    public bool AllowMultipleRestrictions { get; set; }

    [JsonPropertyName("restrictionAxis")]
    public AccessAxis RestrictionAxis { get; set; } = new();

    /// <summary>
    /// The second chosen axis of the matrix - for Leeds, the copyright status - or null on an
    /// installation that derives nothing.
    /// </summary>
    [JsonPropertyName("categoryAxis")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CategoryAxis? CategoryAxis { get; set; }

    [JsonPropertyName("rightsAxis")]
    public RightsAxis RightsAxis { get; set; } = new();

    [JsonPropertyName("derived")]
    public List<DerivedAccessCondition> Derived { get; set; } = [];

    /// <summary>The standard form: a restriction vocabulary and nothing else.</summary>
    public static AccessConditionProfile Simple(IEnumerable<AccessRestriction> restrictions, bool allowMultiple = false) => new()
    {
        AllowMultipleRestrictions = allowMultiple,
        RestrictionAxis = new AccessAxis { Values = restrictions.ToList() }
    };

    /// <summary>
    /// Validation errors for the whole profile; empty when it is usable. Checked at startup.
    /// </summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        ValidateRestrictionAxis(errors);
        ValidateCategoryAxis(errors);
        if (AllowMultipleRestrictions && Derived.Count > 0)
            errors.Add("AllowMultipleRestrictions cannot be combined with Derived access conditions: a derived value is a single matrix cell.");
        if (CategoryAxis != null && Derived.Count == 0)
            errors.Add($"A {nameof(CategoryAxis)} is only needed to derive something from; with nothing Derived, use the standard '{SimpleSectionName}' list instead.");
        if (CategoryAxis == null && Derived.Count > 0)
            errors.Add($"Derived access conditions need a {nameof(CategoryAxis)}: the matrix columns are its values.");
        ValidateDerived(errors);
        return errors;
    }

    private void ValidateRestrictionAxis(List<string> errors)
    {
        // No restriction values at all is allowed (an absent section is a usable, if bare, profile:
        // the dropdown then offers only "inherit"); what is not allowed is an inconsistent one.
        AddDuplicates(errors, RestrictionAxis.Values.Select(v => v.Value), "RestrictionAxis.Values");
        if (RestrictionAxis.Values.Any(v => v.Value is NoneKey or Wildcard))
            errors.Add($"RestrictionAxis.Values may not use the reserved keys '{NoneKey}' or '{Wildcard}'.");
    }

    private void ValidateCategoryAxis(List<string> errors)
    {
        if (CategoryAxis == null) return;
        if (string.IsNullOrWhiteSpace(CategoryAxis.Type))
            errors.Add("CategoryAxis.Type (the mods:accessCondition type it is written under) is required.");
        else if (CategoryAxis.Type is ReservedTypes.RestrictionOnAccess or ReservedTypes.UseAndReproduction)
            errors.Add($"CategoryAxis.Type '{CategoryAxis.Type}' is reserved for the restriction or rights statement.");
        if (CategoryAxis.Values.Count == 0)
            errors.Add("CategoryAxis.Values is empty.");
        AddDuplicates(errors, CategoryAxis.Values.Select(v => v.Value), "CategoryAxis.Values");
        if (CategoryAxis.Values.Any(v => v.Value is NoneKey or Wildcard))
            errors.Add($"CategoryAxis.Values may not use the reserved keys '{NoneKey}' or '{Wildcard}'.");
        foreach (var category in CategoryAxis.Values)
        {
            AddDuplicates(errors, category.RightsStatements, $"CategoryAxis value '{category.Value}' RightsStatements");
            foreach (var unknown in category.RightsStatements.Where(rs => !RightsStatement.ByShortLabel.ContainsKey(rs)))
                errors.Add($"CategoryAxis value '{category.Value}' permits '{unknown}', which is not a known rights statement short label.");
        }
    }

    private void ValidateDerived(List<string> errors)
    {
        AddDuplicates(errors, Derived.Select(d => d.Type), "Derived types");
        var validRows = RestrictionAxis.Values.Select(v => v.Value).Append(NoneKey).Append(Wildcard).ToHashSet(StringComparer.Ordinal);
        var categories = CategoryAxis?.Values.Select(v => v.Value) ?? [];
        var validColumns = categories.Append(NoneKey).Append(Wildcard).ToHashSet(StringComparer.Ordinal);
        foreach (var derived in Derived)
        {
            ValidateDerivedCondition(errors, derived, validRows, validColumns);
        }
    }

    private void ValidateDerivedCondition(
        List<string> errors, DerivedAccessCondition derived, HashSet<string> validRows, HashSet<string> validColumns)
    {
        var name = $"Derived '{derived.Type}'";
        if (string.IsNullOrWhiteSpace(derived.Type))
            errors.Add("A Derived access condition has no Type.");
        else if (derived.Type is ReservedTypes.RestrictionOnAccess or ReservedTypes.UseAndReproduction || derived.Type == CategoryAxis?.Type)
            errors.Add($"{name}: the type is reserved for a chosen (not derived) access condition.");
        if (derived.Values.Count == 0)
            errors.Add($"{name} has no Values.");
        AddDuplicates(errors, derived.Values.Select(v => v.Value), $"{name} Values");
        var validValues = derived.Values.Select(v => v.Value).ToHashSet(StringComparer.Ordinal);
        if (derived.Matrix.Count == 0)
            errors.Add($"{name} has an empty Matrix.");
        foreach (var (row, cells) in derived.Matrix)
        {
            if (!validRows.Contains(row))
                errors.Add($"{name}: matrix row '{row}' is not a restriction value, '{NoneKey}' or '{Wildcard}'.");
            ValidateMatrixRow(errors, name, row, cells, validColumns, validValues);
        }
        if (derived.Matrix.Count > 0)
        {
            ValidateMatrixCoverage(errors, derived, name, validRows.Where(r => r != Wildcard), validColumns.Where(c => c != Wildcard));
        }
    }

    private static void ValidateMatrixRow(
        List<string> errors, string name, string row, Dictionary<string, string> cells,
        HashSet<string> validColumns, HashSet<string> validValues)
    {
        foreach (var (column, value) in cells)
        {
            if (!validColumns.Contains(column))
                errors.Add($"{name}: matrix column '{column}' in row '{row}' is not a category value, '{NoneKey}' or '{Wildcard}'.");
            if (!validValues.Contains(value))
                errors.Add($"{name}: matrix cell [{row}, {column}] = '{value}' is not one of its Values.");
        }
    }

    /// <summary>
    /// Every restriction value (and no restriction) must resolve for every category (and no
    /// category): a gap would silently leave a node with no derived value, which is what a legacy
    /// restriction value did before this check (review of #434).
    /// </summary>
    private static void ValidateMatrixCoverage(
        List<string> errors, DerivedAccessCondition derived, string name, IEnumerable<string> rows, IEnumerable<string> columns)
    {
        var columnList = columns.ToList();
        foreach (var row in rows)
        {
            foreach (var column in columnList.Where(column => derived.Lookup(row, column) == null))
            {
                errors.Add($"{name}: no value for restriction '{row}' with category '{column}' (add the cell, or a '{Wildcard}' row or column).");
            }
        }
    }

    /// <summary>
    /// The derived access conditions for a node, keyed by type, from its EFFECTIVE restriction(s)
    /// and category. A type is absent from the result when the matrix has no cell for the inputs.
    /// </summary>
    /// <remarks>
    /// Several effective restrictions cannot be chosen through the UI when anything is derived
    /// (see <see cref="Validate"/>), but a third-party or legacy document may still carry several:
    /// then each is looked up and the value latest in the derived condition's <c>Values</c> order
    /// wins, on the convention that values are listed least to most restrictive.
    /// </remarks>
    public Dictionary<string, string> ResolveDerived(IReadOnlyList<string> effectiveRestrictions, string? effectiveCategory)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Derived.Count == 0) return result;

        var column = effectiveCategory ?? NoneKey;
        IReadOnlyList<string> rows = effectiveRestrictions.Count == 0 ? [NoneKey] : effectiveRestrictions;

        foreach (var derived in Derived)
        {
            string? best = null;
            var bestRank = -1;
            foreach (var row in rows)
            {
                var value = derived.Lookup(row, column);
                if (value == null) continue;
                var rank = derived.Values.FindIndex(v => v.Value == value);
                if (rank > bestRank)
                {
                    best = value;
                    bestRank = rank;
                }
            }
            if (best != null) result[derived.Type] = best;
        }
        return result;
    }

    /// <summary>
    /// The categories under which a rights statement is permitted. One result lets a UI preselect
    /// the category; none means the statement is not in any list and any category may be chosen.
    /// </summary>
    public IReadOnlyList<string> CategoriesPermitting(Uri? rightsStatement)
    {
        if (CategoryAxis == null || rightsStatement == null || !RightsStatement.ByUri.TryGetValue(rightsStatement, out var rs))
            return [];
        return CategoryAxis.Values.Where(c => c.RightsStatements.Contains(rs.ShortLabel)).Select(c => c.Value).ToList();
    }

    private static void AddDuplicates(List<string> errors, IEnumerable<string> keys, string what)
    {
        foreach (var dup in keys.GroupBy(k => k, StringComparer.Ordinal).Where(g => g.Count() > 1))
            errors.Add($"{what} contains '{dup.Key}' more than once.");
    }

    /// <summary>
    /// The two <c>mods:accessCondition/@type</c> values every installation's chosen conditions use.
    /// Mirrors the constants in DigitalPreservation.Mets, which this project cannot reference.
    /// </summary>
    public static class ReservedTypes
    {
        public const string RestrictionOnAccess = "restriction on access";
        public const string UseAndReproduction = "use and reproduction";
    }
}

/// <summary>
/// The chosen restriction axis: its UI label and its vocabulary.
/// </summary>
public class AccessAxis
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "Access Restriction(s)";

    [JsonPropertyName("values")]
    public List<AccessRestriction> Values { get; set; } = [];
}

/// <summary>
/// The second chosen axis of a derived condition's matrix - for Leeds, the copyright status
/// (out of copyright, low risk, high risk...). Written to METS as <c>mods:accessCondition
/// type="{Type}"</c> and inherited like the restriction. It exists because the rights statement,
/// a public claim about what people may do, does not carry the internal risk assessment that
/// decides access; the two must be chosen separately.
/// </summary>
public class CategoryAxis
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "Category";

    /// <summary>The <c>@type</c> written to METS, e.g. <c>copyright status</c>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("values")]
    public List<AccessCategory> Values { get; set; } = [];
}

/// <summary>One category: a matrix column, with the rights statements an installation permits under it.</summary>
public class AccessCategory
{
    [JsonPropertyName("value")]
    public required string Value { get; set; }

    [JsonPropertyName("label")]
    public required string Label { get; set; }

    /// <summary>
    /// Rights statement short labels (<see cref="RightsStatement.ShortLabel"/>) that make sense
    /// under this category. A statement may appear under several categories. The UI offers these
    /// first once the category is chosen, and preselects the category when a chosen statement is
    /// permitted under exactly one; nothing is enforced on save.
    /// </summary>
    [JsonPropertyName("rightsStatements")]
    public List<string> RightsStatements { get; set; } = [];
}

/// <summary>
/// The rights axis. The vocabulary of record is the hard-coded <see cref="RightsStatement"/>
/// lists; config only labels the axis.
/// </summary>
public class RightsAxis
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "Rights statement";
}

/// <summary>
/// One derived access condition: written as <c>mods:accessCondition type="{Type}"</c> on every
/// node that carries a chosen condition of its own (and on files whose effective inputs come
/// partly from their logical range), with the value the matrix gives for the node's EFFECTIVE
/// restriction (row) and category (column).
/// </summary>
public class DerivedAccessCondition
{
    /// <summary>
    /// The <c>@type</c> written to METS, e.g. <c>tier</c>. Configurable so it can match what
    /// other producers (Goobi) write.
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    /// <summary>The possible values, least to most restrictive.</summary>
    [JsonPropertyName("values")]
    public List<AccessRestriction> Values { get; set; } = [];

    /// <summary>
    /// Restriction value (or <see cref="AccessConditionProfile.NoneKey"/> / <see cref="AccessConditionProfile.Wildcard"/>)
    /// -> category value (or the same reserved keys) -> derived value.
    /// </summary>
    [JsonPropertyName("matrix")]
    public Dictionary<string, Dictionary<string, string>> Matrix { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The cell for a row and column, a specific key beating the wildcard at each level.</summary>
    public string? Lookup(string row, string? column)
    {
        var cells = Matrix.GetValueOrDefault(row) ?? Matrix.GetValueOrDefault(AccessConditionProfile.Wildcard);
        if (cells == null) return null;
        if (column != null && cells.TryGetValue(column, out var exact)) return exact;
        return cells.GetValueOrDefault(AccessConditionProfile.Wildcard);
    }

    public string? LabelFor(string value) => Values.FirstOrDefault(v => v.Value == value)?.Label;
}
