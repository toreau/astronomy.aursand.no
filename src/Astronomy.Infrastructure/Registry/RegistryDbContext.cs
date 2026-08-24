using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Astronomy.Infrastructure.Registry;

public class DatasetRecord
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Version { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset ActivatedAtUtc { get; set; }
    public required string Checksum { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public class ActiveDataset
{
    public required string Name { get; set; }
    public required string Version { get; set; }
    public DateTimeOffset ActivatedAtUtc { get; set; }
}

public class AuditEntry
{
    public int Id { get; set; }
    public required string Action { get; set; }
    public required string Detail { get; set; }
    public DateTimeOffset AtUtc { get; set; }
}

public class RegistryDbContext : DbContext
{
    public RegistryDbContext(DbContextOptions<RegistryDbContext> options) : base(options) { }

    public DbSet<DatasetRecord> Datasets => Set<DatasetRecord>();
    public DbSet<ActiveDataset> ActiveDatasets => Set<ActiveDataset>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DatasetRecord>()
            .HasIndex(d => new { d.Name, d.Version }).IsUnique();
        modelBuilder.Entity<ActiveDataset>()
            .HasKey(a => a.Name);
    }
}

public class RegistryDesignFactory : IDesignTimeDbContextFactory<RegistryDbContext>
{
    // Migrations are Postgres-owned (Option A): always design against Npgsql so a
    // `dotnet ef` run can never silently generate a SQLite migration. The connection
    // string is a design-time placeholder - `migrations add` never opens it.
    public RegistryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<RegistryDbContext>()
            .UseNpgsql("Host=localhost;Database=astronomy-design;Username=astronomy;Password=astronomy")
            .Options;
        return new RegistryDbContext(options);
    }
}
