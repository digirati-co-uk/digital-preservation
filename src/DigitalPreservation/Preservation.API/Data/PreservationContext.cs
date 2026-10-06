using Microsoft.EntityFrameworkCore;
using Preservation.API.Data.Entities;

// ReSharper disable ClassNeverInstantiated.Global

namespace Preservation.API.Data;

public class PreservationContext : DbContext
{
    /// <summary>
    /// Not a real Archival Group: the ArchivalGroup of the seeded watermark row, which is
    /// suppressed so it never appears in the published stream (issue #269).
    /// </summary>
    private const string SeedWatermarkArchivalGroup = "https://example.com/archival-group";

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

    /// <summary>
    /// The Archival Group events that appear in the published Activity Stream - everything except
    /// the ones a maintenance import job asked to be kept out of it.
    /// </summary>
    /// <remarks>
    /// One definition rather than one per query. The collection's count and the page's count and
    /// its page of rows decide the stream's page boundaries between them, so a filter applied to
    /// some of them and not the others puts a "next" link on the last page or leaves one off. It is
    /// the opposite of <see cref="GetLatestArchivalGroupEvent"/>, which must see suppressed events;
    /// the two together are the whole of what suppression means.
    /// </remarks>
    public IQueryable<ArchivalGroupEvent> PublishedArchivalGroupEvents() =>
        ArchivalGroupEvents.Where(e => !e.Suppressed);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Deposit>(builder =>
        {
            builder
                .Property(su => su.Created)
                .HasDefaultValueSql("now()");
        });

        // A row in this table provides GetLatestArchivalGroupEvent's "last checked" watermark for
        // the Storage-stream reader on an otherwise-empty table. Suppressed so it never appears in
        // the published stream itself (issue #269) - PublishedArchivalGroupEvents excludes it, but
        // GetLatestArchivalGroupEvent deliberately still sees it.
        modelBuilder.Entity<ArchivalGroupEvent>().HasData(
            new ArchivalGroupEvent
            {
                Id = -1,
                EventDate = new DateTime(2024, 1, 1).ToUniversalTime(),
                ArchivalGroup = new Uri(SeedWatermarkArchivalGroup),
                Suppressed = true
            });
    }
}