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
}