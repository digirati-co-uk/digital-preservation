using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.PipelineApi;

public class ProcessPipelineResult : Resource
{
    [JsonPropertyOrder(2)]
    [JsonPropertyName("type")]
    public override string Type { get; set; } = nameof(ProcessPipelineResult);

    [JsonPropertyOrder(3)]
    [JsonPropertyName("jobId")]
    public string? JobId { get; set; }

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
    [JsonPropertyName("dateBegun")]
    [JsonPropertyOrder(611)]
    public DateTime? DateBegun { get; set; }

    /// <summary>
    /// Timestamp indicating when the API finished processing the job. Will be null/missing until then.
    /// </summary>
    [JsonPropertyName("dateFinished")]
    [JsonPropertyOrder(610)]
    public DateTime? DateFinished { get; set; }


    /// <summary>
    /// Explicitly included for convenience; the deposit the job was started from.
    /// </summary>
    [JsonPropertyName("deposit")]
    [JsonPropertyOrder(100)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Deposit { get; set; }

    /// <summary>
    /// Also included for convenience, the repository object the changes specified in the job are being applied to
    /// </summary>
    [JsonPropertyName("archivalGroupName")]
    [JsonPropertyOrder(510)]
    public string? ArchivalGroupName { get; set; }

    [JsonPropertyOrder(511)]
    [JsonPropertyName("runUser")]
    public string? RunUser { get; set; }

    [JsonPropertyOrder(512)]
    [JsonPropertyName("cleanupProcessJob")]
    public bool CleanupProcessJob { get; set; } = false;

    [JsonPropertyOrder(513)]
    [JsonPropertyName("virusDefinition")]
    public string? VirusDefinition { get; set; }

}
