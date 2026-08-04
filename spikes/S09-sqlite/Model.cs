using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace S09Sqlite;

public class Dataset
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Version { get; set; }
    public required string Status { get; set; }
    public DateTime ActivatedAtUtc { get; set; }
    public required string Checksum { get; set; }
}

public class SatelliteElement
{
    public int Id { get; set; }
    public required string NoradId { get; set; }
    public DateTime EpochUtc { get; set; }
    public required string DatasetVersion { get; set; }
    public required string ElementsJson { get; set; }
    public string? Source { get; set; }
}

public class AuditEntry
{
    public int Id { get; set; }
    public required string Action { get; set; }
    public required string Detail { get; set; }
    public DateTime AtUtc { get; set; }
}

public class DrillDbContext : DbContext
{
    public DrillDbContext(DbContextOptions<DrillDbContext> options) : base(options) { }

    public DbSet<Dataset> Datasets => Set<Dataset>();
    public DbSet<SatelliteElement> SatelliteElements => Set<SatelliteElement>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
}

public class DesignFactory : IDesignTimeDbContextFactory<DrillDbContext>
{
    public DrillDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DrillDbContext>()
            .UseSqlite("Data Source=drill.db").Options;
        return new DrillDbContext(options);
    }
}
