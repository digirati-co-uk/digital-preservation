using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DigitalPreservation.Common.Model.PipelineApi;
public class LogPipelineStatusResult
{
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
}
