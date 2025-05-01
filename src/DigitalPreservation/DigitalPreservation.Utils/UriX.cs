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
}