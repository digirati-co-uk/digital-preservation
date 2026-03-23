using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit.Extensions;

public class FileLink
{
    [JsonPropertyName("to")]
    [JsonPropertyOrder(1)]
    public required string To { get; set; }
    
    [JsonPropertyName("role")]
    [JsonPropertyOrder(2)]
    public Uri? Role { get; set; }
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
    
    static FileLinkRoles()
    {
        ProvidesUriFromKeyword = new Dictionary<string, Uri>
        {
            { "closedCaptions", new Uri(Prefix + "closedCaptions") },
            { "alternativeText", new Uri(Prefix + "alternativeText") },
            { "longDescription", new Uri(Prefix + "longDescription") },
            { "highContrastAudio", new Uri(Prefix + "highContrastAudio") },
            { "highContrastDisplay", new Uri(Prefix + "highContrastDisplay") },
            { "transcript", new Uri(Prefix + "transcript") },
            { "translation", new Uri(Prefix + "translation") }
        };
        ProvidesKeywordFromUri = ProvidesUriFromKeyword.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);
    }
}