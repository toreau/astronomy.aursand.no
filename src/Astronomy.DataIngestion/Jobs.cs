using System.Globalization;
using Astronomy.Infrastructure;
using Astronomy.Infrastructure.Registry;
using Astronomy.SharedKernel.Datasets;
using Astronomy.SharedKernel.Time;

namespace Astronomy.DataIngestion;

public static class Jobs
{
    private const string EopSourceUrl = "https://maia.usno.navy.mil/ser7/ser7.dat";

    public static async Task<int> RunEopJobAsync(string dbPath, string dataRoot)
    {
        using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var text = await hc.GetStringAsync(EopSourceUrl);
        var samples = new List<(double Mjd, double Ut1MinusUtc)>();
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var mjd)) continue;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var dut1)) continue;
            samples.Add((mjd, dut1));
        }
        if (samples.Count < 100)
        {
            Console.WriteLine($"eop: REJECTED - only {samples.Count} samples parsed from ser7");
            return 1;
        }
        if (Math.Abs(samples[^1].Ut1MinusUtc) > 2.0)
        {
            Console.WriteLine($"eop: REJECTED - implausible latest UT1-UTC {samples[^1].Ut1MinusUtc:F4}");
            return 1;
        }

        var version = DateTime.UtcNow.ToString("yyyyMMdd");
        var dir = Path.Combine(dataRoot, "datasets", "eop-ut1", version);
        Directory.CreateDirectory(dir);
        var csv = new List<string> { "mjd,ut1_minus_utc_seconds" };
        csv.AddRange(samples.Select(s => $"{s.Mjd.ToString("F3", CultureInfo.InvariantCulture)},{s.Ut1MinusUtc.ToString("F7", CultureInfo.InvariantCulture)}"));
        await File.WriteAllLinesAsync(Path.Combine(dir, "eop-ut1.csv"), csv);
        var checksum = Sha256(string.Join('\n', csv));

        var registry = new DatasetRegistry(() => InfrastructureRegistrar.CreateRegistryContext(dbPath));
        await registry.StageAsync("eop-ut1", version, checksum);
        await registry.ActivateAsync("eop-ut1", version);
        Console.WriteLine($"eop: {samples.Count} samples staged+activated as {version} (latest UT1-UTC {samples[^1].Ut1MinusUtc:F4}s)");
        return 0;
    }

    public static async Task<int> RunLeapSecondsJobAsync(string dbPath, string dataRoot)
    {
        var table = LeapSecondTable.Default;
        var version = table.DatasetVersion;
        var dir = Path.Combine(dataRoot, "datasets", "leap-seconds", version);
        Directory.CreateDirectory(dir);
        var csv = new List<string> { "effective_utc,tai_minus_utc" };
        csv.AddRange(table.Entries.Select(e => $"{e.EffectiveUtc:yyyy-MM-ddTHH:mm:ssZ},{e.TaiMinusUtc}"));
        await File.WriteAllLinesAsync(Path.Combine(dir, "leap-seconds.csv"), csv);
        var checksum = Sha256(string.Join('\n', csv));

        var registry = new DatasetRegistry(() => InfrastructureRegistrar.CreateRegistryContext(dbPath));
        await registry.StageAsync("leap-seconds", version, checksum);
        await registry.ActivateAsync("leap-seconds", version);
        Console.WriteLine($"leap-seconds: {table.Entries.Count} entries staged+activated as {version}");
        return 0;
    }

    private static string Sha256(string s) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}
