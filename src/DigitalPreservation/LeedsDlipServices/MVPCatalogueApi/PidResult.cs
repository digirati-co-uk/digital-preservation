using System.Text.Json.Serialization;

namespace LeedsDlipServices.MVPCatalogueApi;

public class PidResult
{
    [JsonPropertyOrder(100)]
    [JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [JsonPropertyOrder(200)]
    [JsonPropertyName("error")]
    public string? Error  { get; set; }
    
    [JsonPropertyOrder(300)]
    [JsonPropertyName("data")]
    public CatalogueRecord? Data { get; set; }
    
}