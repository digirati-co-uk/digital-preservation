using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.AccessConditions;

/// <summary>
/// An installation's access-condition vocabulary (issue #433). A node's access metadata is a small
/// set of typed <c>mods:accessCondition</c> values: two are chosen by a person - the restriction
/// (<c>restriction on access</c>) and the rights statement (<c>use and reproduction</c>) - and any
/// number more are <em>derived</em> from those two by a configured lookup (for Leeds, the Access
/// Tier). Everything beyond <see cref="RestrictionAxis"/>.<see cref="AccessAxis.Values"/> is optional:
/// an installation that configures nothing else gets two dropdowns, two METS entries and nothing
/// derived, which is the platform's pre-#433 behaviour apart from single-select restrictions.
/// </summary>
/// <remarks>
/// Bound from the <c>AccessConditionProfile</c> configuration section in the Preservation API and
/// served to the UI by <c>GET /access-conditions</c>. <see cref="Validate"/> is run at startup.
/// </remarks>
public class AccessConditionProfile
{
    public const string SectionName = "AccessConditionProfile";

    /// <summary>
    /// The matrix key (restriction row or rights group) that stands for "no value": no effective
    /// restriction, or no effective rights statement (absent or explicitly suppressed).
    /// </summary>
    public const string NoneKey = "__none__";

    /// <summary>
    /// A matrix row or column key, or a rights-group member, that matches anything not matched
    /// by a more specific key.
    /// </summary>
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

    [JsonPropertyName("rightsAxis")]
    public RightsAxis RightsAxis { get; set; } = new();

    [JsonPropertyName("derived")]
    public List<DerivedAccessCondition> Derived { get; set; } = [];

    /// <summary>
    /// Validation errors for the whole profile; empty when it is usable. Checked at startup.
    /// </summary>
    public List<string> Validate()
    {
        var errors = new List<string>();

        // No restriction values at all is allowed (an absent section is a usable, if bare, profile:
        // the dropdown then offers only "inherit"); what is not allowed is an inconsistent one.
        AddDuplicates(errors, RestrictionAxis.Values.Select(v => v.Value), "RestrictionAxis.Values");
        if (RestrictionAxis.Values.Any(v => v.Value is NoneKey or Wildcard))
            errors.Add($"RestrictionAxis.Values may not use the reserved keys '{NoneKey}' or '{Wildcard}'.");

        var groupKeys = RightsAxis.Groups.Select(g => g.Key).ToList();
        AddDuplicates(errors, groupKeys, "RightsAxis.Groups keys");
        if (groupKeys.Contains(Wildcard))
            errors.Add($"A rights group may not use the reserved key '{Wildcard}'.");
        var seenMembers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in RightsAxis.Groups)
        {
            if (group.Members.Count == 0)
                errors.Add($"Rights group '{group.Key}' has no members.");
            foreach (var member in group.Members)
            {
                if (member is not (NoneKey or Wildcard) && !RightsStatement.ByShortLabel.ContainsKey(member))
                    errors.Add($"Rights group '{group.Key}' member '{member}' is not a known rights statement short label.");
                if (!seenMembers.Add(member))
                    errors.Add($"Rights statement '{member}' appears in more than one rights group.");
            }
        }

        if (AllowMultipleRestrictions && Derived.Count > 0)
            errors.Add("AllowMultipleRestrictions cannot be combined with Derived access conditions: a derived value is a single matrix cell.");

