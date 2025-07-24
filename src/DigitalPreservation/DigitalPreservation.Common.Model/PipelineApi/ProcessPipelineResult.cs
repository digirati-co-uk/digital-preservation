using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.PipelineApi;

public class ProcessPipelineResult : Resource
{
    [JsonPropertyOrder(2)]
    [JsonPropertyName("type")]
    public override string Type { get; set; } = nameof(ProcessPipelineResult);

    /// <summary>
    /// A list of errors encountered. These are error objects, not strings. 
    /// </summary>
    [JsonPropertyName("errors")]
    [JsonPropertyOrder(700)]
    public Error[]? Errors { get; set; }

    /// <summary>
    /// One of PipelineJobStates
    /// </summary>
    [JsonPropertyName("status")]
    [JsonPropertyOrder(520)]
    public required string Status { get; set; }


    /// <summary>
    /// Timestamp indicating when the API finished processing the job. Will be null/missing until then.
    /// </summary>
    [JsonPropertyName("dateFinished")]
    [JsonPropertyOrder(610)]
    public DateTime? DateFinished { get; set; }

}
