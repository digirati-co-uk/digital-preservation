
using DigitalPreservation.Common.Model.PreservationApi;

namespace DigitalPreservation.Common.Model.Search;
public class SearchCollectiveDeposit
{
    public IList<Deposit>? Deposits { get; set; }
    public int? Total { get; set; } 
    public int? Page { get; set; } 
    public int? PageSize { get; set; } 

}
