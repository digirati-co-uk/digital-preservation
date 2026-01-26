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
        
        var applicationCount = distinctTypes.Count(ct => ct.StartsWith("application/"));
        var videoCount = distinctTypes.Count(ct => ct.StartsWith("video/"));
        var audioCount = distinctTypes.Count(ct => ct.StartsWith("audio/"));
        var imageCount = distinctTypes.Count(ct => ct.StartsWith("image/"));

        if (applicationCount == 1)
        {
            if (videoCount == 1)
            {
                return distinctTypes.Single(ct => ct.StartsWith("video/"));
            }
            if (audioCount == 1)
            {
                return distinctTypes.Single(ct => ct.StartsWith("audio/"));
            }
            if (imageCount == 1)
            {
                return distinctTypes.Single(ct => ct.StartsWith("image/"));
            }
        }
        
        // We still have more than one distinct content type that isn't a generic one, even after our special rules.
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
            // It might really be application/octet-stream, which is OK if that's the best we can do
            distinctTypes.RemoveAll(ct => ct == "binary/octet-stream");
        }
    }
}