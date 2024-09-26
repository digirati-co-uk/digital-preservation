using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Storage.Ocfl;

public class User
{
    [JsonPropertyName("name")]
    [JsonPropertyOrder(1)]
    public required string Name { get; set; }


    [JsonPropertyName("address")]
    [JsonPropertyOrder(2)]
    public string? Address { get; set; }
}