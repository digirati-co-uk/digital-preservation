using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.XmlGen.Premis.V3;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;
using File = DigitalPreservation.XmlGen.Premis.V3.File;

namespace DigitalPreservation.Mets;


public class PremisManagerExif
{
    public ExifMetadata? Read(PremisComplexType premis)
    {
        throw new NotImplementedException();
    }

    public ExifMetadata Read(File file)
    {
        throw new NotImplementedException();
    }

    public PremisComplexType Create(ExifMetadata? exifMetadata)
    {
        var premis = new PremisComplexType();
        var file = new File();
        premis.Object.Add(file);
        var objectCharacteristics = new ObjectCharacteristicsComplexType();
        file.ObjectCharacteristics.Add(objectCharacteristics);

        if (exifMetadata == null) return premis;
        var document = new XmlDocument();
        var parentElement = GetXmlElement(new ExifTag { TagName = "ExifMetadata", TagValue = string.Empty }, document);

        if (parentElement == null) return premis;

        if (exifMetadata is { Tags: not null })
        {
            foreach (var fileExifMetadata in exifMetadata.Tags)
            {
                ProcessExifMetadataItem(file, fileExifMetadata, document, parentElement);
            }
        }

        var parentExtension = new ObjectCharacteristicsExtension();
        parentExtension.Any.Add(parentElement);

        objectCharacteristics.ObjectCharacteristicsExtension.Add(parentExtension);

        return premis;
    }

    public void Patch(PremisComplexType premis, ExifMetadata? exifMetadata)
    {
        // This is not just the same as Create because it shouldn't touch any fields existing
        // in the premis:file already, other than those supplied
        if (premis.Object.FirstOrDefault(po => po is File) is not File file)
        {
            file = new File();
            premis.Object.Add(file);
        }

        var objectCharacteristics = file.ObjectCharacteristics.FirstOrDefault();
        if (objectCharacteristics is null)
        {
            objectCharacteristics = new ObjectCharacteristicsComplexType();
            file.ObjectCharacteristics.Add(objectCharacteristics);
        }

        if (exifMetadata is null) return;
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

        if (parentElement is null) return;
        if (exifMetadata is { Tags: not null })
        {
            foreach (var fileExifMetadata in exifMetadata.Tags)
            {
                ProcessExifMetadataItem(file, fileExifMetadata, document, parentElement);
            }
        }

        var parentExtension = new ObjectCharacteristicsExtension();
        parentExtension.Any.Add(parentElement);

        objectCharacteristics.ObjectCharacteristicsExtension.Add(parentExtension);
    }

    private void ProcessExifMetadataItem(File? file, ExifTag? fileExifMetadata, XmlDocument document, XmlElement? parentElement)
    {
        if (fileExifMetadata is not null)
        {
            var element = GetXmlElement(fileExifMetadata, document);
            if (element is not null)
                parentElement?.AppendChild(element);
        }

        if (fileExifMetadata is not null && string.IsNullOrEmpty(fileExifMetadata.TagName)) return;
        if (fileExifMetadata is null) return;
        var property = fileExifMetadata.TagName?.ToLower().Trim().Replace(" ", string.Empty) switch
        {
            "imageheight" => "ImageHeight",
            "imagewidth" => "ImageWidth",
            "duration" => "Duration",
            "avgbitrate" => "Bitrate",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(property)) return;
        if (file is not null)
            AddSignificantProperty(file, property, fileExifMetadata.TagValue ?? string.Empty);
    }

    // Merges extent values into premis:significantProperties, checking for conflicts with any
    // properties already present (from Exif or any other source). Throws MetadataException if
    // a property has already been written with a different value.
    public void PatchExtent(PremisComplexType premis, ExtentMetadata extentMetadata)
    {
        if (premis.Object.FirstOrDefault(po => po is File) is not File file)
        {
            file = new File();
            premis.Object.Add(file);
        }

        if (extentMetadata.Duration.HasValue)
            PatchSignificantProperty(file, "Duration",
                extentMetadata.Duration.Value.ToString("G", CultureInfo.InvariantCulture));

        if (extentMetadata.PixelWidth.HasValue)
            PatchSignificantProperty(file, "ImageWidth",
                extentMetadata.PixelWidth.Value.ToString(CultureInfo.InvariantCulture));

        if (extentMetadata.PixelHeight.HasValue)
            PatchSignificantProperty(file, "ImageHeight",
                extentMetadata.PixelHeight.Value.ToString(CultureInfo.InvariantCulture));
    }

    // Adds a significantProperty if not already present; if it is already present (from any source),
    // verifies the value matches and throws if not.
    private void PatchSignificantProperty(File file, string propertyName, string value)
    {
        var existing = file.SignificantProperties
            .FirstOrDefault(sp => sp.SignificantPropertiesType?.Value == propertyName);

        if (existing != null)
        {
            var existingValue = existing.SignificantPropertiesValue.FirstOrDefault();
            if (existingValue != value)
                throw new MetadataException(
                    $"Conflicting values for significantProperties/{propertyName}: '{existingValue}' vs '{value}'");
            return;
        }

        AddSignificantProperty(file, propertyName, value);
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

