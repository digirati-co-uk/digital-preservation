

using System.Text.Json.Serialization;
using DigitalPreservation.Common.Model.Identity;

namespace DigitalPreservation.Common.Model.Search;
public  class SearchCollection
{
    public SearchCollectiveFedora? FedoraSearch { get; set; }

    public string? text { get; set; }

    public SearchCollectiveDeposit? DepositSearch { get; set; }

    public Identifier? Identifier { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SearchType? SearchType { get; set; }
}

public enum SearchType
{
    All,
    Deposits,
    Fedora
}