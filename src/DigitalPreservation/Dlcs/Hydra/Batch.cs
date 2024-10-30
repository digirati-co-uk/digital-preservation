using System.Text.Json.Serialization;

namespace Dlcs.Hydra;

public class Batch : JSONLDBase
{
    [JsonPropertyName("count")]
    [JsonPropertyOrder(11)]
    public int Count { get; set; }

    [JsonPropertyName("completed")]
    [JsonPropertyOrder(12)]
    public int Completed { get; set; }

    [JsonPropertyName("errors")]
    [JsonPropertyOrder(13)]
    public int Errors { get; set; }

    [JsonPropertyName("submitted")]
    [JsonPropertyOrder(14)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? Submitted { get; set; }

    [JsonPropertyName("finished")]
    [JsonPropertyOrder(15)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? Finished { get; set; }

    [JsonPropertyName("superseded")]
    [JsonPropertyOrder(16)]
    public bool Superseded { get; set; }
}