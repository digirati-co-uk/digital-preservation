using Microsoft.EntityFrameworkCore;
using Preservation.API.Data.Entities;

// ReSharper disable ClassNeverInstantiated.Global

namespace Preservation.API.Data;

public class PreservationContext : DbContext
{
    public DbSet<Deposit> Deposits { get; set; }
    public DbSet<ImportJob> ImportJobs { get; set; }
    public DbSet<ArchivalGroupEvent> ArchivalGroupEvents { get; set; }
    public DbSet<PipelineRunJob> PipelineRunJobs { get; set; }
    public DbSet<DepositArchiveJob> DepositArchiveJobs { get; set; }
    public PreservationContext(DbContextOptions<PreservationContext> options) : base(options)
    {
    }

    public ImportJob? GetImportJobFromStorageImportJobResult(Uri storageResultUri)
    {
        return ImportJobs.SingleOrDefault(j => j.StorageImportJobResultId == storageResultUri);
    }

    /// <summary>
    /// The most recent Archival Group event, which is how far we have read Storage API's own import
    /// job activities. <b>Deliberately includes suppressed events</b>: suppression keeps an event out
    /// of the PUBLISHED stream, not out of this reckoning.
    /// </summary>
    /// <remarks>
    /// Filtering suppressed events out here would leave the watermark behind whenever the newest
    /// event is a suppressed one, and StorageImportJobsProcessor would re-read the same window of
    /// Storage activities on every pass, for ever. A bulk migration, where every job is suppressed,
    /// would cause exactly that - which is why the event row is written at all rather than skipped.
    /// </remarks>
    public ArchivalGroupEvent? GetLatestArchivalGroupEvent()
    {
        return ArchivalGroupEvents.OrderByDescending(e => e.EventDate).FirstOrDefault();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Deposit>(builder =>
        {
            builder
                .Property(su => su.Created)
                .HasDefaultValueSql("now()");
        });

        // We need a row in this table to provide a "last checked" date for activity streams
        modelBuilder.Entity<ArchivalGroupEvent>().HasData(
            new ArchivalGroupEvent
            {
                Id = -1,
                EventDate = new DateTime(2024, 1, 1).ToUniversalTime(),
                ArchivalGroup = new Uri("https://example.com/archival-group") 
            });
    }
}