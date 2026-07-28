using DigitalPreservation.Common.Model.Transit.Extensions;

namespace DigitalPreservation.Mets;

/// <summary>
/// Parses spatial coordinates from <c>mets:area</c> elements.
/// <para>
/// The METS area element supports SHAPE values of RECT, CIRCLE, and POLY.
/// Currently only RECT (axis-aligned rectangle) is supported; other shapes return null.
/// </para>
/// </summary>
public static class MetsAreaCoords
{
    /// <summary>
    /// Attempts to parse a METS area rectangle from SHAPE and COORDS attributes.
    /// Returns null if <paramref name="shape"/> is not "RECT" or if <paramref name="coords"/>
    /// is missing or cannot be parsed as four integers (x1, y1, x2, y2).
    /// </summary>
    public static Rectangle? TryParseRect(string? shape, string? coords)
    {
        if (!string.Equals(shape, "RECT", StringComparison.OrdinalIgnoreCase))
            return null;
        if (string.IsNullOrEmpty(coords))
            return null;

        var parts = coords.Split(',');
        if (parts.Length == 4 &&
            int.TryParse(parts[0].Trim(), out var x1) &&
            int.TryParse(parts[1].Trim(), out var y1) &&
            int.TryParse(parts[2].Trim(), out var x2) &&
            int.TryParse(parts[3].Trim(), out var y2))
        {
            return new Rectangle { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 };
        }

        return null;
    }
}
