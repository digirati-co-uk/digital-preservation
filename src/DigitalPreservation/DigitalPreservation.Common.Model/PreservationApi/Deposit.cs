using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.PreservationApi;

public class Deposit : Resource
{
    [JsonPropertyOrder(2)]
    [JsonPropertyName("type")]
    public override string Type { get; set; } = nameof(Deposit); 
    
    [JsonPropertyOrder(110)]
    [JsonPropertyName("archivalGroup")] // aka digitalObject
    public Uri? ArchivalGroup { get; set; }
    
    [JsonPropertyOrder(115)]
    [JsonPropertyName("archivalGroupExists")]
    public bool ArchivalGroupExists { get; set; }
    
    [JsonPropertyOrder(120)]
    [JsonPropertyName("files")] 
    public Uri? Files { get; set; }

    [JsonPropertyOrder(130)]
    [JsonPropertyName("status")]
    public string Status { get; set; } = DepositStates.New;
    
    [JsonPropertyOrder(140)]
    [JsonPropertyName("submissionText")]
    public string? SubmissionText { get; set; }
    
    [JsonPropertyOrder(150)]
    [JsonPropertyName("archivalGroupName")]
    public string? ArchivalGroupName { get; set; }
    
    [JsonPropertyOrder(160)]
    [JsonPropertyName("active")]
    public bool Active { get; set; }
    
    
    [JsonPropertyOrder(210)]
    [JsonPropertyName("preserved")]
    public DateTime? Preserved { get; set; }  // if not null can't be reused?
    
    [JsonPropertyOrder(220)]
    [JsonPropertyName("preservedBy")]
    public Uri? PreservedBy { get; set; }
    
    [JsonPropertyOrder(230)]
    [JsonPropertyName("versionPreserved")]
    public string? VersionPreserved { get; set; }
    
    [JsonPropertyOrder(250)]
    [JsonPropertyName("exported")]
    public DateTime? Exported { get; set; }
    
    [JsonPropertyOrder(260)]
    [JsonPropertyName("exportedBy")]
    public Uri? ExportedBy { get; set; }
    
    [JsonPropertyOrder(270)]
    [JsonPropertyName("versionExported")]
    public string? VersionExported { get; set; }
    
    [JsonPropertyOrder(500)]
    [JsonPropertyName("useObjectTemplate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? UseObjectTemplate { get; set; }
    
    /// <summary>
    /// At the time the deposit is requested
    /// As the METS is part of the deposit, some clients may update this without requesting the deposit again.
    /// </summary>
    [JsonPropertyOrder(600)]
    [JsonPropertyName("metsETag")]
    public string? MetsETag { get; set; }
    

    public const string BasePathElement = "deposits";

}

public static class DepositStates
{
    public const string New = "new";
    public const string Exporting = "exporting";
    public const string Preserved = "preserved";
    public const string Error = "error";

    public static readonly string[] All = [New, Exporting, Preserved, Error];
}