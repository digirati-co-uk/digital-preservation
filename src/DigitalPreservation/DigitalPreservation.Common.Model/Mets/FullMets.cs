namespace DigitalPreservation.Common.Model.Mets;

public class FullMets
{
    public required DigitalPreservation.XmlGen.Mets.Mets Mets { get; set; }
    public required Uri Uri { get; set; }
    public string? ETag { get; set; }
}