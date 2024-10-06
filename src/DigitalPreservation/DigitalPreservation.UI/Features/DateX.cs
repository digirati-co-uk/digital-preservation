namespace DigitalPreservation.UI.Features;

public static class DateX
{
    public static string? GetDateDisplay(this DateTime? dt, string? fallback = null)
    {
        return !dt.HasValue ? fallback : dt?.ToString("s").Replace("T", " ");
    }
}