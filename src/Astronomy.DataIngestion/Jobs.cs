using System.Globalization;
using Astronomy.Infrastructure;
using Astronomy.Infrastructure.Registry;
using Astronomy.SharedKernel.Datasets;
using Astronomy.SharedKernel.Time;

namespace Astronomy.DataIngestion;

public static class Jobs
{
    private const string EopSourceUrl = "https://maia.usno.navy.mil/ser7/ser7.dat";

    private static readonly string[] EopC04CandidateUrls =
    {
        Environment.GetEnvironmentVariable("IERS_C04_URL") ?? "",
        "https://datacenter.iers.org/data/latestVersion/5_BULLETIN_C04_IAU2000_TS_EOP.txt",
        "https://datacenter.iers.org/products/eop/long-term/c04_IAU2000.txt",
    };

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

    public static async Task<int> RunEopC04JobAsync(string dbPath, string dataRoot)
    {
        using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        string? text = null;
        foreach (var url in EopC04CandidateUrls)
        {
            if (string.IsNullOrWhiteSpace(url)) continue;
            try
            {
                text = await hc.GetStringAsync(url);
                Console.WriteLine($"eop-c04: fetched {url} ({text.Length} chars)");
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"eop-c04: {url} FAIL {ex.Message.Split('\n')[0]}");
            }
        }
        if (text is null)
        {
            Console.WriteLine("eop-c04: all sources unreachable");
            return 1;
        }

        var samples = new List<(double Mjd, double Ut1MinusUtc)>();
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 7) continue;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)) continue;
            if (year is < 1970 or > 2100) continue;
            if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var mjd)) continue;
            if (mjd is < 40000 or > 80000) continue;
            if (!double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var dut1)) continue;
            samples.Add((mjd, dut1));
        }
        if (samples.Count < 1000)
        {
            Console.WriteLine($"eop-c04: REJECTED - only {samples.Count} samples parsed");
            var head = text[..Math.Min(400, text.Length)].Replace('\n', ' ');
            Console.WriteLine($"eop-c04: head: {head}");
            return 1;
        }
        if (Math.Abs(samples[^1].Ut1MinusUtc) > 2.0)
        {
            Console.WriteLine($"eop-c04: REJECTED - implausible latest UT1-UTC {samples[^1].Ut1MinusUtc:F4}");
            return 1;
        }

        var version = DateTime.UtcNow.ToString("yyyyMMdd");
        var dir = Path.Combine(dataRoot, "datasets", "eop-c04", version);
        Directory.CreateDirectory(dir);
        var csv = new List<string> { "mjd,ut1_minus_utc_seconds" };
        csv.AddRange(samples.Select(s => $"{s.Mjd.ToString("F3", CultureInfo.InvariantCulture)},{s.Ut1MinusUtc.ToString("F7", CultureInfo.InvariantCulture)}"));
        await File.WriteAllLinesAsync(Path.Combine(dir, "eop-c04.csv"), csv);
        var checksum = Sha256(string.Join('\n', csv));

        var registry = new DatasetRegistry(() => InfrastructureRegistrar.CreateRegistryContext(dbPath));
        await registry.StageAsync("eop-c04", version, checksum);
        await registry.ActivateAsync("eop-c04", version);
        Console.WriteLine($"eop-c04: {samples.Count} samples staged+activated as {version} (latest UT1-UTC {samples[^1].Ut1MinusUtc:F4}s)");
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
