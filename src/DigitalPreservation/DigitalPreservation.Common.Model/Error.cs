using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model;

public class Error
{
    /// <summary>
    /// Id directly to this version -optional
    /// </summary>
    [JsonPropertyName("id")]
    [JsonPropertyOrder(1)]
    public Uri? Id { get; set; }
    
    /// <summary>
    /// The error text
    /// </summary>
    [JsonPropertyName("message")]
    [JsonPropertyOrder(2)]
    public required string Message { get; set; }
}