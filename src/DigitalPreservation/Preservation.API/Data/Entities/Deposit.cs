// ReSharper disable EntityFramework.ModelValidation.UnlimitedStringLength

using System.ComponentModel.DataAnnotations.Schema;

namespace Preservation.API.Data.Entities;

/// <summary>
/// Represents a Deposit made to Preservation service
/// </summary>
public class Deposit
{
    /// <summary>
    /// Auto incrementing PK for deposit
    /// </summary>
    public int Id { get; set; }
    
    /// <summary>
    /// Typically externally provided identifier that forms part of URL for deposit and S3 files location
    /// </summary>
    public required string MintedId { get; set; } 
    
    /// <summary>
    /// The intended Archival Group that this deposit will make (new), or is for (existing)
    /// </summary>
    public string? ArchivalGroupPathUnderRoot { get; set; }
    public string? ArchivalGroupProposedName { get; set; }
    public string Status { get; set; } = "new";  // exported / preserved
    public string? SubmissionText { get; set; }
    
    /// <summary>
    /// The dep
    /// </summary>
    public bool Active { get; set; } // name? If not active its files in S3 have been deleted
    
    /// <summary>
    /// Created timestamp
    /// </summary>
    public DateTime CreatedOn { get; set; }
    public required string CreatedBy { get; set; }
    public DateTime? LastModified { get; set; }
    public string? LastModifiedBy { get; set; }
    public DateTime? Preserved { get; set; }  // if not null can't be reused?
    public string? PreservedBy { get; set; }
    public string? VersionPreserved { get; set; }
    public DateTime? Exported { get; set; }
    public string? ExportedBy { get; set; }
    public string? VersionExported { get; set; }
}