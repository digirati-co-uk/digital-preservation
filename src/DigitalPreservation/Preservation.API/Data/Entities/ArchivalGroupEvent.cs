// ReSharper disable EntityFramework.ModelValidation.UnlimitedStringLength
namespace Preservation.API.Data.Entities;

public class ArchivalGroupEvent
{
    /// <summary>
    /// Auto incrementing PK for ArchivalGroupEvent
    /// </summary>
    public int Id { get; set; }
    public required DateTime EventDate { get; set; }
    public required Uri ArchivalGroup  { get; set; }
    public Uri? ImportJobResult { get; set; }
    public string? FromVersion { get; set; }
    public string? ToVersion { get; set; }
    public bool Deleted { get; set; }

    /// <summary>
    /// Recorded, but not published in the Activity Stream. Set from the import job's
    /// SuppressActivityStreamEvent, for maintenance that changes how an object is recorded rather
    /// than what it holds.
    /// </summary>
    /// <remarks>
    /// The row is still written, and that matters for more than the audit trail: the stream reader
    /// takes its watermark from the latest event date, so skipping the row entirely would leave the
    /// watermark behind and make it re-read the same window of Storage activities for ever - which
    /// is exactly what a bulk migration, where every job is suppressed, would cause.
    /// </remarks>
    public bool Suppressed { get; set; }
}