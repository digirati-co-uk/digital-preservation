using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.AccessConditions;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Combined;
using DigitalPreservation.Common.Model.Transit.Extensions;
using Microsoft.Extensions.Logging;

namespace DigitalPreservation.UI.Pages.Shared;

public static class ModsMetadataDisplayBuilder
{
    /// <summary>
    /// ViewData key under which a page makes the installation's <see cref="AccessConditionProfile"/>
    /// available to the partials that render metadata items (issue #433).
    /// </summary>
    public const string ProfileViewDataKey = "AccessConditionProfile";

    public static List<ModsDisplayItem> GetDisplayItems(
        CombinedBase? combinedBase, ILogger? logger = null, AccessConditionProfile? profile = null)
    {
        if (combinedBase == null)
        {
            return [];
        }
        return GetDisplayItems(new ModsDisplayInputs(
            combinedBase.AccessRestrictions,
            combinedBase.EffectiveAccessRestrictions,
            combinedBase.RightsStatement,
            combinedBase.EffectiveRightsStatement,
            combinedBase.RightsStatementSuppressed,
            combinedBase.OtherAccessConditions,
            combinedBase.EffectiveOtherAccessConditions,
            combinedBase.RecordInfo,
            combinedBase.EffectiveRecordInfo), profile, logger);
    }

    public static List<ModsDisplayItem> GetDisplayItems(
        ResourceBase? resourceBase, ILogger? logger = null, AccessConditionProfile? profile = null)
    {
        if (resourceBase == null)
        {
            return [];
        }
        return GetDisplayItems(new ModsDisplayInputs(
            resourceBase.AccessRestrictions,
            resourceBase.EffectiveAccessRestrictions,
            resourceBase.RightsStatement,
            resourceBase.EffectiveRightsStatement,
            resourceBase.RightsStatementSuppressed,
            resourceBase.OtherAccessConditions,
            resourceBase.EffectiveOtherAccessConditions,
            resourceBase.RecordInfo,
            resourceBase.EffectiveRecordInfo), profile, logger);
    }

    /// <summary>The explicit and effective MODS values of one node, whichever transit type it came from.</summary>
    private sealed record ModsDisplayInputs(
        List<string>? Access,
        List<string>? EffectiveAccess,
        Uri? Rights,
        Uri? EffectiveRights,
        bool RightsSuppressed,
        Dictionary<string, string>? Other,
        Dictionary<string, string>? EffectiveOther,
        RecordInfo? Record,
        RecordInfo? EffectiveRecord);

    private static List<ModsDisplayItem> GetDisplayItems(
        ModsDisplayInputs inputs, AccessConditionProfile? profile, ILogger? logger)
    {
        // Collect display items as (label, text, inherited). Inherited items render in muted style.
        var items = new List<ModsDisplayItem>();
        AddAccess(items, inputs, profile, logger);
        AddStatement(items, inputs, profile?.Statements);
        AddRights(items, inputs, logger, profile?.Statements);
        AddOtherTyped(items, inputs, profile);
        AddRecord(items, inputs, logger);
        return items;
    }

    // An explicit value always overrides to become the effective value (MetsParser.
    // ComputeEffectiveMetadata), so a mismatch in any of the three sections below means that
    // invariant has been broken - defensive, not reachable today. Showing the explicit value and
    // logging a warning keeps a future regression from turning a cosmetic inconsistency into a 500
    // for the whole page.

    private static void AddAccess(
        List<ModsDisplayItem> items, ModsDisplayInputs inputs,
        AccessConditionProfile? profile, ILogger? logger)
    {
        if (inputs.Access is { Count: > 0 })
        {
            if (inputs.EffectiveAccess != null && !inputs.EffectiveAccess.SequenceEqual(inputs.Access))
            {
                logger?.LogWarning(
                    "Effective access restrictions ({Effective}) don't match explicit access restrictions ({Explicit}); showing the explicit value",
                    string.Join(", ", inputs.EffectiveAccess), string.Join(", ", inputs.Access));
            }
            items.Add(AccessItem(profile, inputs.Access, false));
        }
        else if (inputs.EffectiveAccess is { Count: > 0 })
        {
            items.Add(AccessItem(profile, inputs.EffectiveAccess, true));
        }
    }

    /// <summary>A row shows a value's short label where the profile gives one; the tooltip has the full one.</summary>
    private static ModsDisplayItem AccessItem(AccessConditionProfile? profile, List<string> values, bool inherited)
    {
        var known = values.Select(v => profile?.RestrictionAxis.Values.FirstOrDefault(r => r.Value == v)).ToList();
        var text = string.Join(", ", values.Select((v, i) => known[i]?.ShortLabel ?? known[i]?.Label ?? v));
        var full = string.Join(", ", values.Select((v, i) => known[i]?.Label ?? v));
        return new ModsDisplayItem("Access", text, inherited, full == text ? null : full);
    }

    /// <summary>
    /// The chosen statement (issue #433), shown by its short label, with its full label and what it
    /// publishes in IIIF as the item's detail (a tooltip). Only on an installation with statements.
    /// </summary>
    private static void AddStatement(List<ModsDisplayItem> items, ModsDisplayInputs inputs, StatementProfile? statements)
    {
        if (statements == null || string.IsNullOrEmpty(statements.Type)) return;
        var (value, inherited) = OwnOrInherited(inputs, statements.Type);
        if (value == null) return;
        var statement = statements.Find(value);
        var text = statement?.ShortLabel ?? statement?.Label ?? value;
        var detail = $"{statement?.Label ?? value}\n{IiifDetail(statements, value)}";
        items.Add(new ModsDisplayItem(statements.Label, text, inherited, detail));
    }

