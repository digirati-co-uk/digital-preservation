using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Export;

public class Export : Resource
{
    [JsonPropertyOrder(2)]
    [JsonPropertyName("type")]
    public override string Type { get; set; } = nameof(Export);
    
    [JsonPropertyName("archivalGroup")]
    [JsonPropertyOrder(510)]
    public required Uri ArchivalGroup { get; set; }
    
    /// <summary>
    /// The S3 location (later maybe other locations) to which the object was exported
    /// </summary>
    [JsonPropertyName("destination")]
    [JsonPropertyOrder(511)]
    public required Uri Destination { get; set; }
    
    /// <summary>
    /// The version of the DigitalObject applied to - known at the start of processing
    /// </summary>
    [JsonPropertyName("sourceVersion")]
    [JsonPropertyOrder(651)]
    public required string SourceVersion { get; set; }
    
    /// <summary>
    /// Timestamp indicating when the API started processing the job. Will be null/missing until then.
    /// </summary>
    [JsonPropertyName("dateBegun")]
    [JsonPropertyOrder(600)]
    public DateTime? DateBegun { get; set; }
    
    /// <summary>
    /// Timestamp indicating when the API finished processing the job. Will be null/missing until then.
    /// </summary>
    [JsonPropertyName("dateFinished")]
    [JsonPropertyOrder(610)]
    public DateTime? DateFinished { get; set; }
    
    /// <summary>
    /// A list of all the files exported - S3 URIs, typically; could be filesystem paths later
    /// </summary>
    [JsonPropertyName("files")]
    [JsonPropertyOrder(21)]
    public List<Uri> Files { get; set; } = [];
    
    /// <summary>
    /// A list of errors encountered. These are error objects, not strings. 
    /// </summary>
    [JsonPropertyName("errors")]
    [JsonPropertyOrder(700)]
    public Error[]? Errors { get; set; }
}