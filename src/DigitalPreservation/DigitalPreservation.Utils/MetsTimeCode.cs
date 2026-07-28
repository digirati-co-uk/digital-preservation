using System.Globalization;

namespace DigitalPreservation.Utils;

/// <summary>
/// Converts between METS area time codes (HH:MM:SS with optional decimal seconds)
/// and time as a number of seconds, as used in <c>mets:area BETYPE="TIME"</c> elements.
/// </summary>
public static class MetsTimeCode
{
    /// <summary>
    /// Parses a METS time code string in HH:MM:SS or HH:MM:SS.sss format to total seconds.
    /// </summary>
    /// <param name="timeCode">A time code string, e.g. "00:35:09.5" or "01:00:00".</param>
    /// <returns>Total number of seconds, e.g. 2109.5</returns>
    /// <exception cref="FormatException">The string is not in the expected format.</exception>
    public static double ToSeconds(string timeCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(timeCode);

        var parts = timeCode.Split(':');
        if (parts.Length != 3)
            throw new FormatException($"Time code must be in HH:MM:SS or HH:MM:SS.sss format: '{timeCode}'");

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) || hours < 0)
            throw new FormatException($"Invalid hours in time code: '{timeCode}'");

        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) || minutes < 0 || minutes > 59)
            throw new FormatException($"Invalid minutes in time code: '{timeCode}'");

        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || seconds < 0 || seconds >= 60)
            throw new FormatException($"Invalid seconds in time code: '{timeCode}'");

        return hours * 3600 + minutes * 60 + seconds;
    }

    /// <summary>
    /// Formats total seconds as a METS time code string in HH:MM:SS or HH:MM:SS.sss format.
    /// Fractional seconds are included only when non-zero, with trailing zeros removed.
    /// Precision is to the nearest millisecond.
    /// </summary>
    /// <param name="totalSeconds">Total number of seconds, e.g. 2109.5</param>
    /// <returns>A time code string, e.g. "00:35:09.5"</returns>
    /// <exception cref="ArgumentOutOfRangeException">totalSeconds is negative.</exception>
    public static string FromSeconds(double totalSeconds)
    {
        if (totalSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(totalSeconds), "Time cannot be negative.");

        // Round to millisecond precision to avoid floating-point noise in arithmetic
        totalSeconds = Math.Round(totalSeconds, 3);

        var hours = (int)(totalSeconds / 3600);
        var remaining = totalSeconds - hours * 3600;
        var minutes = (int)(remaining / 60);
        var seconds = Math.Round(remaining - minutes * 60, 3);

        var wholeSeconds = (int)seconds;
        var fractional = Math.Round(seconds - wholeSeconds, 3);

        if (fractional < 0.0005)
            return $"{hours:D2}:{minutes:D2}:{wholeSeconds:D2}";

        // "0.###" gives e.g. "0.5", "0.54", "0.001" — trimming the leading "0" gives ".5", ".54", ".001"
        var fracStr = fractional.ToString("0.###", CultureInfo.InvariantCulture).TrimStart('0');
        return $"{hours:D2}:{minutes:D2}:{wholeSeconds:D2}{fracStr}";
    }
}