    /// <summary>What a node with this statement publishes in IIIF, as text.</summary>
    public static string IiifDetail(StatementProfile statements, string statement)
    {
        var entry = statements.IiifRightsFor(statement);
        if (entry == null) return "IIIF: nothing configured for this statement";
        var lines = new List<string>();
        if (entry.Rights != null)
        {
            var name = Uri.TryCreate(entry.Rights, UriKind.Absolute, out var uri) && RightsStatement.ByUri.TryGetValue(uri, out var rs)
                ? $" ({rs.Label})" : "";
            lines.Add($"IIIF rights: {entry.Rights}{name}");
        }
        if (entry.RequiredStatement != null)
        {
            var label = entry.RequiredStatementLabel ?? statements.Iiif?.RequiredStatementLabel;
            lines.Add($"IIIF requiredStatement - {label}: {entry.RequiredStatement}");
        }
        return string.Join("\n", lines);
    }

    private static void AddRights(
        List<ModsDisplayItem> items, ModsDisplayInputs inputs, ILogger? logger, StatementProfile? statements)
    {
        if (statements != null && RightsFollowStatement(inputs, statements)) return;
        if (inputs.Rights != null)
        {
            if (inputs.EffectiveRights != inputs.Rights)
            {
                logger?.LogWarning(
                    "Effective rights statement ({Effective}) does not match explicit rights statement ({Explicit}); showing the explicit value",
                    inputs.EffectiveRights, inputs.Rights);
            }
            items.Add(new ModsDisplayItem("Rights", RightsStatement.GetShortLabel(inputs.Rights) ?? "", false));
        }
        else if (inputs.RightsSuppressed)
        {
            // Explicit-but-empty rights: deliberately records "no rights statement chosen here" and does not inherit.
            items.Add(new ModsDisplayItem("Rights", "not chosen (not inherited)", false));
        }
        else if (inputs.EffectiveRights != null)
        {
            items.Add(new ModsDisplayItem("Rights", RightsStatement.GetShortLabel(inputs.EffectiveRights) ?? "", true));
        }
    }

    /// <summary>
    /// With statements (issue #433) a node's rights statement is written from its statement, so
    /// showing it as well would only lengthen the row. It is shown when it does not follow - a value
    /// left from before, or one set by another producer.
    /// </summary>
    private static bool RightsFollowStatement(ModsDisplayInputs inputs, StatementProfile statements)
    {
        var (statement, _) = OwnOrInherited(inputs, statements.Type);
        return statement != null && inputs.EffectiveRights == statements.RightsFor(statement);
    }

    /// <summary>
    /// The other typed conditions (issue #433). With a profile, only its derived types are shown,
    /// under their configured labels; without one (the Browse page), every other typed condition the
    /// parser found is shown under its raw type, which is still true and still useful.
    /// </summary>
    private static void AddOtherTyped(
        List<ModsDisplayItem> items, ModsDisplayInputs inputs, AccessConditionProfile? profile)
    {
        foreach (var type in TypesToShow(inputs, profile))
        {
            var (value, inherited) = OwnOrInherited(inputs, type);
            if (value == null) continue;
            var derived = profile?.Statements?.Derived.FirstOrDefault(d => d.Type == type);
            var label = derived?.Label is { Length: > 0 } l ? l : type;
            var text = derived?.ShortLabelFor(value) ?? value;
            var full = derived?.LabelFor(value);
            items.Add(new ModsDisplayItem(label, text, inherited, full == null || full == text ? null : full));
        }
    }

    private static IEnumerable<string> TypesToShow(ModsDisplayInputs inputs, AccessConditionProfile? profile)
    {
        if (profile != null)
        {
            return profile.Statements?.Derived.Select(d => d.Type) ?? [];
        }
        return (inputs.EffectiveOther?.Keys ?? Enumerable.Empty<string>())
            .Union(inputs.Other?.Keys ?? Enumerable.Empty<string>());
    }

    /// <summary>A typed condition's own value if the node has one, else the one it inherits.</summary>
    private static (string? Value, bool Inherited) OwnOrInherited(ModsDisplayInputs inputs, string type)
    {
        if (inputs.Other != null && inputs.Other.TryGetValue(type, out var own)) return (own, false);
        if (inputs.EffectiveOther != null && inputs.EffectiveOther.TryGetValue(type, out var inherited)) return (inherited, true);
        return (null, false);
    }

    private static void AddRecord(
        List<ModsDisplayItem> items, ModsDisplayInputs inputs, ILogger? logger)
    {
        if (inputs.Record != null)
        {
            if (!inputs.Record.HasSameIdentifiers(inputs.EffectiveRecord))
            {
                logger?.LogWarning(
                    "Effective record identifiers ({Effective}) do not match explicit record identifiers ({Explicit}); showing the explicit value",
                    inputs.EffectiveRecord?.ToCompactString(", "), inputs.Record.ToCompactString(", "));
            }
            items.Add(new ModsDisplayItem("Record", inputs.Record.ToCompactString(", ") ?? "", false));
        }
        else if (inputs.EffectiveRecord != null)
        {
            items.Add(new ModsDisplayItem("Record", inputs.EffectiveRecord.ToCompactString(", ") ?? "", true));
        }
    }

}

/// <summary>One metadata item shown beside a node; <see cref="Detail"/>, when present, is shown on hover.</summary>
public sealed record ModsDisplayItem(string Label, string Text, bool Inherited, string? Detail = null);
