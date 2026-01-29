using DigitalPreservation.Common.Model.DepositHelpers;
using DigitalPreservation.Common.Model.Mets;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Utils;
using DigitalPreservation.XmlGen.Premis.V3;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using File = DigitalPreservation.XmlGen.Premis.V3.File;

namespace Storage.Repository.Common.Mets;


public class PremisManagerExif : IPremisManager<ExifMetadata>
{
    public ExifMetadata? Read(PremisComplexType premis)
    {
        throw new NotImplementedException();
    }

    public ExifMetadata Read(File file)
    {
        throw new NotImplementedException();
    }

    public PremisComplexType Create(ExifMetadata? exifMetadata) //, ExifMetadata? exifMetadata = null
    {
        var premis = new PremisComplexType();
        var file = new File();
        premis.Object.Add(file);
        var objectCharacteristics = new ObjectCharacteristicsComplexType();
        file.ObjectCharacteristics.Add(objectCharacteristics);

        if (exifMetadata != null)
        {
            var document = new XmlDocument();
            var parentElement = GetXmlElement(new ExifTag { TagName = "ExifMetadata", TagValue = string.Empty }, document);

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

        return premis;
    }

    public void Patch(PremisComplexType premis, ExifMetadata? exifMetadata) //, ExifMetadata? exifMetadata = null
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
    }

    private void AddSignificantProperty(File file, string propertyName, string metadataValue)
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

    public string Serialise(PremisComplexType premis)
    {
        var serializer = new XmlSerializer(typeof(PremisComplexType));
        var sw = new StringWriter();
        serializer.Serialize(sw, premis, GetXmlSerializerNameSpaces());
        return sw.ToString();
    }

    public XmlElement? GetXmlElement(PremisComplexType premis, bool fileElement)
    {
        var serializer = new XmlSerializer(typeof(PremisComplexType));
        var doc = new XmlDocument();
        using (var xw = doc.CreateNavigator()!.AppendChild())
        {
            serializer.Serialize(xw, premis, GetXmlSerializerNameSpaces());
        }
        if (fileElement)
        {
            return doc.DocumentElement?.FirstChild as XmlElement;
        }
        return doc.DocumentElement;
    }

    public XmlElement? GetXmlElement(ExifTag exifMetdata, XmlDocument document)
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

    private XmlSerializerNamespaces GetXmlSerializerNameSpaces()
    {
        var namespaces = new XmlSerializerNamespaces();
        namespaces.Add("premis", "http://www.loc.gov/premis/v3");
        namespaces.Add("xsi", "http://www.w3.org/2001/XMLSchema-instance");

        return namespaces;
    }
}

