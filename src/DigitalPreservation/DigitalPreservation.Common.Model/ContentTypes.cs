using DigitalPreservation.Common.Model.Transit;
using DigitalPreservation.Common.Model.Transit.Extensions.Metadata;
using DigitalPreservation.Utils;

namespace DigitalPreservation.Common.Model;

public static class ContentTypes
{
    /// <summary>
    /// This is never served over the web, it's an internal marker
    /// </summary>
    public const string NotIdentified = "dlip/not-identified";

    /// <summary>
    /// Different names for the same format, mapped to the one we prefer. Browsers and S3 often give
    /// the unregistered x- form (e.g., audio/x-m4a for an .m4a upload) where Siegfried gives the
    /// registered one (audio/mp4); without this they look like a disagreement nobody can resolve.
    /// </summary>
    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["audio/x-m4a"] = "audio/mp4",
        ["audio/m4a"] = "audio/mp4",
        ["audio/x-wav"] = "audio/wav",
        ["audio/wave"] = "audio/wav",
        ["audio/vnd.wave"] = "audio/wav",
        ["audio/mp3"] = "audio/mpeg",
        ["audio/x-mp3"] = "audio/mpeg",
        ["audio/x-mpeg"] = "audio/mpeg",
        ["image/jpg"] = "image/jpeg",
        ["image/pjpeg"] = "image/jpeg",
        ["application/x-pdf"] = "application/pdf"
    };

    /// <summary>
    /// The preferred name for this content type; anything not known to have a synonym comes back unchanged.
    /// </summary>
    public static string? Normalise(string? contentType)
    {
        if (contentType is null)
        {
            return null;
        }
        return Synonyms.GetValueOrDefault(contentType, contentType);
    }

    public static List<string> GetAllContentTypes(WorkingFile? fileInDeposit)
    {
        return fileInDeposit is null ? [] : GetDistinctTypes(fileInDeposit);
    }

    public static string? GetBestContentType(WorkingFile? fileInDeposit)
    {
        if (fileInDeposit is null)
        {
            return null;
        }
        var distinctTypes = GetDistinctTypes(fileInDeposit);
        if (distinctTypes.Count == 1)
        {
            return distinctTypes[0];
        }
        RemoveExtraGenericContentTypes(distinctTypes);
        if (distinctTypes.Count == 1)
        {
            return distinctTypes[0];
        }
        
        // This is our jumping off point for more detailed investigation, using the tool outputs 
        // of EXIF and maybe FFProbe in future to determine what the file is.
        var contentTypeFromAdHocRules = GetContentTypeFromSpecificRules(distinctTypes);
        return contentTypeFromAdHocRules;
    }

    private static string? GetContentTypeFromSpecificRules(List<string> distinctTypes)
    {
        var applicationCount = distinctTypes.Count(ct => ct.StartsWith("application/"));

        if (applicationCount == 1)
        {
            var imageCount = distinctTypes.Count(ct => ct.StartsWith("image/"));
            if (imageCount == 1)
            {
                return distinctTypes.Single(ct => ct.StartsWith("image/"));
            }
            
            var videoCount = distinctTypes.Count(ct => ct.StartsWith("video/"));
            if (videoCount == 1)
            {
                return distinctTypes.Single(ct => ct.StartsWith("video/"));
            }
            
            var audioCount = distinctTypes.Count(ct => ct.StartsWith("audio/"));
            if (audioCount == 1)
            {
                return distinctTypes.Single(ct => ct.StartsWith("audio/"));
            }
            
            if (distinctTypes.Count == 2)
            {
                var textCount = distinctTypes.Count(ct => ct.StartsWith("text/"));
                if (textCount == 1)
                {
                    var textForm = distinctTypes.Single(ct => ct.StartsWith("text/"));
                    if (textForm == "text/rtf")
                    {
                        return "text/rtf";
                    }
                    if (textForm == "text/xml")
                    {
                        return "application/xml";
                    }
                }
            }
        }

        // Do we just keep adding new scenarios here?
        if (applicationCount == 2 && distinctTypes.Contains("application/rtf") && distinctTypes.Contains("application/msword"))
        {
            return "application/rtf";
        }

        return null;
    }

    private static List<string> GetDistinctTypes(WorkingFile fileInDeposit)
    {
        var contentTypesFromFileFormatMetadata = fileInDeposit
            .Metadata
            .OfType<FileFormatMetadata>()
            .Where(m => m.ContentType.HasText())
            .Select(m => Normalise(m.ContentType)!)
            .Distinct()
            .ToList();

        var distinctTypes = contentTypesFromFileFormatMetadata;
        if (fileInDeposit.ContentType.HasText())
        {
            distinctTypes = contentTypesFromFileFormatMetadata
                .Union([Normalise(fileInDeposit.ContentType)!])
                .Distinct()
                .ToList();
        }

        return distinctTypes;
    }

    /// <summary>
    /// Will only remove these generic types if one or more other types are present
    /// </summary>
    /// <param name="distinctTypes"></param>
    public static void RemoveExtraGenericContentTypes(List<string> distinctTypes)
    {
        if (distinctTypes.Count > 1)
        {
            // It might really be application/octet-stream, which is OK if that's the best we can do
            distinctTypes.RemoveAll(ct => ct == "application/octet-stream");
        }

        if (distinctTypes.Count > 1)
        {
            // It might really be binary/octet-stream, which is OK if that's the best we can do
            distinctTypes.RemoveAll(ct => ct == "binary/octet-stream");
        }

        if (distinctTypes.Count > 1)
        {
            distinctTypes.RemoveAll(ct => ct == "application/x-www-form-urlencoded");
        }
    }
}