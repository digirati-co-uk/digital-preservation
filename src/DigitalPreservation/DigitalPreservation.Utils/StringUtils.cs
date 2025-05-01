using System.Diagnostics.CodeAnalysis;

namespace DigitalPreservation.Utils;

public static class StringUtils
{
    private static readonly string[] FileSizeSuffixes;
    static StringUtils()
    {
        //Longs run out around EB
        FileSizeSuffixes = new[] { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
    }
    
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

    /// <summary>
    /// This should only be used for simple URIs, no query strings, colon separators etc
    /// </summary>
    /// <param name="uri"></param>
    /// <param name="slug"></param>
    /// <returns></returns>
    public static Uri AppendSlug(this Uri uri, string slug)
    {
        var newUriString = uri.GetStringTemporaryForTesting().TrimEnd('/') + '/' + slug.TrimStart('/');
        return new Uri(newUriString);
    }

    public static Uri? GetParentUri(this Uri uri, bool trimTrailingSlash = false)
    {
        if (uri.AbsolutePath == "/")
        {
            return null;
        }

        Uri newUri;
        if (uri.AbsolutePath.EndsWith('/'))
        {
            newUri = new Uri(uri, "..");
        }
        else
        {
            newUri = new Uri(uri, ".");
        }

        if (trimTrailingSlash)
        {
            return new Uri(newUri.GetStringTemporaryForTesting().TrimEnd('/'));
        }

        return newUri;
    }
    
    
    public static string? GetSlug(this Uri uri)
    {
        if (uri.Segments is ["/"])
        {
            return null;
        }
        if (uri.Segments.Length > 0)
        {
            return uri.Segments[^1].Trim('/');
        }
        return null;
    }
    
    
    public static string GetSlug(this string path)
    {
        // Will return "slug" for .../slug and /slug/ - do we want that?
        if (path.EndsWith('/'))
        {
            path = path[..^1];
        }
        return path.Split('/')[^1];
    }

    public static string GetUriSafeSlug(this string path)
    {
        return path.GetSlug()
            .Replace("#", "(_hash_)")
            .Replace("?", "(_query_)");
    }
    
    
    public static string GetUriSafePath(this string path)
    {
        return path
            .Replace("#", "(_hash_)")
            .Replace("?", "(_query_)");
    }
    
    
    public static string? GetParent(this string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }
        var joined = string.Join('/', parts[..^1]);
        if (path.StartsWith('/'))
        {
            return '/' + joined;
        }
        return joined;
    }

    /// <summary>
    /// Create a nice display format for file size given a raw byte value
    /// 
    /// 42 => "42 B"
    /// 1100 => "1.07 KB"
    /// 6958472 => "6.37 MB"
    /// 
    /// </summary>
    /// <param name="sizeInBytes"></param>
    /// <param name="withSpace">include a space between number and unit</param>
    /// <param name="fallbackIfNull">Return this string if the size is null</param>
    /// <returns></returns>
    public static string FormatFileSize(long? sizeInBytes, bool withSpace = false, string fallbackIfNull = "-")
    {
        if (!sizeInBytes.HasValue)
        {
            return fallbackIfNull;
        }
        var spacer = withSpace ? " " : "";
        if (sizeInBytes == 0)
            return "0" + spacer + FileSizeSuffixes[0];
        long bytes = Math.Abs(sizeInBytes.Value);
        int place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
        double num = Math.Round(bytes / Math.Pow(1024, place), 1);
        return (Math.Sign(sizeInBytes.Value) * num) + spacer +  FileSizeSuffixes[place];
    }

    public static string AsShortInputDate(this DateTime? date)
    {
        return date.HasValue ? date.Value.ToString("yyyy-MM-dd") : string.Empty;
    }
    
    /// <summary>
    /// Must not return a path that ends with a "/"
    /// </summary>
    /// <param name="strings"></param>
    /// <returns></returns>
    public static string GetCommonParent(IEnumerable<string> strings)
    {
        return GetCommonParent(strings.ToArray());
    }

    /// <summary>
    /// Always return a common folder path, not simply a prefix
    /// so "/foo/bar", "/foo/baz" => "/foo", and not "/foo/ba" 
    /// </summary>
    /// <param name="strings"></param>
    /// <returns></returns>
    public static string GetCommonParent(string[] strings)
    {
        var prefix = GetCommonPrefix(strings);
        if (prefix == "/" || prefix.IsNullOrWhiteSpace())
        {
            return string.Empty; 
        }

        if (prefix.EndsWith('/'))
        {
            return prefix[..^1];
        }
        
        var slash = prefix.LastIndexOf('/');
        if (slash == -1)
        {
            return string.Empty;
        }
        return prefix[..^(prefix.Length - slash)];
    }

    public static string GetCommonPrefix(string[] strings)
    {
        if (strings.Length == 0)
        {
            return string.Empty;
        }
        var prefix = strings[0];
        for (int i = 1; i < strings.Length; i++)
        {
            var current = string.Empty;
            for (int j = 0; j < strings[i].Length; j++)
            {
                if (j >= prefix.Length)
                {
                    break;
                }

                if (strings[i][j] == prefix[j])
                {
                    current += strings[i][j];
                }
                else
                {
                    break;
                }
            }
            prefix = current;
        }
        
        return prefix;
    }
    
    public static string GetCommonPrefix(IEnumerable<string> strings)
    {
        return GetCommonPrefix(strings.ToArray());
    }
}