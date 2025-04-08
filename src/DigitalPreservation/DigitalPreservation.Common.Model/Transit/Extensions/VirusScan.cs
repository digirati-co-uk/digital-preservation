namespace DigitalPreservation.Common.Model.Transit.Extensions;

public class VirusScan
{
    public bool HasVirus { get; set; }
    public string? GetDisplay()
    {
        return HasVirus ? "☣" : "✅";
    }
}