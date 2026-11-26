using System.Globalization;
using System.Text.RegularExpressions;

namespace DigitalPreservation.Utils;

/// <summary>
/// Reads a file's duration as it is recorded in a premis:significantProperties Duration: plain
/// seconds, as the platform writes it, or the human-readable forms ExifTool prints and the
/// pipeline copies in as they are (issue #261).
/// </summary>
public static partial class DurationValue
{
    /// <summary>
    /// The duration in seconds, or null when the value is none of the forms below.
    /// <list type="bullet">
    /// <item>plain seconds: <c>596</c>, <c>596.04</c>, <c>12.34 s</c> (ExifTool, under 30 s)</item>
    /// <item><c>H:MM:SS</c> or <c>M:SS</c>, with optional decimal seconds: <c>0:09:56</c>, <c>3:42</c></item>
    /// <item>days first, as ExifTool prints a day or more: <c>1 days 2:03:04</c></item>
    /// </list>
    /// A trailing <c>(approx)</c>, ExifTool's mark of an estimated duration, is accepted and dropped:
    /// an estimate is still the best length there is.
    /// </summary>
    public static double? ToSeconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var text = Approx().Replace(value.Trim(), "");

        var seconds = PlainSeconds().Match(text);
        if (seconds.Success)
        {
            return Number(seconds.Groups["s"].Value);
        }

        var clock = Clock().Match(text);
        if (!clock.Success)
        {
            return null;
        }
        var days = clock.Groups["d"].Success ? int.Parse(clock.Groups["d"].Value, CultureInfo.InvariantCulture) : 0;
        var hours = clock.Groups["h"].Success ? int.Parse(clock.Groups["h"].Value, CultureInfo.InvariantCulture) : 0;
        var minutes = int.Parse(clock.Groups["m"].Value, CultureInfo.InvariantCulture);
        var secs = Number(clock.Groups["s"].Value);
        // With hours given, minutes are a clock's; M:SS alone may run past an hour (75:00).
        if ((clock.Groups["h"].Success && minutes > 59) || secs is null or >= 60)
        {
            return null;
        }
        return days * 86400 + hours * 3600 + minutes * 60 + secs;
    }

    private static double? Number(string s) =>
        double.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) ? d : null;

    [GeneratedRegex(@"\s*\(approx\)$", RegexOptions.IgnoreCase)]
    private static partial Regex Approx();

    [GeneratedRegex(@"^(?<s>\d+(\.\d+)?)(\s*s)?$", RegexOptions.IgnoreCase)]
    private static partial Regex PlainSeconds();

    [GeneratedRegex(@"^((?<d>\d+)\s+days?\s+)?((?<h>\d+):)?(?<m>\d+):(?<s>\d+(\.\d+)?)$", RegexOptions.IgnoreCase)]
    private static partial Regex Clock();
}
