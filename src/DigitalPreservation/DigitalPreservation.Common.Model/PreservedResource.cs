using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json.Serialization;
using DigitalPreservation.Common.Model.Results;
using DigitalPreservation.Utils;

namespace DigitalPreservation.Common.Model;

public abstract class PreservedResource : Resource
{
    [JsonPropertyOrder(10)]
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }
    
    [JsonPropertyOrder(11)]
    [JsonPropertyName("otherNames")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? OtherNames { get; set; }
    
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

    /// <summary>
    /// Use with care - this is just looking at strings, it can't tell whether the
    /// paths actually exist
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static Result ValidPath([NotNullWhen(true)] string? path)
    {
        if (path.IsNullOrWhiteSpace())
        {
            return Result.Fail("Path is null or whitespace");;
        }
        var parts = path.Split('/');
        var badCharMessages = new List<string?>();
        foreach (var part in parts)
        {
            if (!ValidSlug(part, out var reason))
            {
                badCharMessages.Add(reason);
            }
        }

        if (badCharMessages.Count == 0)
        {
            return Result.Ok();
        }
        
        return Result.Fail(ErrorCodes.BadRequest, string.Join(';', badCharMessages));
    }

    public static bool ValidSlug(string? slug, out string? reason)
    {
        reason = null;
        if (slug.IsNullOrWhiteSpace())
        {
            return false;
        }
        var len = slug.Length;
        var valid = len is >= 1 and <= 2000; // 254;
        if (!valid)
        {
            reason = "Slug must be between 1 and 2000 characters";
            return valid;
        }
        for (int i = 0; i < len; i++)
        {
            var slugChar = slug[i];
            valid = ValidSlugChar(slugChar);
            if (!valid) break;
        }

        if (valid)
        {
            return slug != BasePathElement && valid;
        }
        
        // don't build this list in the loop above as it is only an exceptional circumstance
        var badChars = string.Join(',', slug.Where(c => !ValidSlugChar(c)));
        reason = "Character(s) " + badChars + " are not allowed in slugs";
        return valid;
    }

    private static bool ValidSlug(string? slug)
    {
        return ValidSlug(slug, out _);
    }

    private static bool ValidSlugChar(char slugChar)
    {
        var valid = (      slugChar >= 48 && slugChar <= 57) // 0-9
                            || (slugChar >= 65 && slugChar <= 90) // A-Z
                            || (slugChar >= 97 && slugChar <= 122) // a-z
                            // || slugChar == '@'
                            // || slugChar == '/'
                            || slugChar == '%'
                            || slugChar == '('
                            || slugChar == ')'
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