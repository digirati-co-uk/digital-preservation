using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.DepositHelpers;

/// <summary>
/// This class is accepted by the API but can be used by the UI or other clients unknown
/// to manipulate the deposit and its METS outside of API interactions.
/// </summary>
public class DeleteSelection
{
    [JsonPropertyOrder(1)]
    [JsonPropertyName("deposit")]
    public Uri? Deposit { get; set; }
    
    [JsonPropertyOrder(2)]
    [JsonPropertyName("deleteFromMets")]
    public bool DeleteFromMets { get; set; }
    
    [JsonPropertyOrder(3)]
    [JsonPropertyName("deleteFromDepositFiles")]
    public bool DeleteFromDepositFiles { get; set; }
    
    [JsonPropertyOrder(4)]
    [JsonPropertyName("items")]
    public List<MinimalItem> Items { get; set; } = [];
}