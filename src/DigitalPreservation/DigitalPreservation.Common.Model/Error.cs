using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model;

public class Error
{
    /// <summary>
    /// Id directly to this version
    /// </summary>
    [JsonPropertyName("id")]
    [JsonPropertyOrder(1)]
    public Uri? Id { get; set; }
    
    /// <summary>
    /// 
    /// </summary>
    public required string Message { get; set; }
}