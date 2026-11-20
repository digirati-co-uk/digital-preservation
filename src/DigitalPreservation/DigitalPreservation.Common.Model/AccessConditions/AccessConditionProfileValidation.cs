namespace DigitalPreservation.Common.Model.AccessConditions;

/// <summary>
/// Everything that must hold for a profile to be usable (issue #433), checked at startup. The
/// standard model needs little: unique, non-blank restriction values. A <see cref="StatementProfile"/>
/// is a hand-edited document that will grow, so every reference between its parts is checked too -
/// statement to category, statement to IIIF entry, matrix row to restriction, matrix column to
/// category, matrix cell to derived value - and every gap in a matrix is reported, because each
/// would otherwise surface as a silently missing value in preserved METS.
/// </summary>
internal sealed class AccessConditionProfileValidation(AccessConditionProfile profile)
{
    private const string NoneKey = StatementProfile.NoneKey;
    private const string Wildcard = StatementProfile.Wildcard;
    private readonly List<string> errors = [];

    public List<string> Run()
    {
        // The standard model.
        var restrictions = profile.RestrictionAxis.Values;
        CheckKeys(restrictions.Select(v => v.Value), "restrictionAxis.values");
        CheckExternalIds(restrictions.Select(v => (v.Value, v.ExternalIds)), "restrictionAxis.values");

        // The extension.
        var statements = profile.Statements;
        if (statements != null)
        {
            if (profile.AllowMultipleRestrictions && statements.Derived.Count > 0)
                errors.Add("allowMultipleRestrictions cannot be combined with derived access conditions: a derived value is a single matrix cell.");
            ValidateStatements(statements);
            ValidateIiif(statements);
            foreach (var derived in statements.Derived)
            {
                ValidateDerived(statements, derived);
            }
            CheckDuplicates(statements.Derived.Select(d => d.Type), "statements.derived types");
        }
        return errors;
    }

