using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Combined;
using DigitalPreservation.Common.Model.Transit.Extensions;
using Microsoft.Extensions.Logging;

namespace DigitalPreservation.UI.Pages.Shared;

public static class ModsMetadataDisplayBuilder
{
    public static List<(string Label, string Text, bool Inherited)> GetDisplayItems(CombinedBase? combinedBase, ILogger? logger = null)
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
            combinedBase.RecordInfo,
            combinedBase.EffectiveRecordInfo,
            logger);
    }

    public static List<(string Label, string Text, bool Inherited)> GetDisplayItems(ResourceBase? resourceBase, ILogger? logger = null)
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
            resourceBase.RecordInfo,
            resourceBase.EffectiveRecordInfo,
            logger);
    }

    private static List<(string Label, string Text, bool Inherited)> GetDisplayItems(
        List<string>? accessRestrictions,
        List<string>? effectiveAccessRestrictions,
        Uri? rightsStatement,
        Uri? effectiveRightsStatement,
        bool rightsStatementSuppressed,
        RecordInfo? recordInfo,
        RecordInfo? effectiveRecordInfo,
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
            items.Add(("Access", string.Join(", ", accessRestrictions), false));
        }
        else if (effectiveAccessRestrictions is { Count: > 0 })
        {
            items.Add(("Access", string.Join(", ", effectiveAccessRestrictions), true));
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
}