

namespace DigitalPreservation.Common.Model.Search;
public  class SearchCollection
{
    public SearchCollectiveFedora? FedoraSearch { get; set; } 

    public string? text { get; set; }
    public int? page { get; set; }
    public int? pageSize { get; set; }

}
