using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Transit.Extensions;

public class MetsExtensions
{
    [JsonPropertyName("physDivId")]
    [JsonPropertyOrder(101)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PhysDivId { get; set; }
    
    [JsonPropertyName("admId")]
    [JsonPropertyOrder(102)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AdmId { get; set; }
    
    [JsonPropertyName("rightsStatement")]
    [JsonPropertyOrder(103)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RightsStatement { get; set; }
    
    [JsonPropertyName("accessCondition")]
    [JsonPropertyOrder(104)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccessCondition { get; set; }
    
    [JsonPropertyName("originalPath")]
    [JsonPropertyOrder(201)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OriginalPath { get; set; }
    
    [JsonPropertyName("fileFormat")]
    [JsonPropertyOrder(202)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FileFormat? FileFormat { get; set; }
    
    [JsonPropertyName("virusScan")]
    [JsonPropertyOrder(203)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VirusScan? VirusScan { get; set; }
}