using System.Xml;
using System.Xml.Serialization;
using DigitalPreservation.XmlGen.Mets;
using DigitalPreservation.XmlGen.Mods.V3;

namespace Storage.Repository.Common.Mets;


public static class ModsManager
{
    private static readonly XmlSerializerNamespaces Namespaces;
    
    static ModsManager()
    {
        Namespaces = new XmlSerializerNamespaces();
        Namespaces.Add("mods", "http://www.loc.gov/mods/v3");
    }
    
    public static ModsDefinition Create(string name, string? language = null)
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
            if (type == null || accessCondition.Type == type)
            {
                result.Add(accessCondition.Title); // TODO: what is this? .Value is not there - what is the xml content?
            }
        }
        return result;
    }
    
    public static void RemoveAccessConditions(this ModsDefinition modsDefinition, string? type = null)
    {
        foreach (var accessCondition in modsDefinition.AccessCondition)
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
            Title = accessCondition, // TODO...
            Type = type
        };
        modsDefinition.AccessCondition.Add(accessConditionDefinition);
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

    public static ModsDefinition? GetRootMods(DigitalPreservation.XmlGen.Mets.Mets mets)
    {
        var rootDmd = mets.DmdSec.Single(x => x.Id == MetsManager.DmdPhysRoot)!;
        if (rootDmd.MdWrap is { Mdtype: MdSecTypeMdWrapMdtype.Mods })
        {
            var modsXml = rootDmd.MdWrap.XmlData.Any?.FirstOrDefault();
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
}