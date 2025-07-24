using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.PipelineApi;

public class PipelineJob : Resource
{
    [JsonPropertyOrder(2)]
    [JsonPropertyName("type")]
    public override string Type { get; set; } = nameof(PipelineJob);

    [JsonPropertyName("depositname")]
    [JsonPropertyOrder(520)]
    public string? DepositName { get; set; }
}