        AddDuplicates(errors, Derived.Select(d => d.Type), "Derived types");
        var validRows = RestrictionAxis.Values.Select(v => v.Value).Append(NoneKey).Append(Wildcard).ToHashSet(StringComparer.Ordinal);
        var validColumns = groupKeys.Append(Wildcard).ToHashSet(StringComparer.Ordinal);
        foreach (var derived in Derived)
        {
            var name = $"Derived '{derived.Type}'";
            if (string.IsNullOrWhiteSpace(derived.Type))
                errors.Add("A Derived access condition has no Type.");
            else if (derived.Type is ReservedTypes.RestrictionOnAccess or ReservedTypes.UseAndReproduction)
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
                foreach (var (column, value) in cells)
                {
                    if (!validColumns.Contains(column))
                        errors.Add($"{name}: matrix column '{column}' in row '{row}' is not a rights group key or '{Wildcard}'.");
                    if (!validValues.Contains(value))
                        errors.Add($"{name}: matrix cell [{row}, {column}] = '{value}' is not one of its Values.");
                }
            }
        }

        return errors;
    }

    /// <summary>
    /// The rights group an effective rights statement falls in, or null when no group claims it.
    /// Absent and explicitly-suppressed rights both resolve to the <see cref="NoneKey"/> member;
    /// a URI is matched by its short label, then by a group holding the <see cref="Wildcard"/>.
    /// </summary>
    public string? FindRightsGroupKey(Uri? effectiveRights)
    {
        var member = effectiveRights == null
            ? NoneKey
            : RightsStatement.ByUri.TryGetValue(effectiveRights, out var rs) ? rs.ShortLabel : null;

        if (member != null)
        {
            var exact = RightsAxis.Groups.FirstOrDefault(g => g.Members.Contains(member));
            if (exact != null) return exact.Key;
        }
        return RightsAxis.Groups.FirstOrDefault(g => g.Members.Contains(Wildcard))?.Key;
    }

    /// <summary>
    /// The derived access conditions for a node, keyed by type, from its EFFECTIVE restriction(s)
    /// and rights statement. A type is absent from the result when the matrix has no cell for the
    /// inputs (no row, no column, or no group claims the rights statement).
    /// </summary>
    /// <remarks>
    /// Several effective restrictions cannot be chosen through the UI when anything is derived
    /// (see <see cref="Validate"/>), but a third-party or legacy document may still carry several:
    /// then each is looked up and the value latest in the derived condition's <c>Values</c> order
    /// wins, on the convention that values are listed least to most restrictive.
    /// </remarks>
    public Dictionary<string, string> ResolveDerived(IReadOnlyList<string> effectiveRestrictions, Uri? effectiveRights)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Derived.Count == 0) return result;

        var column = FindRightsGroupKey(effectiveRights);
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

    private static void AddDuplicates(List<string> errors, IEnumerable<string> keys, string what)
    {
        foreach (var dup in keys.GroupBy(k => k, StringComparer.Ordinal).Where(g => g.Count() > 1))
            errors.Add($"{what} contains '{dup.Key}' more than once.");
    }

    /// <summary>
    /// The two <c>mods:accessCondition/@type</c> values that are chosen, never derived. Mirrors the
    /// constants in DigitalPreservation.Mets, which this project cannot reference.
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
/// The rights axis. The vocabulary of record is the hard-coded <see cref="RightsStatement"/>
/// lists; config only labels the axis and, optionally, GROUPS the statements. When there are
/// groups, the dropdown shows them as optgroups, hides any statement not in a group, and the
/// group keys are the columns of every derived condition's matrix.
/// </summary>
public class RightsAxis
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = "Rights statement";

    [JsonPropertyName("groups")]
    public List<RightsGroup> Groups { get; set; } = [];
}

public class RightsGroup
{
    [JsonPropertyName("key")]
    public required string Key { get; set; }

    [JsonPropertyName("label")]
    public required string Label { get; set; }

    /// <summary>
    /// Rights statement short labels (<see cref="RightsStatement.ShortLabel"/>), plus optionally
    /// <see cref="AccessConditionProfile.NoneKey"/> (no effective rights statement) and
    /// <see cref="AccessConditionProfile.Wildcard"/> (any statement no other group claims).
    /// </summary>
    [JsonPropertyName("members")]
    public List<string> Members { get; set; } = [];
}

/// <summary>
/// One derived access condition: written as <c>mods:accessCondition type="{Type}"</c> on every
/// node that carries an explicit restriction or rights statement, with the value the matrix gives
/// for the node's EFFECTIVE restriction (row) and rights group (column).
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

    /// <summary>The possible values, listed least to most restrictive.</summary>
    [JsonPropertyName("values")]
    public List<AccessRestriction> Values { get; set; } = [];

    /// <summary>
    /// Row key (a restriction value, <c>__none__</c> or <c>*</c>) to column key (a rights group
    /// key or <c>*</c>) to derived value. A specific key beats the wildcard at each level.
    /// </summary>
    [JsonPropertyName("matrix")]
    public Dictionary<string, Dictionary<string, string>> Matrix { get; set; } = new(StringComparer.Ordinal);

    public string? Lookup(string row, string? column)
    {
        var cells = Matrix.GetValueOrDefault(row) ?? Matrix.GetValueOrDefault(AccessConditionProfile.Wildcard);
        if (cells == null) return null;
        if (column != null && cells.TryGetValue(column, out var exact)) return exact;
        return cells.GetValueOrDefault(AccessConditionProfile.Wildcard);
    }

    public string? LabelFor(string value) => Values.FirstOrDefault(v => v.Value == value)?.Label;
}
