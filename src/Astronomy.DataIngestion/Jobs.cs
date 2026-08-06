using System.Globalization;
using System.IO.Compression;
using Astronomy.Infrastructure;
using Astronomy.Infrastructure.Registry;
using Astronomy.Modules.Satellites.Application;
using Astronomy.SharedKernel.Datasets;
using Astronomy.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.DataIngestion;

public static class Jobs
{
    private const string EopSourceUrl = "https://maia.usno.navy.mil/ser7/ser7.dat";

    private const string LeapSecondsUrl = "https://hpiers.obspm.fr/iers/bul/bulc/ntp/leap-seconds.list";

    private static readonly string[] EopC04CandidateUrls =
    {
        Environment.GetEnvironmentVariable("IERS_C04_URL") ?? "",
        "https://datacenter.iers.org/data/latestVersion/224_EOP_C04_14.62-NOW.IAU2000A224.txt",
        "https://datacenter.iers.org/data/latestVersion/221_EOP_C04_14.XX.IAU2000A221.txt",
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

        var samples = new List<(double Mjd, double Ut1MinusUtc, double X, double Y)>();
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 7) continue;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)) continue;
            if (year is < 1970 or > 2100) continue;
            if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var mjd)) continue;
            if (mjd is < 40000 or > 80000) continue;
            if (!double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out var dut1)) continue;
            var x = 0.0;
            var y = 0.0;
            if (parts.Length > 7 && double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var xv)) x = xv;
            if (parts.Length > 8 && double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var yv)) y = yv;
            samples.Add((mjd, dut1, x, y));
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
        var csv = new List<string> { "mjd,ut1_minus_utc_seconds,x_arcsec,y_arcsec" };
        csv.AddRange(samples.Select(s => $"{s.Mjd.ToString("F3", CultureInfo.InvariantCulture)},{s.Ut1MinusUtc.ToString("F7", CultureInfo.InvariantCulture)},{s.X.ToString("F6", CultureInfo.InvariantCulture)},{s.Y.ToString("F6", CultureInfo.InvariantCulture)}"));
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
        using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        string text;
        try
        {
            text = await hc.GetStringAsync(LeapSecondsUrl);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"leap-seconds: fetch FAIL {ex.Message.Split('\n')[0]} (keeping current dataset)");
            return 1;
        }

        var entries = ParseLeapSecondsFile(text).OrderBy(e => e.EffectiveUtc).ToList();
        if (entries.Count < 20)
        {
            Console.WriteLine($"leap-seconds: REJECTED - only {entries.Count} entries parsed");
            return 1;
        }
        for (var i = 1; i < entries.Count; i++)
        {
            if (entries[i].EffectiveUtc <= entries[i - 1].EffectiveUtc)
            {
                Console.WriteLine("leap-seconds: REJECTED - non-monotonic effective dates");
                return 1;
            }
        }
        if (entries[^1].TaiMinusUtc is < 36 or > 40)
        {
            Console.WriteLine($"leap-seconds: REJECTED - implausible latest TAI-UTC {entries[^1].TaiMinusUtc}");
            return 1;
        }

        var version = DateTime.UtcNow.ToString("yyyyMMdd");
        var dir = Path.Combine(dataRoot, "datasets", "leap-seconds", version);
        Directory.CreateDirectory(dir);
        var csv = new List<string> { "effective_utc,tai_minus_utc" };
        csv.AddRange(entries.Select(e => $"{e.EffectiveUtc:yyyy-MM-ddTHH:mm:ssZ},{e.TaiMinusUtc}"));
        await File.WriteAllLinesAsync(Path.Combine(dir, "leap-seconds.csv"), csv);
        var checksum = Sha256(string.Join('\n', csv));

        var registry = new DatasetRegistry(() => InfrastructureRegistrar.CreateRegistryContext(dbPath));
        await registry.StageAsync("leap-seconds", version, checksum);
        await registry.ActivateAsync("leap-seconds", version);
        Console.WriteLine($"leap-seconds: {entries.Count} entries staged+activated as {version} (latest TAI-UTC {entries[^1].TaiMinusUtc})");
        return 0;
    }

    /// <summary>
    /// Parse the IERS NTP-format leap-seconds.list: comment lines '#', metadata
    /// lines '#@'/'#$', data lines "&lt;ntp-seconds&gt; &lt;tai-utc&gt; # date".
    /// Entries before 1972 (rubber-seconds era, fractional offsets) are dropped;
    /// TAI-UTC is an integer from 1972 on.
    /// </summary>
    internal static List<LeapSecond> ParseLeapSecondsFile(string text)
    {
        var entries = new List<LeapSecond>();
        var ntpEpoch = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ntpSeconds)) continue;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var taiMinusUtc)) continue;
            var effective = ntpEpoch.AddSeconds(ntpSeconds);
            if (effective < LeapSecondEraStart) continue;
            entries.Add(new LeapSecond(effective, taiMinusUtc));
        }
        return entries;
    }

    private static readonly DateTimeOffset LeapSecondEraStart = new(1972, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task<int> RunStarCatalogJobAsync(string dbPath, string dataRoot)
    {
        const string sourceUrl = "https://raw.githubusercontent.com/astronexus/HYG-Database/main/hyg/v3/hyg_v38.csv.gz";
        using var hc = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        byte[] gz;
        try
        {
            gz = await hc.GetByteArrayAsync(sourceUrl);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"star-catalog: fetch FAIL {ex.Message.Split('\n')[0]}");
            return 1;
        }
        Console.WriteLine($"star-catalog: fetched hyg_v38.csv.gz {gz.Length} bytes");

        var stars = new List<Astronomy.SharedKernel.Stars.StarRecord>(120_000);
        using (var gzStream = new GZipStream(new MemoryStream(gz), CompressionMode.Decompress))
        using (var reader = new StreamReader(gzStream))
        {
            reader.ReadLine(); // header
            string? line;
            var parsed = 0;
            while ((line = reader.ReadLine()) is not null)
            {
                parsed++;
                var star = ParseHygLine(line);
                if (star is null) continue;
                stars.Add(star.Value);
            }
            Console.WriteLine($"star-catalog: {parsed} raw lines, {stars.Count} kept");
        }

        if (stars.Count < 100_000)
        {
            Console.WriteLine($"star-catalog: REJECTED - only {stars.Count} stars parsed");
            return 1;
        }
        if (!stars.Any(s => s.Hip == "32349" && Math.Abs(s.Vmag - -1.44) < 0.01))
        {
            Console.WriteLine("star-catalog: REJECTED - Sirius spot check failed (hip 32349, mag -1.44)");
            return 1;
        }
        var magMin = stars.Min(s => s.Vmag);
        var magMax = stars.Max(s => s.Vmag);
        if (magMin < -30 || magMax > 30)
        {
            Console.WriteLine($"star-catalog: REJECTED - implausible magnitude range {magMin:F1}..{magMax:F1}");
            return 1;
        }

        const string version = "v38";
        var dir = Path.Combine(dataRoot, "datasets", "star-catalog-hyg", version);
        Directory.CreateDirectory(dir);
        var csv = new List<string>(stars.Count + 1)
        {
            "hip,proper,bayer_flamsteed,bayer,flam,con,ra_deg,dec_deg,pmra_mas_yr,pmdec_mas_yr,dist_ly,vmag,spect",
        };
        csv.AddRange(stars.Select(s => s.ToCsvLine()));
        var csvText = string.Join('\n', csv);
        await File.WriteAllTextAsync(Path.Combine(dir, "star-catalog-hyg.csv"), csvText);
        var checksum = Sha256(csvText);

        var registry = new DatasetRegistry(() => InfrastructureRegistrar.CreateRegistryContext(dbPath));
        await registry.StageAsync("star-catalog-hyg", version, checksum);
        await registry.ActivateAsync("star-catalog-hyg", version);
        Console.WriteLine($"star-catalog: {stars.Count} stars staged+activated as {version} (mag range {magMin:F1}..{magMax:F1})");
        return 0;
    }

    /// <summary>
    /// Refresh satellite elements from CelesTrak (stations group): fetch, validate,
    /// stage and activate under a UTC-date version (yyyyMMdd). Idempotent for
    /// same-day reruns (same version, upsert semantics); on any failure the
    /// currently-active dataset stays untouched.
    /// </summary>
    public static async Task<int> RunSatelliteElementsRefreshAsync(string dbPath, string dataRoot)
    {
        // The registry + satellite schema are normally created by the heartbeat/API
        // startup; ensure they exist so this job also works standalone on a fresh db.
        InfrastructureRegistrar.MigrateRegistry(dbPath);
        SatelliteStore.EnsureSchema(dbPath);
        var service = new ServiceCollection()
            .AddAstronomyInfrastructure(dbPath, dataRoot)
            .AddSatellitesModule(dbPath)
            .BuildServiceProvider()
            .GetRequiredService<ISatelliteElementIngestionService>();
        var version = DateTime.UtcNow.ToString("yyyyMMdd");
        if (await service.FetchAndStageAsync(version) != 0)
        {
            Console.WriteLine($"omm: refresh FAIL - staging {version} rejected (active dataset unchanged)");
            return 1;
        }
        await service.ActivateAsync(version);
        var s = await service.GetStatusAsync();
        Console.WriteLine($"omm: refresh ok - active={s.ActiveVersion} elements={s.ElementCount} fresh={s.Fresh} warn={s.Warn} degraded={s.Degraded} refuse={s.Refuse}");
        return 0;
    }

    /// <summary>
    /// Parse one HYG v3.8 CSV line (quoted fields handled) into a normalized
    /// StarRecord. Columns: id,hip,hd,hr,gl,bf,proper,ra(h),dec(deg),dist(pc),
    /// pmra,pmdec,rv,mag,absmag,spect,ci,x,y,z,vx,vy,vz,rarad,decrad,pmrarad,
    /// pmdecrad,bayer,flam,con,comp,comp_primary,base,lum,var,var_min,var_max.
    /// Sol (dist 0) and rows without usable coordinates are dropped.
    /// </summary>
    internal static Astronomy.SharedKernel.Stars.StarRecord? ParseHygLine(string line)
    {
        var fields = SplitCsv(line);
        if (fields.Length < 31) return null;
        if (!double.TryParse(fields[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var raHours)) return null;
        if (!double.TryParse(fields[8], NumberStyles.Float, CultureInfo.InvariantCulture, out var dec)) return null;
        if (!double.TryParse(fields[9], NumberStyles.Float, CultureInfo.InvariantCulture, out var distPc)) return null;
        if (distPc <= 0) return null; // Sol
        if (!double.TryParse(fields[10], NumberStyles.Float, CultureInfo.InvariantCulture, out var pmra)) pmra = 0;
        if (!double.TryParse(fields[11], NumberStyles.Float, CultureInfo.InvariantCulture, out var pmdec)) pmdec = 0;
        if (!double.TryParse(fields[13], NumberStyles.Float, CultureInfo.InvariantCulture, out var mag)) mag = 99;
        var proper = fields[6];
        var bf = fields[5];
        var bayer = fields[27];
        var flam = fields[28];
        var con = fields[29];
        return new Astronomy.SharedKernel.Stars.StarRecord(
            fields[1].Trim(), proper, bf, bayer, flam, con,
            raHours * 15.0, dec, pmra, pmdec, distPc * 3.262, mag, fields[15]);
    }

    /// <summary>Minimal RFC-4180-style CSV line splitter (quotes + embedded commas).</summary>
    internal static string[] SplitCsv(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else inQuotes = false;
                }
                else current.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        fields.Add(current.ToString());
        return fields.ToArray();
    }

    private static string Sha256(string s) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}
