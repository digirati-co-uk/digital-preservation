

namespace DigitalPreservation.Common.Model.Search;
public class SearchCollectiveFedora
{
    public int? Total { get; set; } = 0;
    public int? Count { get; set; } = 0;
    public SearchResultFedora[]? Results { get; set; }
    public int? PageSize { get; set; } = 50;
    public int? Page { get; set; } = 0;
}
