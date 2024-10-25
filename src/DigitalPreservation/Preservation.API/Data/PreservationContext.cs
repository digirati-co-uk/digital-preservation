using Microsoft.EntityFrameworkCore;
using Preservation.API.Data.Entities;

// ReSharper disable ClassNeverInstantiated.Global

namespace Preservation.API.Data;

public class PreservationContext : DbContext
{
    public DbSet<Deposit> Deposits { get; set; }
    public DbSet<ImportJob> ImportJobs { get; set; }
    
    public PreservationContext(DbContextOptions<PreservationContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Deposit>(builder =>
        {
            builder
                .Property(su => su.Created)
                .HasDefaultValueSql("now()");
        });
    }
}