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
        file.ObjectCharacteristics.Add(new ObjectCharacteristicsComplexType());
        Patch(premis, exifMetadata);
        return premis;
    }

    public void Patch(PremisComplexType premis, ExifMetadata? exifMetadata)
    {
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

        // Only clear the exif XML extension blob — significant properties may have been set
        // by other sources (e.g. PatchExtent) and are merged via PatchSignificantProperty.
        foreach (var ext in objectCharacteristics.ObjectCharacteristicsExtension.ToList())
            objectCharacteristics.ObjectCharacteristicsExtension.Remove(ext);

        var document = new XmlDocument();
        var parentElement = GetXmlElement(new ExifTag { TagName = "ExifMetadata", TagValue = string.Empty }, document);
        if (parentElement is null) return;

        if (exifMetadata.Tags is not null)
        {
            foreach (var tag in exifMetadata.Tags)
                ProcessExifMetadataItem(document, parentElement, tag);
        }

        var parentExtension = new ObjectCharacteristicsExtension();
        parentExtension.Any.Add(parentElement);
        objectCharacteristics.ObjectCharacteristicsExtension.Add(parentExtension);

        if (exifMetadata.Tags is not null)
            SetSignificantPropertiesFromTags(file, exifMetadata.Tags);
    }

    private static void ProcessExifMetadataItem(XmlDocument document, XmlElement? parentElement, ExifTag? tag)
    {
        if (tag?.TagName is null or "") return;
        var element = GetXmlElement(tag, document);
        if (element is not null)
            parentElement?.AppendChild(element);
    }

    private void SetSignificantPropertiesFromTags(File file, List<ExifTag> tags)
    {
        // ImageSize is ExifTool's composite tag representing the final video frame dimensions.
        // It appears once and takes precedence over the per-track ImageWidth/ImageHeight tags,
        // which repeat for each MOV track and may carry misleading values (e.g. a timecode
        // track reporting 853x20 rather than the actual video frame dimensions 853x480).
        var imageSize = FirstTag(tags, "imagesize");
        if (imageSize?.TagValue is { } sizeVal)
        {
            var parts = sizeVal.Split('x');
            if (parts.Length == 2)
            {
                PatchSignificantProperty(file, "ImageWidth", parts[0]);
                PatchSignificantProperty(file, "ImageHeight", parts[1]);
            }
        }
        else
        {
            // SourceImageWidth/SourceImageHeight come from the video codec section and appear
            // once. Fall back to the first ImageWidth/ImageHeight if those aren't present.
            var w = FirstTag(tags, "sourceimagewidth")?.TagValue ?? FirstTag(tags, "imagewidth")?.TagValue;
            var h = FirstTag(tags, "sourceimageheight")?.TagValue ?? FirstTag(tags, "imageheight")?.TagValue;
            if (w is not null) PatchSignificantProperty(file, "ImageWidth", w);
            if (h is not null) PatchSignificantProperty(file, "ImageHeight", h);
        }

        if (FirstTag(tags, "duration")?.TagValue is { } duration)
            PatchSignificantProperty(file, "Duration", duration);

        if (FirstTag(tags, "avgbitrate")?.TagValue is { } bitrate)
            PatchSignificantProperty(file, "Bitrate", bitrate);
    }

    private static ExifTag? FirstTag(List<ExifTag> tags, string normalizedName) =>
        tags.FirstOrDefault(t => NormTag(t.TagName) == normalizedName);

    private static string NormTag(string? name) =>
        name?.ToLower().Trim().Replace(" ", string.Empty) ?? string.Empty;

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

    // Adds a significantProperty if not already present; if already present from any source,
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

    private static void AddSignificantProperty(File file, string propertyName, string metadataValue)
    {
        var significantProperties = new SignificantPropertiesComplexType();
        file.SignificantProperties.Add(significantProperties);

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

    private XmlSerializerNamespaces GetXmlSerializerNameSpaces()
    {
        var namespaces = new XmlSerializerNamespaces();
        namespaces.Add("premis", "http://www.loc.gov/premis/v3");
        namespaces.Add("xsi", "http://www.w3.org/2001/XMLSchema-instance");

        return namespaces;
    }
}
