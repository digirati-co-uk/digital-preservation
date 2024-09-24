using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model;

public abstract class PreservedResource : Resource
{
    [JsonPropertyOrder(10)]
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }
    
    [JsonPropertyName("partOf")]
    [JsonPropertyOrder(50)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Uri? PartOf { get; set; }
    
    [JsonPropertyOrder(200)]
    [JsonPropertyName("origin")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Uri? Origin { get; set; }

    protected string? GetSlug()
    {
        return Id != null ? Id.Segments[^1].Trim('/') : null;
    }

    public const string BasePathElement = "repository";

    public static bool ValidSlug(string slug)
    {
        var len = slug.Length;
        var valid = len is >= 1 and <= 254;
        if (!valid) return valid;
        for (int i = 0; i < len; i++)
        {
            valid = (     slug[i] >= 48 && slug[i] <= 57) // 0-9
                    // || (slug[i] >= 65 && slug[i] <= 90) // A-Z
                       || (slug[i] >= 97 && slug[i] <= 122) // a-z
                    // || slug[i] == '@'
                    // || slug[i] == '/'
                       || slug[i] == '.'
                       || slug[i] == '_'
                       || slug[i] == '-';

            if (!valid) return false;
        }
        return valid;
    } 
    
}