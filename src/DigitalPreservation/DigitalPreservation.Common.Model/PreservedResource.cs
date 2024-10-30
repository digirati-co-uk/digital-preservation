using System.Text;
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

    public string? GetSlug()
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
            var slugChar = slug[i];
            valid = ValidSlugChar(slugChar);
            if (!valid) return false;
        }

        return slug != BasePathElement && valid;
    }

    private static bool ValidSlugChar(char slugChar)
    {
        var valid = (      slugChar >= 48 && slugChar <= 57) // 0-9
                         // || (slugChar >= 65 && slugChar <= 90) // A-Z
                            || (slugChar >= 97 && slugChar <= 122) // a-z
                            // || slugChar == '@'
                            // || slugChar == '/'
                            || slugChar == '.'
                            || slugChar == '_'
                            || slugChar == '-';
        return valid;
    }


    public override string ToString()
    {
        return $"{StringIcon} {Name ?? GetSlug() ?? GetType().Name}";
    }

    [JsonIgnore]
    public abstract string StringIcon { get; }

    public static string MakeValidSlug(string unsafeName)
    {
        var lowered = unsafeName.ToLowerInvariant();
        var sb = new StringBuilder();
        foreach (var c in lowered)
        {
            sb.Append(ValidSlugChar(c) ? c : '-'); // Do we want to use '-'? Or just omit?
        }
        return sb.ToString();
    }
}