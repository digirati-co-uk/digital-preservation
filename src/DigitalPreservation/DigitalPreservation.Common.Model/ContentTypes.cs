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

        if (applicationCount == 2)
        {
            // Do we just keep adding new scenarios here?
            if (distinctTypes.Contains("application/rtf") && distinctTypes.Contains("application/msword"))
            {
                return "application/rtf";
            }
        }

        return null;
    }

    private static List<string> GetDistinctTypes(WorkingFile fileInDeposit)
    {
        var contentTypesFromFileFormatMetadata = fileInDeposit
            .Metadata
            .OfType<FileFormatMetadata>()
            .Where(m => m.ContentType.HasText())
            .Select(m => m.ContentType!)
            .Distinct()
            .ToList();

        var distinctTypes = contentTypesFromFileFormatMetadata;
        if (fileInDeposit.ContentType.HasText())
        {
            distinctTypes = contentTypesFromFileFormatMetadata
                .Union([fileInDeposit.ContentType])
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