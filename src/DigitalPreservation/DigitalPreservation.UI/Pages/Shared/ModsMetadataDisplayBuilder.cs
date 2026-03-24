using DigitalPreservation.Common.Model;
using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Combined;
using DigitalPreservation.Common.Model.Transit.Extensions;

namespace DigitalPreservation.UI.Pages.Shared;

public class ModsMetadataDisplayBuilder
{
    public static List<(string Label, string Text, bool Inherited)> GetDisplayItems(CombinedBase? combinedBase)
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
            combinedBase.RecordInfo,
            combinedBase.EffectiveRecordInfo);
    }
    
    public static List<(string Label, string Text, bool Inherited)> GetDisplayItems(ResourceBase? resourceBase)
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
            resourceBase.RecordInfo,
            resourceBase.EffectiveRecordInfo);
    }

    private static List<(string Label, string Text, bool Inherited)> GetDisplayItems(
        List<string>? accessRestrictions, 
        List<string>? effectiveAccessRestrictions, 
        Uri? rightsStatement, 
        Uri? effectiveRightsStatement, 
        RecordInfo? recordInfo, 
        RecordInfo? effectiveRecordInfo)
    {
        // Collect display items as (label, text, inherited). Inherited items render in muted style.
        var items = new List<(string Label, string Text, bool Inherited)>();

        if (accessRestrictions is { Count: > 0 })
        {
            if (effectiveAccessRestrictions != null && !effectiveAccessRestrictions.SequenceEqual(accessRestrictions))
                throw new NotSupportedException("Effective access restrictions don't equal explicit access restrictions");
            items.Add(("Access", string.Join(", ", accessRestrictions), false));
        }
        else if (effectiveAccessRestrictions is { Count: > 0 })
        {
            items.Add(("Access", string.Join(", ", effectiveAccessRestrictions), true));
        }

        if (rightsStatement != null)
        {
            if (effectiveRightsStatement != rightsStatement)
                throw new NotSupportedException("Effective rights statement does not equal explicit rights statement");
            items.Add(("Rights", RightsStatement.GetShortLabel(rightsStatement) ?? "", false));
        }
        else if (effectiveRightsStatement != null)
        {
            items.Add(("Rights", RightsStatement.GetShortLabel(effectiveRightsStatement) ?? "", true));
        }

        if (recordInfo != null)
        {
            if (!recordInfo.HasSameIdentifiers(effectiveRecordInfo))
                throw new NotSupportedException("Effective record identifiers do not equal explicit record identifiers");
            items.Add(("Record", recordInfo.ToCompactString(", ") ?? "", false));
        }
        else if (effectiveRecordInfo != null)
        {
            items.Add(("Record", effectiveRecordInfo.ToCompactString(", ") ?? "", true));
        }

        return items;
    }
}