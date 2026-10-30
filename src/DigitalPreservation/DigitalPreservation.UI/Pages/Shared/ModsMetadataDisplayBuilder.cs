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
        return GetDisplayItems(
            combinedBase.AccessRestrictions,
            combinedBase.EffectiveAccessRestrictions,
            combinedBase.RightsStatement,
            combinedBase.EffectiveRightsStatement,
            combinedBase.RightsStatementSuppressed,
            combinedBase.DerivedAccessConditions,
            combinedBase.EffectiveDerivedAccessConditions,
            combinedBase.RecordInfo,
            combinedBase.EffectiveRecordInfo,
            profile,
            logger);
    }

    public static List<(string Label, string Text, bool Inherited)> GetDisplayItems(
        ResourceBase? resourceBase, ILogger? logger = null, AccessConditionProfile? profile = null)
    {
        if (resourceBase == null)
        {
            return [];
        }
        return GetDisplayItems(
            resourceBase.AccessRestrictions,
            resourceBase.EffectiveAccessRestrictions,
            resourceBase.RightsStatement,
            resourceBase.EffectiveRightsStatement,
            resourceBase.RightsStatementSuppressed,
            resourceBase.DerivedAccessConditions,
            resourceBase.EffectiveDerivedAccessConditions,
            resourceBase.RecordInfo,
            resourceBase.EffectiveRecordInfo,
            profile,
            logger);
    }

    private static List<(string Label, string Text, bool Inherited)> GetDisplayItems(
        List<string>? accessRestrictions,
        List<string>? effectiveAccessRestrictions,
        Uri? rightsStatement,
        Uri? effectiveRightsStatement,
        bool rightsStatementSuppressed,
        Dictionary<string, string>? derivedAccessConditions,
        Dictionary<string, string>? effectiveDerivedAccessConditions,
        RecordInfo? recordInfo,
        RecordInfo? effectiveRecordInfo,
        AccessConditionProfile? profile,
        ILogger? logger)
    {
        // Collect display items as (label, text, inherited). Inherited items render in muted style.
        var items = new List<(string Label, string Text, bool Inherited)>();

        // An explicit value always overrides to become the effective value (MetsParser.
        // ComputeEffectiveMetadata), so a mismatch here means that invariant has been broken -
        // defensive, not reachable today. Showing the explicit value and logging a warning keeps a
        // future regression from turning a cosmetic inconsistency into a 500 for the whole page.
        if (accessRestrictions is { Count: > 0 })
        {
            if (effectiveAccessRestrictions != null && !effectiveAccessRestrictions.SequenceEqual(accessRestrictions))
            {
                logger?.LogWarning(
                    "Effective access restrictions ({Effective}) don't match explicit access restrictions ({Explicit}); showing the explicit value",
                    string.Join(", ", effectiveAccessRestrictions), string.Join(", ", accessRestrictions));
            }
            items.Add(("Access", string.Join(", ", accessRestrictions.Select(v => RestrictionLabel(profile, v))), false));
        }
        else if (effectiveAccessRestrictions is { Count: > 0 })
        {
            items.Add(("Access", string.Join(", ", effectiveAccessRestrictions.Select(v => RestrictionLabel(profile, v))), true));
        }

        if (rightsStatement != null)
        {
            if (effectiveRightsStatement != rightsStatement)
            {
                logger?.LogWarning(
                    "Effective rights statement ({Effective}) does not match explicit rights statement ({Explicit}); showing the explicit value",
                    effectiveRightsStatement, rightsStatement);
            }
            items.Add(("Rights", RightsStatement.GetShortLabel(rightsStatement) ?? "", false));
        }
        else if (rightsStatementSuppressed)
        {
            // Explicit-but-empty rights: deliberately asserts "no rights" and does not inherit.
            items.Add(("Rights", "none (not inherited)", false));
        }
        else if (effectiveRightsStatement != null)
        {
            items.Add(("Rights", RightsStatement.GetShortLabel(effectiveRightsStatement) ?? "", true));
        }

        // Derived conditions (issue #433). With a profile, only its derived types are shown, under
        // their configured labels; without one (the Browse page), every other typed condition the
        // parser found is shown under its raw type, which is still true and still useful.
        var types = profile != null
            ? profile.Derived.Select(d => d.Type)
            : (effectiveDerivedAccessConditions?.Keys ?? Enumerable.Empty<string>())
                .Union(derivedAccessConditions?.Keys ?? Enumerable.Empty<string>());
        foreach (var type in types)
        {
            var derived = profile?.Derived.FirstOrDefault(d => d.Type == type);
            var label = derived?.Label is { Length: > 0 } l ? l : type;
            if (derivedAccessConditions != null && derivedAccessConditions.TryGetValue(type, out var explicitValue))
            {
                items.Add((label, derived?.LabelFor(explicitValue) ?? explicitValue, false));
            }
            else if (effectiveDerivedAccessConditions != null && effectiveDerivedAccessConditions.TryGetValue(type, out var effectiveValue))
            {
                items.Add((label, derived?.LabelFor(effectiveValue) ?? effectiveValue, true));
            }
        }

        if (recordInfo != null)
        {
            if (!recordInfo.HasSameIdentifiers(effectiveRecordInfo))
            {
                logger?.LogWarning(
                    "Effective record identifiers ({Effective}) do not match explicit record identifiers ({Explicit}); showing the explicit value",
                    effectiveRecordInfo?.ToCompactString(", "), recordInfo.ToCompactString(", "));
            }
            items.Add(("Record", recordInfo.ToCompactString(", ") ?? "", false));
        }
        else if (effectiveRecordInfo != null)
        {
            items.Add(("Record", effectiveRecordInfo.ToCompactString(", ") ?? "", true));
        }

        return items;
    }

    /// <summary>The configured label for a restriction value, or the value itself when the profile has no such value (legacy or third-party content).</summary>
    private static string RestrictionLabel(AccessConditionProfile? profile, string value) =>
        profile?.RestrictionAxis.Values.FirstOrDefault(v => v.Value == value)?.Label ?? value;
}
