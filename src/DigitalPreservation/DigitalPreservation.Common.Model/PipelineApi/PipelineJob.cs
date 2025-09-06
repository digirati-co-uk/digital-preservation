using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.PipelineApi;

public class PipelineJob : Resource
{
    [JsonPropertyOrder(2)]
    [JsonPropertyName("type")]
    public override string Type { get; set; } = nameof(PipelineJob);

    [JsonPropertyName("depositName")]
    [JsonPropertyOrder(520)]
    public string? DepositName { get; set; }

    [JsonPropertyName("jobIdentifier")]
    [JsonPropertyOrder(521)]
    public string? JobIdentifier { get; set; }

    [JsonPropertyName("runUser")]
    [JsonPropertyOrder(530)]
    public string? RunUser { get; set; }
}
