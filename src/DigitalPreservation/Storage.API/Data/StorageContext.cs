using Microsoft.EntityFrameworkCore;
using Storage.API.Data.Entities;

namespace Storage.API.Data;

public class StorageContext : DbContext
{
    public DbSet<ImportJob> ImportJobs { get; set; }
    
    public DbSet<Export> ExportResults { get; set; }
    
    public StorageContext(DbContextOptions<StorageContext> options) : base(options)
    {
    }

    /// <summary>
    /// Must match the index name in <see cref="OnModelCreating"/>: the store recognises a violation
    /// of this index by name (issue #460).
    /// </summary>
    public const string OneActiveImportJobPerArchivalGroupIndexName = "ix_import_jobs_archival_group_one_active";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Issue #460: QueueImportJobHandler checks for an active job and then inserts, so two
        // requests arriving together could both pass the check and both queue. This index makes a
        // second active row for the same Archival Group impossible; the store turns the violation
        // into the same 409 the check gives. The same pattern as the pipeline's in #353.
        modelBuilder.Entity<ImportJob>()
            .HasIndex(ij => ij.ArchivalGroup)
            .IsUnique()
            .HasDatabaseName(OneActiveImportJobPerArchivalGroupIndexName)
            .HasFilter("active");
    }
    
}