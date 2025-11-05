// ReSharper disable EntityFramework.ModelValidation.UnlimitedStringLength
using DigitalPreservation.Common.Model.Import;
using DigitalPreservation.Common.Model.PipelineApi;

namespace Preservation.API.Data.Entities;

/// <summary>
/// As an entity in the db, we don't have a separate ImportJob and ImportJobResult
/// </summary>
public class PipelineRunJob
{
    /// <summary>
    /// Minted
    /// </summary>
    public required string Id { get; set; }
    
    public required string Deposit { get; set; }
    
    public required string? ArchivalGroup { get; set; }
    
    public string Status { get; set; } = PipelineJobStates.Waiting;
    
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
    public required string PipelineJobJson { get; set; }

    public string? RunUser { get; set; }
    public string? Errors { get; set; }
}