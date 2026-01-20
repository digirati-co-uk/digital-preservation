using System.Text;
using System.Xml;
using System.Xml.Serialization;
using DigitalPreservation.Common.Model.Mets;

namespace Storage.Repository.Common.Mets.StorageImpl;

public static class StorageHelpers
{
    private static XmlSerializerNamespaces GetNamespaces()
    {
        var ns = new XmlSerializerNamespaces();
        ns.Add("mets", "http://www.loc.gov/METS/");
        ns.Add("mods", "http://www.loc.gov/mods/v3");
        ns.Add("premis", "http://www.loc.gov/premis/v3");
        ns.Add("xlink", "http://www.w3.org/1999/xlink");
        ns.Add("xsi", "http://www.w3.org/2001/XMLSchema-instance");
        return ns;
    }

    public static string XmlFromFullMets(FullMets fullMets)
    {
        var serializer = new XmlSerializer(typeof(DigitalPreservation.XmlGen.Mets.Mets));
        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, new XmlWriterSettings
               {
                   OmitXmlDeclaration = true,
                   NamespaceHandling = NamespaceHandling.OmitDuplicates,
               }))
        {
            serializer.Serialize(writer, fullMets.Mets, GetNamespaces());
        }

        return sb.ToString();
    }
    
    public static string? GetAgentName(DigitalPreservation.XmlGen.Mets.Mets? mets)
    {
        string? agentName = null;
        if (mets?.MetsHdr?.Agent is not null && mets.MetsHdr.Agent.Count > 0)
        {
            agentName = mets.MetsHdr.Agent[0].Name;
        }

        return agentName;
    }
}