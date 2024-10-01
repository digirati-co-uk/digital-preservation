namespace DigitalPreservation.Common.Model.PreservationApi;

public class QueryBase
{
    public Uri? CreatedBy { get; set; }
    public DateTime? CreatedAfter { get; set; }
    public DateTime? CreatedBefore { get; set; }
    public Uri? LastModifiedBy { get; set; }
    public DateTime? LastModifiedAfter { get; set; }
    public DateTime? LastModifiedBefore { get; set; }
    public string? OrderBy { get; set; }
    public bool? Ascending { get; set; }
}