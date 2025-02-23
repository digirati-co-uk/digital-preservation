using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.DepositHelpers;

public class AddSelectionToMets
{
    [JsonPropertyOrder(1)]
    [JsonPropertyName("deposit")]
    public Uri? Deposit { get; set; }
    
    // This form can only work if the DepositFileSystem JSON has content types and digests already
    [JsonPropertyOrder(2)]
    [JsonPropertyName("items")]
    public List<MinimalItem> Items { get; set; } = [];
    
    // A separate process is required for bagits
}