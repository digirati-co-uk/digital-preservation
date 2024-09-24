using System.Diagnostics.CodeAnalysis;

namespace DigitalPreservation.Core.Utils;

public static class StringUtils
{
    public static bool IsNullOrWhiteSpace([NotNullWhen(false)] this string? s)
    {
        return string.IsNullOrWhiteSpace(s);
    }

    /// <summary>
    /// Does this string have significant content (is not null, empty, or just whitespace character(s))
    /// </summary>
    /// <remarks>
    /// This may seem trivial but it helps code readability.
    /// </remarks>
    /// <param name="str"></param>
    /// <returns></returns>
    public static bool HasText([NotNullWhen(true)] this string? str) => !string.IsNullOrWhiteSpace(str);
    
    /// <summary> 
    /// Removes separator from the start of str if it's there, otherwise leave it alone.
    /// 
    /// "something", "thing" => "something"
    /// "something", "some" => "thing"
    /// 
    /// </summary>
    /// <param name="str"></param>
    /// <param name="start"></param>
    /// <returns></returns>
    public static string? RemoveStart(this string? str, string start)
    {
        switch (str)
        {
            case null:
                return null;
            case "":
                return string.Empty;
        }

        if (str.StartsWith(start) && str.Length > start.Length)
        {
            return str.Substring(start.Length);
        }

        return str;
    }
    
    
    /// <summary>
    /// like String.Replace, but only replaces the first instance of search in str
    /// </summary>
    /// <param name="str"></param>
    /// <param name="search"></param>
    /// <param name="replace"></param>
    /// <returns></returns>
    public static string ReplaceFirst(this string str, string search, string replace)
    {
        if (string.IsNullOrEmpty(search))
        {
            return str;
        }
        int pos = str.IndexOf(search, StringComparison.Ordinal);
        if (pos < 0)
        {
            return str;
        }
        return str.Substring(0, pos) + replace + str.Substring(pos + search.Length);
    }

    public static string BuildPath(bool startWithSeparator, params string?[] elements)
    {
        var parts = new List<string>();
        foreach (var element in elements)
        {
            if (element.HasText())
            {
                var subParts = element.Split("/", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                parts.AddRange(subParts);
            }
        }
        var path = string.Join("/", parts);
        if(startWithSeparator) path = "/" + path;
        return path;
    }

    public static Uri? GetParentUri(this Uri uri)
    {
        if (uri.AbsolutePath == "/")
        {
            return null;
        }
        if (uri.AbsolutePath.EndsWith('/'))
        {
            return new Uri(uri, "..");
        }
        return new Uri(uri, ".");
    }
    
    
    public static string? GetSlug(this Uri uri)
    {
        if (uri.Segments.Length > 0)
        {
            return uri.Segments[^1].Trim('/');
        }
        return null;
    }
}