using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.PreservationApi;

public class PreservationVersion
{
    /// <summary>
    /// Id directly to this version
    /// </summary>
    [JsonPropertyName("id")]
    [JsonPropertyOrder(1)]
    public Uri? Id { get; set; }
    
    /// <summary>
    /// Name of this version (e.g. v1, v2 etc)
    /// </summary>
    [JsonPropertyOrder(2)]
    public string? Name { get; set; }
    
    /// <summary>
    /// Date of this version
    /// </summary>
    [JsonPropertyOrder(3)]
    public DateTime Date { get; set; }
}