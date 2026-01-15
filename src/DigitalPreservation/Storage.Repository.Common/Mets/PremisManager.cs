using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Utils;
using DigitalPreservation.XmlGen.Premis.V3;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
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
    
    public static FileFormatMetadata? Read(PremisComplexType premis)
    {
        var file = premis.Object.FirstOrDefault(po => po is File);
        return file == null ? null : Read((File)file);
    }
    
    public static FileFormatMetadata Read(File file)
    {
        var premisFile = new FileFormatMetadata{ Source = "METS" };
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
        
        var storage = file.Storage?.SingleOrDefault(
            s => s.StorageMedium.FirstOrDefault(sm => sm.Value == IMetsManager.MetsCreatorAgent) != null);
        if (storage != null)
        {
            premisFile.StorageLocation = new Uri(storage.ContentLocation.ContentLocationValue);
        }
        return premisFile;

    }
    
    public static PremisComplexType Create(FileFormatMetadata premisFile, ExifMetadata? exifMetadata = null)
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

        if (exifMetadata != null)
        {
            var document = new XmlDocument();
            var parentElement = GetXmlElement(new ExifTag{ TagName = "ExifMetadata" , TagValue = string.Empty}, document);

            if (parentElement != null)
            {
                if (exifMetadata is { Tags: not null })
                {
                    foreach (var fileExifMetadata in exifMetadata.Tags)
                    {
                        var element = GetXmlElement(fileExifMetadata, document);
                        if (element != null) parentElement.AppendChild(element);

                        if (fileExifMetadata.TagName != null && fileExifMetadata.TagName.ToLower().Trim().Replace(" ", string.Empty) == "imageheight")
                        {
                            AddSignificantProperty(file, "ImageHeight", fileExifMetadata.TagValue ?? string.Empty);
                        }

                        if (fileExifMetadata.TagName != null && fileExifMetadata.TagName.ToLower().Trim().Replace(" ", string.Empty) == "imagewidth")
                        {
                            AddSignificantProperty(file, "ImageWidth", fileExifMetadata.TagValue ?? string.Empty);
                        }


                        if (fileExifMetadata.TagName != null && fileExifMetadata.TagName.ToLower().Trim().Replace(" ", string.Empty) == "duration")
                        {
                            AddSignificantProperty(file, "Duration", fileExifMetadata.TagValue ?? string.Empty);
                        }

                        if (fileExifMetadata.TagName != null && fileExifMetadata.TagName.ToLower().Trim().Replace(" ", string.Empty) == "avgbitrate")
                        {
                            AddSignificantProperty(file, "Bitrate", fileExifMetadata.TagValue ?? string.Empty);
                        }
                    }
                }

                var parentExtension = new ObjectCharacteristicsExtension();
                parentExtension.Any.Add(parentElement);

                objectCharacteristics.ObjectCharacteristicsExtension.Add(parentExtension);
            }

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

        if (premisFile.StorageLocation != null)
        {
            var storageComplexType = new StorageComplexType();
            storageComplexType.StorageMedium.Add(new StorageMedium { Value = IMetsManager.MetsCreatorAgent });
            storageComplexType.ContentLocation = new ContentLocationComplexType
            {
                ContentLocationType = new ContentLocationType { Value = "uri" },
                ContentLocationValue = premisFile.StorageLocation.OriginalString
            };
            file.Storage.Add(storageComplexType);
        }

        return premis;
    }

    public static void Patch(PremisComplexType premis, FileFormatMetadata premisFile, ExifMetadata? exifMetadata = null)
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
            var fixity =
                objectCharacteristics.Fixity.FirstOrDefault(f =>
                    f.MessageDigestAlgorithm.Value?.ToUpperInvariant() == Sha256);
            if (fixity == null)
            {
                fixity = new FixityComplexType
                {
                    MessageDigestAlgorithm = new MessageDigestAlgorithm { Value = Sha256 }
                };
                objectCharacteristics.Fixity.Add(fixity);
            }

            fixity.MessageDigest = premisFile.Digest;
        }

        if (exifMetadata != null)
        {
            var document = new XmlDocument();
            var parentElement = GetXmlElement(new ExifTag { TagName = "ExifMetadata", TagValue = string.Empty }, document);
            
            foreach (var extensionComplexType in objectCharacteristics.ObjectCharacteristicsExtension.ToList())
            {
                objectCharacteristics.ObjectCharacteristicsExtension.Remove(extensionComplexType);
            }

            foreach (var significantPropertiesComplexType in file.SignificantProperties.ToList())
            {
                file.SignificantProperties.Remove(significantPropertiesComplexType);
            }

            if (parentElement != null)
            {
                if (exifMetadata is { Tags: not null })
                {
                    foreach (var fileExifMetadata in exifMetadata.Tags)
                    {
                        var element = GetXmlElement(fileExifMetadata, document);
                        if (element != null) parentElement.AppendChild(element);

                        if (fileExifMetadata.TagName != null && fileExifMetadata.TagName.ToLower().Trim().Replace(" ", string.Empty) == "imageheight")
                        {
                            AddSignificantProperty(file, "ImageHeight", fileExifMetadata.TagValue ?? string.Empty);
                        }

                        if (fileExifMetadata.TagName != null && fileExifMetadata.TagName.ToLower().Trim().Replace(" ", string.Empty) == "imagewidth")
                        {
                            AddSignificantProperty(file, "ImageWidth", fileExifMetadata.TagValue ?? string.Empty);
                        }


                        if (fileExifMetadata.TagName != null && fileExifMetadata.TagName.ToLower().Trim().Replace(" ", string.Empty) == "duration")
                        {
                            AddSignificantProperty(file, "Duration", fileExifMetadata.TagValue ?? string.Empty);
                        }

                        if (fileExifMetadata.TagName != null && fileExifMetadata.TagName.ToLower().Trim().Replace(" ", string.Empty) == "avgbitrate")
                        {
                            AddSignificantProperty(file, "Bitrate", fileExifMetadata.TagValue ?? string.Empty);
                        }
                    }
                }

                var parentExtension = new ObjectCharacteristicsExtension();
                parentExtension.Any.Add(parentElement);

                objectCharacteristics.ObjectCharacteristicsExtension.Add(parentExtension);
            }

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

        if (premisFile.StorageLocation != null)
        {
            var contentLocation = EnsureContentLocation(file);
            contentLocation.ContentLocationType = new ContentLocationType { Value = "uri" };
            contentLocation.ContentLocationValue = premisFile.StorageLocation.OriginalString;
        }
    }

    private static void AddSignificantProperty(File file, string propertyName, string metadataValue)
    {
        var significantProperties = new SignificantPropertiesComplexType();
        file.SignificantProperties.Add(significantProperties);

        //TODO: add value to significant properties
        significantProperties.SignificantPropertiesType = new StringPlusAuthority
        {
            Value = propertyName
        };

        significantProperties.SignificantPropertiesValue.Add(metadataValue);
    }

    private static ContentLocationComplexType EnsureContentLocation(File file)
    {
        var thisStorage = file.Storage.FirstOrDefault(
            s => s.StorageMedium.FirstOrDefault(sm => sm.Value == IMetsManager.MetsCreatorAgent) != null);
        if (thisStorage == null)
        {
            thisStorage = new StorageComplexType();
            thisStorage.StorageMedium.Add(new StorageMedium { Value = IMetsManager.MetsCreatorAgent });
            file.Storage.Add(thisStorage);
        }

        var contentLocation = thisStorage.ContentLocation;
        if (contentLocation == null)
        {
            contentLocation = new ContentLocationComplexType();
            thisStorage.ContentLocation = contentLocation;
        }
        return contentLocation;
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

    public static XmlElement? GetXmlElement(ExifTag exifMetdata, XmlDocument document)
    {
        try
        {
            var rgx = new Regex("[^a-zA-Z0-9]");
            if (exifMetdata.TagName != null)
            {
                var element = document.CreateElement(rgx.Replace(exifMetdata.TagName, ""));
                element.InnerText = exifMetdata.TagValue ?? string.Empty;
                return element;
            }
        }
        catch (Exception)
        {
            return null;
        }
        return null;
    }
}

