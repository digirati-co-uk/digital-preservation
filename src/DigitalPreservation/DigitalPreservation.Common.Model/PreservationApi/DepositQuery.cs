namespace DigitalPreservation.Common.Model.PreservationApi;

public class DepositQuery : QueryBase
{
    public const string LastModified = "LastModified";
    public const string Created = "Created";
    public const string Preserved = "Preserved";
    public const string Exported = "Exported";
    public string? ArchivalGroupPath { get; set; }
    public string? ArchivalGroupPathParent { get; set; }
    public string? PreservedBy { get; set; }
    public DateTime? PreservedAfter { get; set; }
    public DateTime? PreservedBefore { get; set; }
    public string? ExportedBy { get; set; }
    public DateTime? ExportedAfter { get; set; }
    public DateTime? ExportedBefore { get; set; }
    public string? Status { get; set; }
    public bool? ShowAll { get; set; }
    public bool? ShowForm { get; set; }

    public override bool NoTerms()
    {
        return base.NoTerms() &&
               ArchivalGroupPath is null &&
               ArchivalGroupPathParent is null &&
               PreservedBy is null &&
               PreservedAfter is null &&
               PreservedBefore is null &&
               ExportedBy is null &&
               ExportedAfter is null &&
               ExportedBefore is null &&
               Status is null &&
               ShowAll is null or false;
    }
}