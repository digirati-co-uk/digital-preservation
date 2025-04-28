namespace DigitalPreservation.Common.Model.ToolOutput.Siegfried;

public class TechnicalProvenance
{
    public string? Siegfried { get; set; }
    public DateTime? Scandate { get; set; }
    public string? Signature { get; set; }
    public DateTime? Created { get; set; }
    public List<Identifier>? Identifiers { get; set; } = [];
}