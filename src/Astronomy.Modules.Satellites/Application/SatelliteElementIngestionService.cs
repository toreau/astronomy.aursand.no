using System.Globalization;
using Astronomy.SharedKernel.Datasets;

namespace Astronomy.Modules.Satellites.Application;

internal sealed class SatelliteElementIngestionService : ISatelliteElementIngestionService
{
    private const string CelesTrakOmm = "https://celestrak.org/NORAD/elements/gp.php?GROUP=stations&FORMAT=omm";
    private const string DatasetName = "satellite-elements";
    private const int MinimumRowCount = 10;

    private readonly string _dbPath;
    private readonly IDatasetRegistry _registry;

    public SatelliteElementIngestionService(string dbPath, IDatasetRegistry registry)
    {
        _dbPath = dbPath;
        _registry = registry;
        SatelliteStore.EnsureSchema(dbPath);
    }

    public async Task<int> FetchAndStageAsync(string version, CancellationToken ct = default)
    {
        using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var payload = await hc.GetStringAsync(CelesTrakOmm, ct);
        var rows = ParseCsv(payload);
        if (RejectPayload(rows)) return 1;
        var errors = Validate(rows, DateTimeOffset.UtcNow);
        if (errors.Count > 0)
        {
            Console.WriteLine($"omm: REJECTED - {errors.Count} violations (first 5):");
            foreach (var (row, field, value) in errors.Take(5))
                Console.WriteLine($"  row {row} [{field}] = {value}");
            return 1;
        }
        await Stage(version, rows);
        Console.WriteLine($"omm: staged {rows.Count} rows as {version}");
        return 0;
    }

    public async Task<int> StageFileAsync(string version, string csvPath, CancellationToken ct = default)
    {
        var payload = await File.ReadAllTextAsync(csvPath, ct);
        var rows = ParseCsv(payload);
        if (RejectPayload(rows)) return 1;
        var errors = Validate(rows, DateTimeOffset.UtcNow);
        if (errors.Count > 0)
        {
            Console.WriteLine($"omm: REJECTED - {errors.Count} violations (first 5):");
            foreach (var (row, field, value) in errors.Take(5))
                Console.WriteLine($"  row {row} [{field}] = {value}");
            return 1;
        }
        await Stage(version, rows);
        Console.WriteLine($"omm: staged {rows.Count} rows as {version}");
        return 0;
    }

    /// <summary>Payload-level guard: a truncated/empty response must never be staged.</summary>
    private static bool RejectPayload(IReadOnlyList<OrbitalElementRow> rows)
    {
        if (rows.Count >= MinimumRowCount) return false;
        Console.WriteLine($"omm: REJECTED - only {rows.Count} rows (min {MinimumRowCount})");
        return true;
    }

    public Task<int> ActivateAsync(string version, CancellationToken ct = default) =>
        _registry.ActivateAsync(DatasetName, version, ct);

    public Task<int> RollbackAsync(string version, CancellationToken ct = default) =>
        _registry.RollbackAsync(DatasetName, version, ct);

    public async Task<IngestionStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var active = _registry.ActiveVersion(DatasetName);
        var rows = active is null ? [] : SatelliteStore.ReadElements(_dbPath, active.Version);
        var (fresh, warn, degraded, refuse) = SatelliteStore.Freshness(rows, DateTimeOffset.UtcNow);
        return new IngestionStatus(active?.Version, rows.Count, fresh, warn, degraded, refuse);
    }

    private async Task Stage(string version, IReadOnlyList<OrbitalElementRow> rows)
    {
        var checksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join('\n', rows.Select(r => SatelliteStore.ToJson(r)))))).ToLowerInvariant();
        await _registry.StageAsync(DatasetName, version, checksum);
        SatelliteStore.WriteElements(_dbPath, version, rows);
    }

    internal static List<OrbitalElementRow> ParseCsv(string csv)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) throw new FormatException("empty or header-only OMM payload");
        var rows = new List<OrbitalElementRow>();
        foreach (var line in lines.Skip(1))
        {
            var p = line.Split(',');
            if (p.Length < 17) continue;
            var epoch = DateTimeOffset.ParseExact(p[2].Trim(), "yyyy-MM-ddTHH:mm:ss.ffffff",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            rows.Add(new OrbitalElementRow(
                p[0].Trim(), p[11].Trim(), epoch,
                D(p[3]), D(p[4]), D(p[5]), D(p[6]), D(p[7]), D(p[8]),
                D(p[14]), D(p[15]), D(p[16]),
                int.TryParse(p[13].Trim(), out var rev) ? rev : 0));
        }
        return rows;

        static double D(string s) => double.Parse(s.Trim(), CultureInfo.InvariantCulture);
    }

    internal static List<ElementValidationError> Validate(IReadOnlyList<OrbitalElementRow> rows, DateTimeOffset nowUtc)
    {
        var errors = new List<ElementValidationError>();
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.NoradId.Length is < 5 or > 6 || !r.NoradId.All(char.IsDigit))
                errors.Add(new(i, "norad", r.NoradId));
            var age = (nowUtc - r.EpochUtc).TotalHours;
            if (age < -2 || age > 48)
                errors.Add(new(i, "epoch", $"{r.EpochUtc:O} (age {age:F1}h)"));
            if (r.MeanMotion is < 0.05 or > 17.0)
                errors.Add(new(i, "mean_motion", r.MeanMotion.ToString(CultureInfo.InvariantCulture)));
            if (r.Eccentricity is < 0.0 or > 0.9)
                errors.Add(new(i, "eccentricity", r.Eccentricity.ToString(CultureInfo.InvariantCulture)));
            if (r.Inclination is < 0.0 or > 180.0)
                errors.Add(new(i, "inclination", r.Inclination.ToString(CultureInfo.InvariantCulture)));
            if (Math.Abs(r.Bstar) > 0.02)
                errors.Add(new(i, "bstar", r.Bstar.ToString(CultureInfo.InvariantCulture)));
            if (r.RaOfAscNode is < 0.0 or >= 360.0 || r.ArgOfPericenter is < 0.0 or >= 360.0 || r.MeanAnomaly is < 0.0 or >= 360.0)
                errors.Add(new(i, "angles", $"{r.RaOfAscNode:F2}/{r.ArgOfPericenter:F2}/{r.MeanAnomaly:F2}"));
        }
        return errors;
    }
}
