using System.Text.Json.Serialization;
using DigitalPreservation.Utils;

namespace DigitalPreservation.Common.Model.Transit.Extensions;


public class FileLink
{
    [JsonPropertyName("to")]
    [JsonPropertyOrder(1)]
    public required string To { get; set; }
    
    [JsonPropertyName("role")]
    [JsonPropertyOrder(2)]
    public Uri? Role { get; set; }

    /// <summary>
    /// What kind of document the linked file is, when the METS says so and its media type doesn't:
    /// <see cref="FileLinkProfiles.Alto"/> for a page's OCR, which METS marks only by its fileGrp
    /// (issue #261). Read from the METS, never written to it.
    /// </summary>
    [JsonPropertyName("profile")]
    [JsonPropertyOrder(3)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Profile { get; set; }
}

public static class FileLinkProfiles
{
    /// <summary>
    /// ALTO OCR. As a IIIF seeAlso profile it is what text services (IIIF-CS's text pipeline among
    /// them) look for: any profile containing "alto".
    /// </summary>
    public const string Alto = "http://www.loc.gov/standards/alto/";
}


public static class FileLinkRoles
{
    private const string Prefix = "http://iiif.io/api/presentation/3#";

    public static Dictionary<string, Uri> ProvidesUriFromKeyword { get; }
    public static Dictionary<Uri, string> ProvidesKeywordFromUri { get; }

    private static readonly Uri Supplementing = new(Prefix + "supplementing");
    
    public static Uri FromIiifProvides(string keyword)
    {
        return ProvidesUriFromKeyword.GetValueOrDefault(keyword, Supplementing);
    }

    public static string? ToIiifProvides(Uri? uri)
    {
        return uri == null ? null : ProvidesKeywordFromUri.GetValueOrDefault(uri);
    }
    
    static FileLinkRoles()
    {
        ProvidesUriFromKeyword = new Dictionary<string, Uri>
        {
            { "transcript", new Uri(Prefix + "transcript") },
            { "translation", new Uri(Prefix + "translation") },
            { "closedCaptions", new Uri(Prefix + "closedCaptions") },
            { "alternativeText", new Uri(Prefix + "alternativeText") },
            { "longDescription", new Uri(Prefix + "longDescription") },
            { "highContrastAudio", new Uri(Prefix + "highContrastAudio") },
            { "highContrastDisplay", new Uri(Prefix + "highContrastDisplay") },
            
            { "supplementing", new Uri(Prefix + "supplementing") }
        };
        // Uri.GetHashCode() ignores fragments in .NET, so use a comparer based on AbsoluteUri string
        ProvidesKeywordFromUri = ProvidesUriFromKeyword.ToDictionary(
            kvp => kvp.Value,
            kvp => kvp.Key,
            new UriAbsoluteStringComparer());
    }
}