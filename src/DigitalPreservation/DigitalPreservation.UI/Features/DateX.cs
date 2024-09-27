namespace DigitalPreservation.UI.Features;

public static class DateX
{
    public static string? GetDateDisplay(this DateTime? dt)
    {
        return dt?.ToString("s").Replace("T", " ");
    }
}