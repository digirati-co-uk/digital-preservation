using System.Xml;
using System.Xml.Serialization;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Utils;
using DigitalPreservation.XmlGen.Premis.V3;
using File = DigitalPreservation.XmlGen.Premis.V3.File;

namespace Storage.Repository.Common.Mets;

public static class PremisManager
{
    private static readonly XmlSerializerNamespaces Namespaces;
    public const string Pronom = "PRONOM";
    public const string Sha256 = "SHA256";
    
    static PremisManager()
    {
        Namespaces = new XmlSerializerNamespaces();
        Namespaces.Add("premis", "http://www.loc.gov/premis/v3");
        Namespaces.Add("xsi", "http://www.w3.org/2001/XMLSchema-instance");
    }
    
    public static PremisMetadata? Read(PremisComplexType premis)
    {
        var file = premis.Object.FirstOrDefault(po => po is File);
        return file == null ? null : Read((File)file);
    }
    
    public static PremisMetadata Read(File file)
    {
        var premisFile = new PremisMetadata{ Source = "METS" };
        var objectCharacteristics = file.ObjectCharacteristics.FirstOrDefault();
        
        var fixity = objectCharacteristics?.Fixity?.FirstOrDefault(f => f.MessageDigestAlgorithm.Value?.ToUpperInvariant() == Sha256);
        if (fixity != null)
        {
            premisFile.Digest = fixity.MessageDigest;
        }

        if (objectCharacteristics?.Size is > 0)
        {
            premisFile.Size = objectCharacteristics.Size;
        }
        var pronomFormat = objectCharacteristics?.Format.FirstOrDefault(
            f => f.FormatRegistry.FirstOrDefault(
                fr => fr.FormatRegistryName.Value == Pronom) != null);

        if (pronomFormat != null)
        {
            premisFile.FormatName = pronomFormat.FormatDesignation.FormatName.Value;
        }
        var registry = pronomFormat?.FormatRegistry.FirstOrDefault(fr => fr.FormatRegistryName.Value == Pronom);
        if (registry != null)
        {
            premisFile.PronomKey = registry.FormatRegistryKey.Value;
        }

        if (file.OriginalName?.Value != null)
        {
            premisFile.OriginalName = file.OriginalName.Value;
        }

        return premisFile;

    }
    
    public static PremisComplexType Create(PremisMetadata premisFile)
    {
        var premis = new PremisComplexType();
        var file = new File();
        premis.Object.Add(file);
        var objectCharacteristics = new ObjectCharacteristicsComplexType();
        file.ObjectCharacteristics.Add(objectCharacteristics);

        if (premisFile.Digest.HasText())
        {
            var fixity = new FixityComplexType
            {
                MessageDigestAlgorithm = new MessageDigestAlgorithm{ Value = Sha256 },
                MessageDigest = premisFile.Digest
            };
            objectCharacteristics.Fixity.Add(fixity);
        }

        if (premisFile.Size is > 0)
        {
            objectCharacteristics.Size = premisFile.Size;
        }

        if (premisFile.PronomKey.HasText())
        {
            var format = new FormatComplexType
            {
                FormatDesignation = new FormatDesignationComplexType
                {
                    FormatName = new FormatName { Value = premisFile.FormatName }
                }
            };
            var registry = new FormatRegistryComplexType
            {
                FormatRegistryName = new FormatRegistryName { Value = Pronom },
                FormatRegistryKey = new FormatRegistryKey { Value = premisFile.PronomKey }
            };
            format.FormatRegistry.Add(registry);
            objectCharacteristics.Format.Add(format);
        }

        if (premisFile.OriginalName.HasText())
        {
            file.OriginalName = new OriginalNameComplexType
            {
                Value = premisFile.OriginalName
            };
        }

        return premis;
    }

    public static void Patch(PremisComplexType premis, PremisMetadata premisFile)
    {
        // This is not just the same as Create because it shouldn't touch any fields existing
        // in the premis:file already, other than those supplied
        if (premis.Object.FirstOrDefault(po => po is File) is not File file)
        {
            file = new File();
            premis.Object.Add(file);
        }
        
        var objectCharacteristics = file.ObjectCharacteristics.FirstOrDefault();
        if (objectCharacteristics == null)
        {
            objectCharacteristics = new ObjectCharacteristicsComplexType();
            file.ObjectCharacteristics.Add(objectCharacteristics);
        }
        
        if (premisFile.Digest.HasText())
        {
            var fixity = objectCharacteristics.Fixity.FirstOrDefault(f => f.MessageDigestAlgorithm.Value?.ToUpperInvariant() == Sha256);
            if (fixity == null)
            {
                fixity = new FixityComplexType
                {
                    MessageDigestAlgorithm = new MessageDigestAlgorithm{ Value = Sha256 }
                };
                objectCharacteristics.Fixity.Add(fixity);
            }
            fixity.MessageDigest = premisFile.Digest;
        }

        if (premisFile.Size is > 0)
        {
            objectCharacteristics.Size = premisFile.Size;
        }

        if (premisFile.PronomKey.HasText())
        {
            var pronomFormat = EnsurePronomFormat(objectCharacteristics);
            var registry = pronomFormat.FormatRegistry.FirstOrDefault(fr => fr.FormatRegistryName.Value == Pronom);
            if (registry == null)
            {
                registry = new FormatRegistryComplexType
                {
                    FormatRegistryName = new FormatRegistryName { Value = Pronom }
                };
                pronomFormat.FormatRegistry.Add(registry);
            }
            registry.FormatRegistryKey = new FormatRegistryKey { Value = premisFile.PronomKey };
        }

        if (premisFile.FormatName.HasText())
        {
            var pronomFormat = EnsurePronomFormat(objectCharacteristics);
            pronomFormat.FormatDesignation = new FormatDesignationComplexType
            {
                FormatName = new FormatName { Value = premisFile.FormatName }
            };
        }

        if (premisFile.OriginalName.HasText())
        {
            file.OriginalName = new OriginalNameComplexType
            {
                Value = premisFile.OriginalName
            };
        }
    }

    private static FormatComplexType EnsurePronomFormat(ObjectCharacteristicsComplexType objectCharacteristics)
    {
        var pronomFormat = objectCharacteristics.Format.FirstOrDefault(
            f => f.FormatRegistry.FirstOrDefault(
                fr => fr.FormatRegistryName.Value == Pronom) != null);
        if (pronomFormat == null)
        {
            pronomFormat = new FormatComplexType();
            objectCharacteristics.Format.Add(pronomFormat);
        }

        return pronomFormat;
    }

    public static string Serialise(PremisComplexType premis)
    {
        var serializer = new XmlSerializer(typeof(PremisComplexType));
        var sw = new StringWriter();
        serializer.Serialize(sw, premis, Namespaces);
        return sw.ToString();
    }

    public static XmlElement? GetXmlElement(PremisComplexType premis, bool fileElement)
    {
        var serializer = new XmlSerializer(typeof(PremisComplexType));
        var doc = new XmlDocument();
        using (var xw = doc.CreateNavigator()!.AppendChild()) {
            serializer.Serialize(xw,  premis, Namespaces);
        }
        if (fileElement)
        {
            return doc.DocumentElement?.FirstChild as XmlElement;
        }
        return doc.DocumentElement;
    }
}

