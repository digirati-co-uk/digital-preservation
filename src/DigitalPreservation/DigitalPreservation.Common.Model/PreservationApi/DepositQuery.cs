namespace DigitalPreservation.Common.Model.PreservationApi;

public class DepositQuery : QueryBase
{
    public string? ArchivalGroupPath { get; set; }
    public Uri? PreservedBy { get; set; }
    public DateTime? PreservedAfter { get; set; }
    public DateTime? PreservedBefore { get; set; }
    public Uri? ExportedBy { get; set; }
    public DateTime? ExportedAfter { get; set; }
    public DateTime? ExportedBefore { get; set; }
    public string? Status { get; set; }
    public bool Active { get; set; }

    public override bool NoTerms()
    {
        return base.NoTerms() &&
               ArchivalGroupPath is null &&
               PreservedBy is null &&
               PreservedAfter is null &&
               PreservedBefore is null &&
               ExportedBy is null &&
               ExportedAfter is null &&
               ExportedBefore is null &&
               Status is null &&
               Active == false;
    }
}