using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Astronomy.Modules.Satellites.Application;

public class SatelliteElementRecord
{
    public int Id { get; set; }
    public required string DatasetVersion { get; set; }
    public required string NoradId { get; set; }
    public required string ObjectName { get; set; }
    public required string EpochUtc { get; set; }
    public required string ElementsJson { get; set; }
}

public class SatelliteDbContext : DbContext
{
    public SatelliteDbContext(DbContextOptions<SatelliteDbContext> options) : base(options) { }

    public DbSet<SatelliteElementRecord> Elements => Set<SatelliteElementRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SatelliteElementRecord>()
            .HasIndex(e => new { e.DatasetVersion, e.NoradId })
            .IsUnique();
    }
}

public class SatelliteDesignFactory : IDesignTimeDbContextFactory<SatelliteDbContext>
{
    public SatelliteDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SatelliteDbContext>()
            .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), "satellite-design.db")}")
            .Options;
        return new SatelliteDbContext(options);
    }
}

public static class SatelliteStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    internal static SatelliteDbContext CreateContext(string dbPath)
    {
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
            cmd.ExecuteNonQuery();
        }
        var options = new DbContextOptionsBuilder<SatelliteDbContext>().UseSqlite(conn).Options;
        return new SatelliteDbContext(options);
    }

    public static void EnsureSchema(string dbPath)
    {
        using var ctx = CreateContext(dbPath);
        ctx.Database.Migrate();
    }

    public static void WriteElements(string dbPath, string version, IReadOnlyList<OrbitalElementRow> rows)
    {
        using var ctx = CreateContext(dbPath);
        var existing = ctx.Elements.Where(e => e.DatasetVersion == version).ToList();
        ctx.Elements.RemoveRange(existing);
        ctx.Elements.AddRange(rows.Select(r => new SatelliteElementRecord
        {
            DatasetVersion = version,
            NoradId = r.NoradId,
            ObjectName = r.Name,
            EpochUtc = r.EpochUtc.ToString("O"),
            ElementsJson = ToJson(r),
        }));
        ctx.SaveChanges();
    }

    public static IReadOnlyList<OrbitalElementRow> ReadElements(string dbPath)
    {
        using var ctx = CreateContext(dbPath);
        var rows = ctx.Elements.AsNoTracking().ToList();
        return rows.Select(FromJson).ToList();
    }

    public static IReadOnlyList<OrbitalElementRow> ReadElements(string dbPath, string datasetVersion)
    {
        using var ctx = CreateContext(dbPath);
        var rows = ctx.Elements.AsNoTracking().Where(e => e.DatasetVersion == datasetVersion).ToList();
        return rows.Select(FromJson).ToList();
    }

    public static (int Fresh, int Warn, int Degraded, int Refuse) Freshness(IReadOnlyList<OrbitalElementRow> rows, DateTimeOffset now)
    {
        var fresh = 0; var warn = 0; var degraded = 0; var refuse = 0;
        foreach (var r in rows)
        {
            var age = (now - r.EpochUtc).TotalHours;
            if (age < 24) fresh++;
            else if (age < 72) warn++;
            else if (age < 168) degraded++;
            else refuse++;
        }
        return (fresh, warn, degraded, refuse);
    }

    internal static string ToJson(OrbitalElementRow r) => JsonSerializer.Serialize(r, JsonOptions);

    private static OrbitalElementRow FromJson(SatelliteElementRecord record)
    {
        using var probe = JsonDocument.Parse(record.ElementsJson);
        // Rows staged before the STJ format used short keys ("mm", "norad", ...);
        // STJ's constructor binding would silently default those, so detect them
        // explicitly and parse the legacy shape.
        if (probe.RootElement.TryGetProperty("mm", out _))
            return FromLegacyJson(probe);
        return JsonSerializer.Deserialize<OrbitalElementRow>(record.ElementsJson, JsonOptions)
            ?? throw new FormatException("null elements json");
    }

    private static OrbitalElementRow FromLegacyJson(JsonDocument doc)
    {
        var r = doc.RootElement;
        return new OrbitalElementRow(
            r.GetProperty("name").GetString()!, r.GetProperty("norad").GetString()!,
            DateTimeOffset.Parse(r.GetProperty("epoch").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            r.GetProperty("mm").GetDouble(), r.GetProperty("ecc").GetDouble(), r.GetProperty("incl").GetDouble(),
            r.GetProperty("raan").GetDouble(), r.GetProperty("argp").GetDouble(), r.GetProperty("ma").GetDouble(),
            r.GetProperty("bstar").GetDouble(), r.GetProperty("mmdot").GetDouble(), r.GetProperty("mmddot").GetDouble(),
            r.GetProperty("rev").GetInt32());
    }
}
