using System.Xml;
using System.Xml.Serialization;
using DigitalPreservation.Common.Model.Transit.Extensions;
using DigitalPreservation.Utils;
using DigitalPreservation.XmlGen.Mets;
using DigitalPreservation.XmlGen.Mods.V3;

namespace DigitalPreservation.Mets;


public static class ModsManager
{
    private static readonly XmlSerializerNamespaces Namespaces;
    
    static ModsManager()
    {
        Namespaces = new XmlSerializerNamespaces();
        Namespaces.Add("mods", "http://www.loc.gov/mods/v3");
        Namespaces.Add("xlink", "http://www.w3.org/1999/xlink");
    }
    
    public static ModsDefinition CreateRootMods(string name, string? language = null)
    {
        var modsDefinition = new ModsDefinition();
        var titleInfoDefinition = new TitleInfoDefinition();
        titleInfoDefinition.Title.Add(new StringPlusLanguage{ Value = name, Lang = language });
        modsDefinition.TitleInfo.Add(titleInfoDefinition);
        return modsDefinition;
    }

    
    public static List<string> GetAccessConditions(this ModsDefinition modsDefinition, string? type = null)
    {
        var result = new List<string>();
        foreach (var accessCondition in modsDefinition.AccessCondition)
        {
            if (type == null || accessCondition.Type == type && accessCondition.Text.Length > 0)
            {
                result.Add(accessCondition.Text[0]);
            }
        }
        return result;
    }
    
    public static void RemoveAccessConditions(this ModsDefinition modsDefinition, string? type = null)
    {
        foreach (var accessCondition in modsDefinition.AccessCondition.ToList())
        {
            if (type == null || accessCondition.Type == type)
            {
                modsDefinition.AccessCondition.Remove(accessCondition);
            }
        }
    }
    
    
    public static void AddAccessCondition(this ModsDefinition modsDefinition, string accessCondition, string type)
    {
        var accessConditionDefinition = new AccessConditionDefinition
        {
            Text = [accessCondition],
            Type = type
        };
        modsDefinition.AccessCondition.Add(accessConditionDefinition);
    }


    public static RecordInfo? GetRecordInfo(this ModsDefinition modsDefinition)
    {
        RecordInfo? recordInfo = null;
        // This will handle multiple RecordInfo elements, if we ever find any
        foreach (var recordInfoDefinition in modsDefinition.RecordInfo)
        {
            foreach (var recordIdentifierDefinition in recordInfoDefinition.RecordIdentifier)
            {
                recordInfo ??= new RecordInfo();
                recordInfo.RecordIdentifiers.Add(new RecordIdentifier
                {
                    Source =  recordIdentifierDefinition.Source,
                    Value = recordIdentifierDefinition.Value
                });
            }
        }
        return recordInfo;
    }
    
    
    public static void SetTitle(this ModsDefinition modsDefinition, string name, string? language = null)
    {
        modsDefinition.TitleInfo.Clear();
        var titleInfo = new TitleInfoDefinition();
        titleInfo.Title.Add(new StringPlusLanguage { Value = name, Lang = language });
        modsDefinition.TitleInfo.Add(titleInfo);
    }

    public static void SetRecordInfo(this ModsDefinition modsDefinition, RecordInfo recordInfo)
    {
        modsDefinition.RecordInfo.Clear();
        foreach (var recordIdentifier in recordInfo.RecordIdentifiers)
        {
            if (modsDefinition.RecordInfo.Count == 0)
            {
                modsDefinition.RecordInfo.Add(new RecordInfoDefinition());
            }
            modsDefinition.RecordInfo[0].RecordIdentifier.Add(new RecordIdentifierDefinition()
            {
                Source =  recordIdentifier.Source,
                Value = recordIdentifier.Value
            });
        }
    }
    
    public static XmlElement? GetXmlElement(ModsDefinition modsDefinition)
    {
        var serializer = new XmlSerializer(typeof(ModsDefinition));
        var doc = new XmlDocument();
        using (var xw = doc.CreateNavigator()!.AppendChild()) {
            serializer.Serialize(xw, modsDefinition, Namespaces);
        }
        return doc.DocumentElement;
    }

    public static ModsDefinition? GetModsForDmdId(DigitalPreservation.XmlGen.Mets.Mets mets, string dmdId, bool createDmd = false)
    {
        var dmd = mets.DmdSec.SingleOrDefault(x => x.Id == dmdId);
        if (dmd == null && createDmd)
        {
            dmd = new MdSecType
            {
                Id = dmdId,
                MdWrap = MakeDmdMdWrapForMods(new ModsDefinition())
            };
            mets.DmdSec.Add(dmd);
        }

        if (dmd == null)
        {
            return null;
        }
        
        if (dmd.MdWrap is { Mdtype: MdSecTypeMdWrapMdtype.Mods })
        {
            var modsXml = dmd.MdWrap.XmlData.Any?.FirstOrDefault();
            if (modsXml != null)
            {
                var serializer = new XmlSerializer(typeof(ModsDefinition));
                using var xmlNodeReader = new XmlNodeReader(modsXml);
                var des = serializer.Deserialize(xmlNodeReader);
                return des as ModsDefinition;
            }
        }

        return null;
    }

    public static ModsDefinition? GetModsForDiv(DigitalPreservation.XmlGen.Mets.Mets mets, DivType div, bool createDmd = false)
    {
        if (div.Dmdid.Count == 0)
        {
            // There is no DMDID on this div
            if (createDmd)
            {
                if (div.Id.StartsWith(Constants.PhysIdPrefix))
                {
                    div.Dmdid.Add(Constants.DmdIdPrefix + div.Id.RemoveStart(Constants.PhysIdPrefix));
                }
                else
                {
                    // A logical structMap div that might have been supplied by the client
                    div.Dmdid.Add(Constants.DmdIdPrefix + div.Id);
                }
            }
        }
        var normalised = string.Join(' ', div.Dmdid);
        return GetModsForDmdId(mets, normalised, createDmd);
    }

    
    public static void SetModsForDiv(DigitalPreservation.XmlGen.Mets.Mets mets, DivType div, ModsDefinition mods)
    {
        var normalised = string.Join(' ', div.Dmdid);
        SetModsForDmdId(mets, normalised, mods);
    }

    private static void SetModsForDmdId(DigitalPreservation.XmlGen.Mets.Mets mets, string dmdId, ModsDefinition mods)
    {
        var dmd = mets.DmdSec.Single(x => x.Id == dmdId)!;
        dmd.MdWrap = MakeDmdMdWrapForMods(mods);
    }

    private static MdSecTypeMdWrap MakeDmdMdWrapForMods(ModsDefinition mods)
    {
        return new MdSecTypeMdWrap
        {
            Mdtype = MdSecTypeMdWrapMdtype.Mods,
            XmlData = new MdSecTypeMdWrapXmlData
            {
                Any = { GetXmlElement(mods) }
            }
        };
    }
}