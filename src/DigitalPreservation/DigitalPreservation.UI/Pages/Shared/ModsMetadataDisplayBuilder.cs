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

    public static List<(string Label, string Text, bool Inherited)> GetDisplayItems(
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

    public static List<(string Label, string Text, bool Inherited)> GetDisplayItems(
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

    private static List<(string Label, string Text, bool Inherited)> GetDisplayItems(
        ModsDisplayInputs inputs, AccessConditionProfile? profile, ILogger? logger)
    {
        // Collect display items as (label, text, inherited). Inherited items render in muted style.
        var items = new List<(string Label, string Text, bool Inherited)>();
        AddAccess(items, inputs, profile, logger);
        AddCategory(items, inputs, profile);
        AddRights(items, inputs, logger);
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
        List<(string Label, string Text, bool Inherited)> items, ModsDisplayInputs inputs,
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
            items.Add(("Access", string.Join(", ", inputs.Access.Select(v => RestrictionLabel(profile, v))), false));
        }
        else if (inputs.EffectiveAccess is { Count: > 0 })
        {
            items.Add(("Access", string.Join(", ", inputs.EffectiveAccess.Select(v => RestrictionLabel(profile, v))), true));
        }
    }

    /// <summary>The chosen category (issue #433), shown under its configured label; only with a profile that has one.</summary>
    private static void AddCategory(
        List<(string Label, string Text, bool Inherited)> items, ModsDisplayInputs inputs, AccessConditionProfile? profile)
    {
        var axis = profile?.CategoryAxis;
        if (axis == null || string.IsNullOrEmpty(axis.Type)) return;
        string Text(string value) => axis.Values.FirstOrDefault(v => v.Value == value)?.Label ?? value;
        if (inputs.Other != null && inputs.Other.TryGetValue(axis.Type, out var explicitValue))
        {
            items.Add((axis.Label, Text(explicitValue), false));
        }
        else if (inputs.EffectiveOther != null && inputs.EffectiveOther.TryGetValue(axis.Type, out var effectiveValue))
        {
            items.Add((axis.Label, Text(effectiveValue), true));
        }
    }

    private static void AddRights(
        List<(string Label, string Text, bool Inherited)> items, ModsDisplayInputs inputs, ILogger? logger)
    {
        if (inputs.Rights != null)
        {
            if (inputs.EffectiveRights != inputs.Rights)
            {
                logger?.LogWarning(
                    "Effective rights statement ({Effective}) does not match explicit rights statement ({Explicit}); showing the explicit value",
                    inputs.EffectiveRights, inputs.Rights);
            }
            items.Add(("Rights", RightsStatement.GetShortLabel(inputs.Rights) ?? "", false));
        }
        else if (inputs.RightsSuppressed)
        {
            // Explicit-but-empty rights: deliberately asserts "no rights" and does not inherit.
            items.Add(("Rights", "none (not inherited)", false));
        }
        else if (inputs.EffectiveRights != null)
        {
            items.Add(("Rights", RightsStatement.GetShortLabel(inputs.EffectiveRights) ?? "", true));
        }
    }

    /// <summary>
    /// The other typed conditions (issue #433). With a profile, only its derived types are shown,
    /// under their configured labels (the category has its own item above); without one (the
    /// Browse page), every other typed condition the parser found is shown under its raw type,
    /// which is still true and still useful.
    /// </summary>
    private static void AddOtherTyped(
        List<(string Label, string Text, bool Inherited)> items, ModsDisplayInputs inputs, AccessConditionProfile? profile)
    {
        foreach (var type in TypesToShow(inputs, profile))
        {
            var derived = profile?.Derived.FirstOrDefault(d => d.Type == type);
            var label = derived?.Label is { Length: > 0 } l ? l : type;
            if (inputs.Other != null && inputs.Other.TryGetValue(type, out var explicitValue))
            {
                items.Add((label, derived?.LabelFor(explicitValue) ?? explicitValue, false));
            }
            else if (inputs.EffectiveOther != null && inputs.EffectiveOther.TryGetValue(type, out var effectiveValue))
            {
                items.Add((label, derived?.LabelFor(effectiveValue) ?? effectiveValue, true));
            }
        }
    }

    private static IEnumerable<string> TypesToShow(ModsDisplayInputs inputs, AccessConditionProfile? profile)
    {
        if (profile != null)
        {
            return profile.Derived.Select(d => d.Type);
        }
        return (inputs.EffectiveOther?.Keys ?? Enumerable.Empty<string>())
            .Union(inputs.Other?.Keys ?? Enumerable.Empty<string>());
    }

    private static void AddRecord(
        List<(string Label, string Text, bool Inherited)> items, ModsDisplayInputs inputs, ILogger? logger)
    {
        if (inputs.Record != null)
        {
            if (!inputs.Record.HasSameIdentifiers(inputs.EffectiveRecord))
            {
                logger?.LogWarning(
                    "Effective record identifiers ({Effective}) do not match explicit record identifiers ({Explicit}); showing the explicit value",
                    inputs.EffectiveRecord?.ToCompactString(", "), inputs.Record.ToCompactString(", "));
            }
            items.Add(("Record", inputs.Record.ToCompactString(", ") ?? "", false));
        }
        else if (inputs.EffectiveRecord != null)
        {
            items.Add(("Record", inputs.EffectiveRecord.ToCompactString(", ") ?? "", true));
        }
    }

    /// <summary>The configured label for a restriction value, or the value itself when the profile has no such value (legacy or third-party content).</summary>
    private static string RestrictionLabel(AccessConditionProfile? profile, string value) =>
        profile?.RestrictionAxis.Values.FirstOrDefault(v => v.Value == value)?.Label ?? value;
}
