namespace DigitalPreservation.Utils;

public static class UriX
{
    public static string GetStringTemporaryForTesting(this Uri uri)
    {
        var originalString = uri.OriginalString;
        var s = uri.ToString();
        if (s == originalString) return originalString;
        
        Console.WriteLine($"uri.OriginalString is '{originalString}', uri.ToString() is '{s}'");
        if (s.IsNullOrWhiteSpace())
        {
            Console.WriteLine("uri.ToString() is empty");
        }
        return originalString.HasText() ? originalString : s;
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
            return new Uri(newUri.ToString().TrimEnd('/'));
        }

        return newUri;
    }
    
    // TODO: URI
    /// <summary>
    /// </summary>
    /// <param name="uri"></param>
    /// <param name="escapedSlug"></param>
    /// <returns></returns>
    public static Uri AppendEscapedSlug(this Uri uri, string escapedSlug)
    {
        if (escapedSlug.IsNullOrWhiteSpace())
        {
            throw new Exception("Cannot append empty slug");
        }

        if (escapedSlug[0] == '/')
        {
            // We will take care of the trailing slash on the parent
            escapedSlug = escapedSlug[1..];
        }
        
        if (escapedSlug == string.Empty)
        {
            throw new Exception("Cannot append empty slug");
        }
        
        // We need to allow a trailing slash, but not a leading one
        // if (escapedSlug[^1] == '/')
        // {
        //     escapedSlug = escapedSlug[..^1];
        // }
        // if (escapedSlug == string.Empty)
        // {
        //     throw new Exception("Cannot append empty slug");
        // }

        if (escapedSlug[..^1].Contains('/'))
        {
            // We want to do this to force callers to escape parts, because we DO NOT want to escape '/'
            throw new Exception("Cannot append slug with '/' in it except at the end");
        }
        
        var s = uri.ToString();
        if (!s.EndsWith('/'))
        {
            uri = new Uri(s + "/");
        }
        return new Uri(uri, escapedSlug);

        //var newUriString = uri.GetStringTemporaryForTesting().TrimEnd('/') + '/' + slug.TrimStart('/');
        //return new Uri(newUriString);
    }
    
    public static string EscapeForUri(this string s)
    {
        return Uri.EscapeDataString(s);
    }
    
    public static string UnEscapeFromUri(this string s)
    {
        return Uri.UnescapeDataString(s);
    }
    
    public static string EscapePathElements(this string path)
    {
        var escapedParts = path.Split('/').Select(EscapeForUri);
        return string.Join("/", escapedParts);
    }
    
    public static string UnEscapePathElements(this string path)
    {
        var unescapedParts = path.Split('/').Select(UnEscapeFromUri);
        return string.Join("/", unescapedParts);
    }
}