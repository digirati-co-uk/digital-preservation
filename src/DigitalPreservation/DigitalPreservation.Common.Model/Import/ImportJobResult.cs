using System.Text.Json.Serialization;

namespace DigitalPreservation.Common.Model.Import;

public class ImportJobResult : Resource
{
    public override string Type { get; set; } = nameof(ImportJobResult);
    
    /// <summary>
    /// A URI minted by the API which shows you the ImportJob submitted, for which this is the result. This is newly
    /// minted by the API when you actually submit an ImportJob, because: 1) not all Import Jobs are actually executed;
    /// 2) It may have been the special .../diff ImportJob; 3) It may have been an external identifier you provided.
    /// </summary>
    [JsonPropertyName("importJob")]
    [JsonPropertyOrder(60)]
    public required Uri ImportJob { get; set; }
    
    /// <summary>
    /// The id property of the original submitted job
    /// </summary>
    [JsonPropertyName("originalImportJob")]
    [JsonPropertyOrder(80)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Uri? OriginalImportJob { get; set; }
    
    /// <summary>
    /// Explicitly included for convenience; the deposit the job was started from.
    /// </summary>
    [JsonPropertyName("deposit")]
    [JsonPropertyOrder(100)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Uri? Deposit { get; set; }
    
    /// <summary>
    /// Also included for convenience, the repository object the changes specified in the job are being applied to
    /// </summary>
    [JsonPropertyName("archivalGroup")]
    [JsonPropertyOrder(510)]
    public required Uri ArchivalGroup { get; set; }
    
    /// <summary>
    /// One of ImportJobStates
    /// </summary>
    [JsonPropertyName("status")]
    [JsonPropertyOrder(520)]
    public required string Status { get; set; }
    
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
    /// The version of the DigitalObject this job caused to be produced. Not known until the job has finished processing
    /// </summary>
    [JsonPropertyName("newVersion")]
    [JsonPropertyOrder(650)]
    public string? NewVersion { get; set; }
    
    /// <summary>
    /// A list of errors encountered. These are error objects, not strings. 
    /// </summary>
    [JsonPropertyName("errors")]
    [JsonPropertyOrder(700)]
    public Error[]? Errors { get; set; }
    
    
    /// <summary>
    /// Populated once the job has finished successfully.
    /// </summary>
    [JsonPropertyName("containersAdded")]
    [JsonPropertyOrder(719)]
    public Container[] ContainersAdded { get; set; } = [];
    
    /// <summary>
    /// Populated once the job has finished successfully.
    /// </summary>
    [JsonPropertyName("binariesAdded")]
    [JsonPropertyOrder(720)]
    public Binary[] BinariesAdded { get; set; } = [];
    
    /// <summary>
    /// Populated once the job has finished successfully.
    /// </summary>
    [JsonPropertyOrder(721)]
    [JsonPropertyName("containersDeleted")]
    public Container[] ContainersDeleted { get; set; } = [];
    
    /// <summary>
    /// Populated once the job has finished successfully.
    /// </summary>
    [JsonPropertyOrder(722)]
    [JsonPropertyName("binariesDeleted")]
    public Binary[] BinariesDeleted { get; set; } = [];
    
    /// <summary>
    /// Populated once the job has finished successfully.
    /// </summary>
    [JsonPropertyOrder(723)]
    [JsonPropertyName("binariesPatched")]
    public Binary[] BinariesPatched { get; set; } = [];
    
    /// <summary>
    /// Populated once the job has finished successfully.
    /// </summary>
    [JsonPropertyOrder(724)]
    [JsonPropertyName("binariesRenamed")]
    public Binary[] BinariesRenamed { get; set; } = [];
    
    /// <summary>
    /// Populated once the job has finished successfully.
    /// </summary>
    [JsonPropertyOrder(725)]
    [JsonPropertyName("containersRenamed")]
    public Container[] ContainersRenamed { get; set; } = [];
}

public class Error
{
    /// <summary>
    /// Id directly to this version
    /// </summary>
    [JsonPropertyName("id")]
    [JsonPropertyOrder(1)]
    public Uri? Id { get; set; }
    
    /// <summary>
    /// 
    /// </summary>
    public required string Message { get; set; }
}