    private void ValidateStatements(StatementProfile statements)
    {
        if (string.IsNullOrWhiteSpace(statements.Type))
            errors.Add("statements.type (the mods:accessCondition type a statement is written under) is required.");
        else if (IsStandardType(statements.Type))
            errors.Add($"statements.type '{statements.Type}' is reserved for the standard restriction or rights statement.");
        if (statements.Values.Count == 0)
            errors.Add("statements.values is empty.");
        CheckKeys(statements.Values.Select(v => v.Value), "statements.values");
        CheckExternalIds(statements.Values.Select(v => (v.Value, v.ExternalIds)), "statements.values");
        CheckKeys(statements.Categories.Select(c => c.Value), "statements.categories");

        var categories = statements.Categories.Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
        var entries = (statements.Iiif?.Entries ?? []).Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var statement in statements.Values)
        {
            var name = $"statement '{statement.Value}'";
            if (statement.Category == null && statements.Derived.Count > 0)
                errors.Add($"{name} has no category, so no derived value could be computed for it.");
            if (statement.Category != null && !categories.Contains(statement.Category))
                errors.Add($"{name}: category '{statement.Category}' is not one of statements.categories.");
            if (statement.Iiif != null && !entries.Contains(statement.Iiif))
                errors.Add($"{name}: iiif '{statement.Iiif}' is not one of statements.iiif.entries.");
        }
    }

    private void ValidateIiif(StatementProfile statements)
    {
        var iiif = statements.Iiif;
        if (iiif == null) return;
        if (string.IsNullOrWhiteSpace(iiif.RequiredStatementLabel))
            errors.Add("statements.iiif.requiredStatementLabel is blank.");
        CheckKeys(iiif.Entries.Select(e => e.Id), "statements.iiif.entries");
        foreach (var entry in iiif.Entries)
        {
            var name = $"iiif entry '{entry.Id}'";
            if (entry.Rights == null && entry.RequiredStatement == null)
                errors.Add($"{name} has neither rights nor requiredStatement.");
            // It is written into METS as the standard use and reproduction element, so it must be
            // one of the long-lived statements the platform knows.
            if (entry.Rights != null && !IsKnownRightsStatement(entry.Rights))
                errors.Add($"{name}: rights '{entry.Rights}' is not one of the platform's rights statements (RightsStatement).");
            if (entry.RequiredStatement != null && string.IsNullOrWhiteSpace(entry.RequiredStatement))
                errors.Add($"{name}: requiredStatement is blank (omit it instead).");
            if (entry.RequiredStatementLabel != null && string.IsNullOrWhiteSpace(entry.RequiredStatementLabel))
                errors.Add($"{name}: requiredStatementLabel is blank (omit it to use statements.iiif.requiredStatementLabel).");
        }
    }

    private void ValidateDerived(StatementProfile statements, DerivedAccessCondition derived)
    {
        var name = $"derived '{derived.Type}'";
        if (string.IsNullOrWhiteSpace(derived.Type))
            errors.Add("A derived access condition has no type.");
        else if (IsStandardType(derived.Type) || derived.Type == statements.Type)
            errors.Add($"{name}: the type is reserved for a chosen (not derived) access condition.");
        if (derived.Values.Count == 0)
            errors.Add($"{name} has no values.");
        CheckKeys(derived.Values.Select(v => v.Value), $"{name} values");
        CheckExternalIds(derived.Values.Select(v => (v.Value, v.ExternalIds)), $"{name} values");
        if (derived.Matrix.Count == 0)
        {
            errors.Add($"{name} has an empty matrix.");
            return;
        }

        var rows = profile.RestrictionAxis.Values.Select(v => v.Value).Append(NoneKey).ToList();
        var columns = statements.Categories.Select(c => c.Value).Append(NoneKey).ToList();
        CheckMatrixReferences(derived, name, rows, columns);

        // Every restriction (and none) must resolve for every category (and no statement): a gap
        // would silently leave a node with no derived value.
        foreach (var row in rows)
        {
            foreach (var column in columns.Where(column => derived.Lookup(row, column) == null))
            {
                errors.Add($"{name}: no value for restriction '{row}' with category '{column}' (add the cell, or a '{Wildcard}' row or column).");
            }
        }
    }

    private void CheckMatrixReferences(DerivedAccessCondition derived, string name, List<string> rows, List<string> columns)
    {
        var validValues = derived.Values.Select(v => v.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var (row, cells) in derived.Matrix)
        {
            if (row != Wildcard && !rows.Contains(row))
                errors.Add($"{name}: matrix row '{row}' is not a restriction value, '{NoneKey}' or '{Wildcard}'.");
            foreach (var (column, value) in cells)
            {
                if (column != Wildcard && !columns.Contains(column))
                    errors.Add($"{name}: matrix column '{column}' in row '{row}' is not a category, '{NoneKey}' or '{Wildcard}'.");
                if (!validValues.Contains(value))
                    errors.Add($"{name}: matrix cell [{row}, {column}] = '{value}' is not one of its values.");
            }
        }
    }

    // ---- Shared checks -------------------------------------------------------------------------

    /// <summary>
    /// Keys must be unique and non-blank - the forms post "" for "(inherit from parent)", and a blank
    /// value would be written as an empty <c>mods:accessCondition</c>, which reads back as absent -
    /// and may not be a reserved matrix key.
    /// </summary>
    private void CheckKeys(IEnumerable<string?> keys, string what)
    {
        var list = keys.ToList();
        if (list.Any(string.IsNullOrWhiteSpace))
            errors.Add($"{what} contains a blank value; a blank means \"(inherit from parent)\".");
        if (list.Any(k => k is NoneKey or Wildcard))
            errors.Add($"{what} may not use the reserved keys '{NoneKey}' or '{Wildcard}'.");
        CheckDuplicates(list.OfType<string>(), what);
    }

    private void CheckDuplicates(IEnumerable<string> keys, string what)
    {
        foreach (var duplicate in keys.GroupBy(k => k, StringComparer.Ordinal).Where(g => g.Count() > 1))
            errors.Add($"{what} contains '{duplicate.Key}' more than once.");
    }

    /// <summary>
    /// Within one axis, an external identifier may belong to only one value, so that a system
    /// holding it (EMu, through the Goobi flow) maps to exactly one key.
    /// </summary>
    private void CheckExternalIds(IEnumerable<(string Value, Dictionary<string, List<string>>? Ids)> values, string what)
    {
        var owners = new Dictionary<(string Scheme, string Id), string>();
        var all = values
            .Where(v => v.Ids != null)
            .SelectMany(v => v.Ids!.SelectMany(scheme => scheme.Value.Select(id => (v.Value, Scheme: scheme.Key, Id: id))));
        foreach (var (value, scheme, id) in all)
        {
            if (string.IsNullOrWhiteSpace(scheme) || string.IsNullOrWhiteSpace(id))
            {
                errors.Add($"{what} '{value}' has a blank externalIds scheme or id.");
            }
            else if (owners.TryGetValue((scheme, id), out var owner) && owner != value)
            {
                errors.Add($"{what}: {scheme} id '{id}' belongs to both '{owner}' and '{value}'.");
            }
            else
            {
                owners[(scheme, id)] = value;
            }
        }
    }

    private static bool IsStandardType(string type) =>
        type is AccessConditionProfile.ReservedTypes.RestrictionOnAccess or AccessConditionProfile.ReservedTypes.UseAndReproduction;

    private static bool IsKnownRightsStatement(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && RightsStatement.ByUri.ContainsKey(uri);
}
