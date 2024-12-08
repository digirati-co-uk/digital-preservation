using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Storage.Ocfl;

public class Version
{
    [JsonPropertyName("created")]
    [JsonPropertyOrder(1)]
    public required DateTime Created { get; set; }

    [JsonPropertyName("user")]
    [JsonPropertyOrder(3)]
    public required User User { get; set; }

    [JsonPropertyName("state")]
    [JsonPropertyOrder(4)]
    public required Dictionary<string, string[]> State { get; set; }
}
