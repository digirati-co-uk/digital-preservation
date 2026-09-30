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
        var valid = len is >= 1 and <= 2000;
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
            if (HasMalformedPercentEscape(slug))
            {
                // '%' is legal only as the start of a percent-encoded escape (%20 etc.) - that's how
                // every deposit file name reaches a slug (EscapeForUriNoHashes). A bare '%' can only
                // come from a caller building ids directly, and doesn't decode to a sensible name.
                reason = "A '%' in a slug must begin a percent-encoded escape such as %20.";
                return false;
            }
            if (UriPathX.IsDotSegment(slug))
            {
                // Every character is legal, but as a whole the slug is a dot segment: resolved against
                // a parent URI it names the parent, not a child.
                reason = "'.' and '..' are not allowed as slugs.";
                return false;
            }
            if (UriPathX.ContainsEncodedSeparator(slug))
            {
                // A well-formed escape is still refused if it decodes to a separator - that would be
                // two segments to anything that decodes it, and the Storage API refuses it too.
                reason = "A slug may not contain an encoded path separator (%2f or %5c).";
                return false;
            }
            return slug != BasePathElement && valid;
        }
        
        // don't build this list in the loop above as it is only an exceptional circumstance
        var badChars = slug.Where(c => !ValidSlugChar(c)).ToList();
        var display = string.Join(',', badChars.Select(c => $"'{c}'"));
        reason = "Character(s) " + display + " are not allowed in slugs.";
        if (badChars.Contains(' '))
        {
            reason += " Spaces are not allowed.";
        }
        return valid;
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
                            || slugChar == '-'
                            // RFC 3986 unreserved, left unescaped by EscapeForUriNoHashes - safe in a
                            // path segment. Needed for DOS 8.3 short names (REPORT~1.DOC) and Office
                            // lock files (~$budget.xlsx), both common in born-digital accessions.
                            || slugChar == '~';
        return valid;
    }

    /// <summary>
    /// Whether the slug contains a '%' that isn't the start of a well-formed percent-encoded escape
    /// (%, then two hex digits).
    /// </summary>
    private static bool HasMalformedPercentEscape(string slug)
    {
        for (var i = 0; i < slug.Length; i++)
        {
            if (slug[i] != '%')
            {
                continue;
            }
            if (i + 2 >= slug.Length || !Uri.IsHexDigit(slug[i + 1]) || !Uri.IsHexDigit(slug[i + 2]))
            {
                return true;
            }
        }
        return false;
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
        var slug = ReplaceMalformedPercentEscapes(sb.ToString());
        // See IsValidSlug: a name that is only a dot segment would resolve to the parent.
        return UriPathX.IsDotSegment(slug) || UriPathX.ContainsEncodedSeparator(slug)
            ? slug.Replace('.', '-').Replace('%', '-')
            : slug;
    }

    /// <summary>
    /// Maps a stray '%' - one that doesn't begin a well-formed percent-encoded escape - to '-'.
    /// A well-formed escape is left as-is.
    /// </summary>
    private static string ReplaceMalformedPercentEscapes(string slug)
    {
        var chars = slug.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] != '%')
            {
                continue;
            }
            if (i + 2 >= chars.Length || !Uri.IsHexDigit(chars[i + 1]) || !Uri.IsHexDigit(chars[i + 2]))
            {
                chars[i] = '-';
            }
        }
        return new string(chars);
    }
}