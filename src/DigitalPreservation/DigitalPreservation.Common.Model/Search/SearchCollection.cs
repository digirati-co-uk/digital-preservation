

using DigitalPreservation.Common.Model.Identity;

namespace DigitalPreservation.Common.Model.Search;
public  class SearchCollection
{
    public SearchCollectiveFedora? FedoraSearch { get; set; } 

    public string? text { get; set; }
    public int? page { get; set; }
    public int? pageSize { get; set; }

    public SearchCollectiveDeposit? DepositSearch { get; set; }

    public Identifier? Identifier { get; set; }
}
