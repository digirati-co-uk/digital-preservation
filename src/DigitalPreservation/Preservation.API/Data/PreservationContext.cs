using Microsoft.EntityFrameworkCore;
using Preservation.API.Data.Entities;

// ReSharper disable ClassNeverInstantiated.Global

namespace Preservation.API.Data;

public class PreservationContext : DbContext
{
    public DbSet<Deposit> Deposits { get; set; }
    public DbSet<ImportJob> ImportJobs { get; set; }
    public DbSet<ArchivalGroupEvent> ArchivalGroupEvents { get; set; }
    
    public PreservationContext(DbContextOptions<PreservationContext> options) : base(options)
    {
    }

    public ImportJob? GetImportJobFromStorageImportJobResult(Uri storageResultUri)
    {
        return ImportJobs.SingleOrDefault(j => j.StorageImportJobResultId == storageResultUri);
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