using Microsoft.EntityFrameworkCore;
using Storage.API.Data.Entities;

namespace Storage.API.Data;

public class StorageContext : DbContext
{
    public DbSet<ImportJob> ImportJobs { get; set; }
    
    public DbSet<ExportResult> ExportResults { get; set; }
    
    public StorageContext(DbContextOptions<StorageContext> options) : base(options)
    {
    }
    
}