using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model;

public abstract class Resource
{
    [JsonPropertyOrder(1)]
    [JsonPropertyName("id")]
    public Uri? Id { get; set; }

    [JsonPropertyOrder(2)]
    [JsonPropertyName("type")]
    public abstract string Type { get; set; }

    [JsonPropertyOrder(500)]
    [JsonPropertyName("created")]
    public DateTime? Created { get; set; }

    [JsonPropertyOrder(501)]
    [JsonPropertyName("createdBy")]
    public Uri? CreatedBy { get; set; }

    [JsonPropertyOrder(502)]
    [JsonPropertyName("lastModified")]
    public DateTime? LastModified { get; set; }

    [JsonPropertyOrder(503)]
    [JsonPropertyName("lastModifiedBy")]
    public Uri? LastModifiedBy { get; set; }
}