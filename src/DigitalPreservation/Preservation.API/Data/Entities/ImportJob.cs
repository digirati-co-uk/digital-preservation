// ReSharper disable EntityFramework.ModelValidation.UnlimitedStringLength
using DigitalPreservation.Common.Model.Import;

namespace Preservation.API.Data.Entities;

/// <summary>
/// As an entity in the db, we don't have a separate ImportJob and ImportJobResult
/// </summary>
public class ImportJob
{
    /// <summary>
    /// Minted
    /// </summary>
    public required string Id { get; set; }
    
    public required Uri StorageImportJobResultId { get; set; }
    
    public required string Deposit { get; set; }
    
    public required Uri ArchivalGroup { get; set; }
    
    public string Status { get; set; } = ImportJobStates.Waiting;
    
    public required DateTime LastUpdated { get; set; }
    
    /// <summary>
    /// When the job was submitted to API
    /// </summary>
    public DateTime? DateSubmitted { get; set; }
    
    /// <summary>
    /// When the API started processing the job
    /// </summary>
    public DateTime? DateBegun { get; set; }
    
    /// <summary>
    /// When the API finished processing the job
    /// </summary>
    public DateTime? DateFinished { get; set; }
    
    
    /// <summary>
    /// Copy of JSON used to initiate this import job
    /// </summary>
    public required string ImportJobJson { get; set; }
    
    public required string LatestStorageApiResultJson { get; set; }
    
    /// <summary>
    /// If the job has completed then we simply return this to callers
    /// </summary>
    public required string LatestPreservationApiResultJson { get; set; }
    
    // This populates the RESULT
    
    /// <summary>
    /// The version of the DigitalObject the job is to be applied to
    /// </summary>
    public string? SourceVersion { get; set; }
    
    /// <summary>
    /// The version of the DigitalObject this job caused to be produced
    /// </summary>
    public string? NewVersion { get; set; }
    
    public string? Errors { get; set; }
    // public string? ContainersAdded { get; set; }
    // public string? BinariesAdded { get; set; }
    // public string? ContainersDeleted { get; set; }
    // public string? BinariesDeleted { get; set; }
    // public string? BinariesPatched { get; set; }
    //
    //
    // public string? ContainersRenamed { get; set; }
    // public string? BinariesRenamed { get; set; }
}