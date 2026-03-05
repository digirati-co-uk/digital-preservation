using DigitalPreservation.Common.Model.Import;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DigitalPreservation.Common.Model.DepositArchiver;

public class ArchiveJobResult : Resource
{
    [JsonPropertyOrder(2)]
    [JsonPropertyName("type")]
    public override string Type { get; set; } = nameof(ArchiveJobResult);

    [JsonPropertyOrder(3)]
    [JsonPropertyName("depositId")]
    public string? DepositId { get; set; }


    /// <summary>
    /// Timestamp indicating when the API finished processing the job. Will be null/missing until then.
    /// </summary>
    [JsonPropertyName("dateBegun")]
    [JsonPropertyOrder(611)]
    public DateTime? DateBegun { get; set; }

    /// <summary>
    /// Timestamp indicating when the archiver finished processing the job. Will be null/missing until then.
    /// </summary>
    [JsonPropertyName("dateFinished")]
    [JsonPropertyOrder(610)]
    public DateTime? DateFinished { get; set; }

    /// <summary>
    /// A list of errors encountered. These are error objects, not strings. 
    /// </summary>
    [JsonPropertyName("errors")]
    [JsonPropertyOrder(700)]
    public Error[]? Errors { get; set; }

    /// <summary>
    /// Success or failure
    /// </summary>
    [JsonPropertyName("status")]
    [JsonPropertyOrder(800)]
    public string? Status { get; set; }
}
