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
}