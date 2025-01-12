namespace DigitalPreservation.Common.Model.PreservationApi;

public class QueryBase
{
    public string? CreatedBy { get; set; }
    public DateTime? CreatedAfter { get; set; }
    public DateTime? CreatedBefore { get; set; }
    public string? LastModifiedBy { get; set; }
    public DateTime? LastModifiedAfter { get; set; }
    public DateTime? LastModifiedBefore { get; set; }
    public string? OrderBy { get; set; }
    public bool? Ascending { get; set; }
    
    public int? Page { get; set; }
    public int? PageSize { get; set; }

    public virtual bool NoTerms()
    {
        return
            CreatedBy is null &&
            CreatedAfter is null &&
            CreatedBefore is null &&
            LastModifiedBy is null &&
            LastModifiedAfter is null &&
            LastModifiedBefore is null;
    }
}