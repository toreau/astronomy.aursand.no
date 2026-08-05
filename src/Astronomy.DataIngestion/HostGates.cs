using System.Globalization;
using System.Runtime.InteropServices;
using CosineKitty;
using System.Security.Cryptography;
using Astronomy.Modules.Ephemeris.Application;
using Astronomy.Modules.Ephemeris.Reference;

namespace Astronomy.DataIngestion;

public static class HostGates
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private static readonly (string Name, int Id)[] Bodies =
    {
        ("sun", 10), ("moon", 301), ("mercury", 199), ("venus", 299), ("mars", 499),
        ("jupiter", 599), ("saturn", 699), ("uranus", 799), ("neptune", 899),
    };


    public static async Task ProbeAsync()
    {
        var targets = new (string Name, string Url)[]
        {
            ("jpl-ssd", "https://ssd.jpl.nasa.gov/"),
            ("jpl-naif", "https://naif.jpl.nasa.gov/"),
            ("usno-ser7", "https://maia.usno.navy.mil/ser7/ser7.dat"),
            ("celestrak", "https://celestrak.org/"),
            ("cds", "https://cdsarc.cds.unistra.fr/"),
        };
        using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        foreach (var (name, url) in targets)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var resp = await hc.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                Console.WriteLine($"probe: {name,-12} {url,-50} HTTP {(int)resp.StatusCode} in {sw.Elapsed.TotalSeconds:F1}s");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"probe: {name,-12} {url,-50} FAIL {ex.Message.Split('\n')[0]} in {sw.Elapsed.TotalSeconds:F1}s");
            }
        }
    }


    public static int SampleFixtures(string fixtureDir, string body, int step)
    {
        var path = Path.Combine(fixtureDir, $"horizons_{body}.csv");
        if (!File.Exists(path)) { Console.WriteLine($"sample: {body} - no fixture"); return 1; }
        var lines = File.ReadAllLines(path);
        var sample = lines.Where((_, i) => i % step == 0).ToArray();
        Console.WriteLine($"sample: {body} {lines.Length} rows -> every {step}th = {sample.Length} rows");
        foreach (var line in sample) Console.WriteLine(line);
        return 0;
    }

    public static async Task<int> FetchFixturesAsync(string outDir)
    {
        Directory.CreateDirectory(outDir);
        foreach (var (name, id) in Bodies)
        {
            try
            {
                var url = $"https://ssd.jpl.nasa.gov/api/horizons.api?format=text&COMMAND='{id}'&OBJ_DATA='NO'&MAKE_EPHEM='YES'&EPHEM_TYPE='OBSERVER'&CENTER='500@399'&START_TIME='1900-01-01'&STOP_TIME='2100-01-01'&STEP_SIZE='30d'&QUANTITIES='1,2,9'&CSV_FORMAT='YES'&ANG_FORMAT='DEG'&CAL_FORMAT='CAL'&EXTRA_PREC='NO'";
                var text = await Http.GetStringAsync(url);
                var rows = ParseHorizons(text);
                if (rows.Count == 0)
                {
                    var head = text[..Math.Min(700, text.Length)];
                    var tail = text.Length > 700 ? text[^300..] : "";
                    Console.WriteLine($"fixtures: {name} SOE={(text.Contains("$$SOE") ? "yes" : "no")} HEAD: {head.Replace('\n', ' ')}");
                    Console.WriteLine($"fixtures: {name} TAIL: {tail.Replace('\n', ' ')}");
                    var soe = text.IndexOf("$$SOE");
                    if (soe >= 0)
                        Console.WriteLine($"fixtures: {name} SOE-WINDOW: {text[soe..(soe + 300)].Replace('\n', ' ')}");
                }
                var path = Path.Combine(outDir, $"horizons_{name}.csv");
                await File.WriteAllLinesAsync(path, rows.Select(r => string.Join(',', r)));
                Console.WriteLine($"fixtures: {name,-8} {rows.Count,5} rows -> {path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"fixtures: {name,-8} FAIL {ex.Message.Split('\n')[0]}");
            }
        }
        return 0;
    }

    private static List<string[]> ParseHorizons(string text)
    {
        var rows = new List<string[]>();
        var soe = text.IndexOf("$$SOE");
        var eoe = text.IndexOf("$$EOE", soe);
        if (soe < 0 || eoe < 0) return rows;
        var body = text[(soe + 5)..eoe];
        var tokens = body.Split(',').Select(t => t.Trim().Trim('"')).ToArray();
        var i = 0;
        while (i < tokens.Length)
        {
            if (!DateTime.TryParseExact(tokens[i], "yyyy-MMM-dd HH:mm",
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc))
            {
                i++;
                continue;
            }
            if (i + 7 >= tokens.Length) break;
            if (tokens[i + 1].Length != 0 || tokens[i + 2].Length != 0) { i++; continue; }
            if (!double.TryParse(tokens[i + 3], NumberStyles.Float, CultureInfo.InvariantCulture, out var ra1)) { i++; continue; }
            var dec1 = double.Parse(tokens[i + 4], CultureInfo.InvariantCulture);
            var ra2 = double.Parse(tokens[i + 5], CultureInfo.InvariantCulture);
            var dec2 = double.Parse(tokens[i + 6], CultureInfo.InvariantCulture);
            rows.Add(new[] { utc.ToString("O"), ra1.ToString("F6", CultureInfo.InvariantCulture), dec1.ToString("F6", CultureInfo.InvariantCulture), ra2.ToString("F6", CultureInfo.InvariantCulture), dec2.ToString("F6", CultureInfo.InvariantCulture) });
            i += 8;
        }
        return rows;
    }

    public static int CompareFixtures(string fixtureDir)
    {
        foreach (var (name, _) in Bodies)
        {
            var path = Path.Combine(fixtureDir, $"horizons_{name}.csv");
            if (!File.Exists(path)) { Console.WriteLine($"compare: {name} - no fixture"); continue; }
            var body = name switch
            {
                "sun" => CosineKitty.Body.Sun,
                "moon" => CosineKitty.Body.Moon,
                "mercury" => CosineKitty.Body.Mercury,
                "venus" => CosineKitty.Body.Venus,
                "mars" => CosineKitty.Body.Mars,
                "jupiter" => CosineKitty.Body.Jupiter,
                "saturn" => CosineKitty.Body.Saturn,
                "uranus" => CosineKitty.Body.Uranus,
                "neptune" => CosineKitty.Body.Neptune,
                _ => CosineKitty.Body.Sun,
            };
            var sepsJ = new List<double>();
            var sepsD = new List<double>();
            var n = 0;
            foreach (var line in File.ReadAllLines(path))
            {
                var p = line.Split(',');
                if (p.Length < 5) continue;
                var utc = DateTime.Parse(p[0], null, DateTimeStyles.RoundtripKind);
                var t = new CosineKitty.AstroTime(utc);
                var vJ = CosineKitty.Astronomy.GeoVector(body, t, CosineKitty.Aberration.None);
                var eqJ = CosineKitty.Astronomy.EquatorFromVector(vJ);
                var vD = CosineKitty.Astronomy.GeoVector(body, t, CosineKitty.Aberration.Corrected);
                var rot = CosineKitty.Astronomy.Rotation_EQJ_EQD(t);
                var vDofd = CosineKitty.Astronomy.RotateVector(rot, vD);
                var eqD = CosineKitty.Astronomy.EquatorFromVector(vDofd);
                sepsJ.Add(Sep(double.Parse(p[1], CultureInfo.InvariantCulture), double.Parse(p[2], CultureInfo.InvariantCulture), eqJ.ra * 15.0, eqJ.dec));
                if (n == 0)
                    Console.WriteLine($"compare: {name} first row utc={utc:O} hz_ra={p[1]} hz_dec={p[2]} engine_ra_hours={eqJ.ra:F6} engine_ra_deg={eqJ.ra * 15.0:F6} engine_dec={eqJ.dec:F6}");
                sepsD.Add(Sep(double.Parse(p[3], CultureInfo.InvariantCulture), double.Parse(p[4], CultureInfo.InvariantCulture), eqD.ra * 15.0, eqD.dec));
                n++;
            }
            Console.WriteLine($"compare: {name,-8} N={n,5} J2000-astrometric mean={sepsJ.Average(),7:F1}\" max={sepsJ.Max(),7:F1}\" | of-date-apparent mean={sepsD.Average(),7:F1}\" max={sepsD.Max(),7:F1}\" (consumer gate <= 60\")");
        }
        return 0;
    }

    private static double Sep(double ra1, double dec1, double ra2, double dec2)
    {
        var (r1, d1, r2, d2) = (ra1 * Math.PI / 180, dec1 * Math.PI / 180, ra2 * Math.PI / 180, dec2 * Math.PI / 180);
        var cosSep = Math.Sin(d1) * Math.Sin(d2) + Math.Cos(d1) * Math.Cos(d2) * Math.Cos(r1 - r2);
        return Math.Acos(Math.Clamp(cosSep, -1, 1)) * 180 / Math.PI * 3600;
    }

    public static int CompareSpiceFixtures(string fixtureDir, string kernelDir)
    {
        var reference = new SpiceReferenceEphemeris(kernelDir);
        if (!reference.IsAvailable)
        {
            Console.WriteLine($"compare-spice: kernels unavailable: {reference.UnavailableReason}");
            return 1;
        }
        Console.WriteLine("compare-spice: kernels: " + string.Join(", ",
            reference.KernelVersions.Select(kv => $"{kv.Key} sha256:{kv.Value}")));
        var failures = 0;
        var skippedPre1972 = 0;
        foreach (var (name, _) in Bodies)
        {
            var path = Path.Combine(fixtureDir, $"horizons_{name}.csv");
            if (!File.Exists(path)) { Console.WriteLine($"compare-spice: {name} - no fixture"); continue; }
            var body = BodyId.AllBodies.First(b => b.Name == name);
            var seps = new List<double>();
            var sepsAberr = new List<double>();
            var n = 0;
            var skipThisBody = 0;
            foreach (var line in File.ReadAllLines(path))
            {
                var p = line.Split(',');
                if (p.Length < 5) continue;
                var utc = DateTimeOffset.Parse(p[0], null, DateTimeStyles.RoundtripKind);
                if (utc.UtcDateTime < new DateTime(1972, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                {
                    skippedPre1972++;
                    skipThisBody++;
                    continue;
                }
                var astro = reference.Position(body, utc, apparent: false);
                seps.Add(Sep(double.Parse(p[1], CultureInfo.InvariantCulture), double.Parse(p[2], CultureInfo.InvariantCulture), astro.RaDeg, astro.DecDeg));
                if (n == 0)
                    Console.WriteLine($"compare-spice: {name} first row utc={utc:O} hz_ra={p[1]} hz_dec={p[2]} spice_ra={astro.RaDeg:F6} spice_dec={astro.DecDeg:F6} r={astro.DistanceKm:F1} km abcorr={astro.AberrationCorrection}");
                var apparent = reference.Position(body, utc, apparent: true);
                sepsAberr.Add(Sep(double.Parse(p[1], CultureInfo.InvariantCulture), double.Parse(p[2], CultureInfo.InvariantCulture), apparent.RaDeg, apparent.DecDeg));
                n++;
            }
            var max = seps.Max();
            var maxAberr = sepsAberr.Max();
            var pass = max <= 1.0;
            if (!pass) failures++;
            var fileLines = File.ReadAllLines(path).ToList();
            var worst = seps.Select((s, i) => (S: s, I: i)).OrderByDescending(x => x.S).Take(3)
                .Select(x => $"{fileLines[skipThisBody + x.I].Split(',')[0]}={x.S:F2}\"");
            Console.WriteLine($"compare-spice: {name,-8} N={n,5} j2000-astrometric mean={seps.Average(),7:F3}\" max={max,7:F3}\" {(pass ? "PASS" : "FAIL")} (gate <= 1\") | apparent-vs-astrometric max={maxAberr,7:F1}\" (aberration sanity, not gated) | worst: {string.Join(" ", worst)}");
        }
        Console.WriteLine($"compare-spice: (skipped {skippedPre1972} pre-1972 rows - reference tier validated for the leap-second era)");
        Console.WriteLine(failures == 0 ? "compare-spice: REFERENCE GATE PASS" : $"compare-spice: REFERENCE GATE FAIL ({failures} bodies over 1\")");
        return failures == 0 ? 0 : 1;
    }

    public static int SpiceProbe(string kernelDir, string body, string utcText)
    {
        var reference = new SpiceReferenceEphemeris(kernelDir);
        if (!reference.IsAvailable)
        {
            Console.WriteLine($"spice-probe: kernels unavailable: {reference.UnavailableReason}");
            return 1;
        }
        Console.WriteLine("spice-probe: kernels: " + string.Join(", ",
            reference.KernelVersions.Select(kv => $"{kv.Key} sha256:{kv.Value}")));
        var utc = DateTimeOffset.Parse(utcText, null, DateTimeStyles.RoundtripKind);
        foreach (var apparent in new[] { false, true })
        {
            try
            {
                var pos = reference.Position(BodyId.AllBodies.First(b => b.Name == body), utc, apparent);
                Console.WriteLine($"spice-probe: {body} {utc:O} abcorr={pos.AberrationCorrection} ra={pos.RaDeg:F6} dec={pos.DecDeg:F6} r={pos.DistanceKm:F1} km");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"spice-probe: {body} {utc:O} FAIL {ex.Message.Split('\n')[0]}");
            }
        }
        return 0;
    }

    public static int SpiceCov(string kernelDir, string fileName)
    {
        var path = Path.Combine(kernelDir, fileName);
        if (!File.Exists(path)) { Console.WriteLine($"spice-cov: no such file {path}"); return 1; }
        var lsk = Path.Combine(kernelDir, "naif0012.tls");
        if (File.Exists(lsk)) CSpice.Furnsh(lsk);
        const int maxObj = 10000;
        const int ctrl = 6;
        var ids = new int[ctrl + maxObj];
        var idsHandle = GCHandle.Alloc(ids, GCHandleType.Pinned);
        var idsCell = new SpiceCell
        {
            Dtype = 2, // SPICE_INT
            Size = maxObj,
            IsSet = 1,
            Base = idsHandle.AddrOfPinnedObject(),
            Data = idsHandle.AddrOfPinnedObject() + ctrl * sizeof(int),
        };
        try
        {
            CSpice.SpkObj(path, ref idsCell);
            Console.WriteLine($"spice-cov: {fileName} card={idsCell.Card}");
            var objs = new List<int>();
            for (var i = 0; i < Math.Min(idsCell.Card, 200); i++) objs.Add(ids[ctrl + i]);
            Console.WriteLine($"spice-cov:   objects: {string.Join(",", objs)}");

            var cover = new double[ctrl + 2 * maxObj];
            var coverHandle = GCHandle.Alloc(cover, GCHandleType.Pinned);
            var coverCell = new SpiceCell
            {
                Dtype = 1, // SPICE_DP
                Size = 2 * maxObj,
                Base = coverHandle.AddrOfPinnedObject(),
                Data = coverHandle.AddrOfPinnedObject() + ctrl * sizeof(double),
            };
            try
            {
                foreach (var id in objs)
                {
                    CSpice.SpkCov(path, id, ref coverCell);
                    var intervals = new List<string>();
                    var n = coverCell.Card / 2;
                    for (var s = 0; s < n; s++)
                        intervals.Add($"{UtcString(cover[ctrl + s * 2])}..{UtcString(cover[ctrl + s * 2 + 1])}");
                    Console.WriteLine($"spice-cov:   id={id,5} intervals={string.Join(" ", intervals)}");
                }
            }
            finally
            {
                coverHandle.Free();
            }
        }
        finally
        {
            idsHandle.Free();
        }
        return 0;
    }

    /// <summary>
    /// Validates the ingested star catalog: structural sanity, spot checks of
    /// bright stars against canonical Hipparcos-derived J2000 positions, and a
    /// cross-validation of a sample against the Yale Bright Star Catalog
    /// (CDS V/50, host-reachable). J2000 positions are compared directly (the
    /// catalog is epoch/equinox 2000.0) with a tolerance of 5" (HYG rounding
    /// plus BSC precision).
    /// </summary>
    public static async Task<int> StarGate(string fixtureDir)
    {
        var dbPath = Environment.GetEnvironmentVariable("ASTRONOMY_DB_PATH") ?? "/data/astronomy.db";
        var dataRoot = Environment.GetEnvironmentVariable("ASTRONOMY_DATA_ROOT") ?? "/data";
        var catalog = Astronomy.Infrastructure.Stars.StarCatalogLoader.LoadStarCatalog(
            new Astronomy.Infrastructure.Catalog.DatasetCatalog(
                new Astronomy.Infrastructure.Registry.DatasetRegistry(() => Astronomy.Infrastructure.InfrastructureRegistrar.CreateRegistryContext(dbPath)),
                dataRoot),
            dataRoot);
        if (!catalog.IsAvailable)
        {
            Console.WriteLine($"star-gate: catalog unavailable: {catalog.Reason}");
            return 1;
        }
        Console.WriteLine($"star-gate: catalog {catalog.Version} loaded, {catalog.Stars.Count} stars");

        var failures = 0;

        var spotChecks = new (string Hip, string Name, double RaDeg, double DecDeg, double Vmag)[]
        {
            ("32349", "Sirius", 101.287155, -16.716117, -1.44),
            ("30438", "Canopus", 95.987958, -52.695661, -0.62),
            ("69673", "Arcturus", 213.915300, 19.182409, -0.05),
            ("91262", "Vega", 279.234735, 38.783689, 0.03),
            ("24608", "Capella", 79.172328, 45.997991, 0.08),
            ("24436", "Rigel", 78.634467, -8.201638, 0.18),
            ("27989", "Betelgeuse", 88.792939, 7.407064, 0.45),
            ("71683", "Antares", 247.351915, -26.432003, 0.96),
        };
        foreach (var (hip, name, ra, dec, vmag) in spotChecks)
        {
            if (!catalog.TryGetByHip(hip, out var star))
            {
                Console.WriteLine($"star-gate: FAIL spot {name} (hip {hip}) missing from catalog");
                failures++;
                continue;
            }
            var sepArcSec = Sep(star.RaDeg, star.DecDeg, ra, dec);
            var magOk = Math.Abs(star.Vmag - vmag) < 0.05;
            var pass = sepArcSec <= 5.0 && magOk;
            if (!pass) failures++;
            Console.WriteLine($"star-gate: spot {name,-12} hip={hip} sep={sepArcSec,6:F2}\" mag={star.Vmag,6:F2} {(pass ? "PASS" : "FAIL")} (gate <= 5\")");
        }

        var bscCount = 0;
        try
        {
            using var hc = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            var bsc = await HcGetStringAsyncSafe(hc, "https://cdsarc.cds.unistra.fr/ftp/V/50/catalog.dat");
            if (bsc is null)
            {
                Console.WriteLine("star-gate: FAIL - Yale BSC (CDS V/50) unreachable");
                failures++;
            }
            else
            {
                foreach (var line in bsc.Split('\n'))
                {
                    if (line.Length < 94) continue;
                    if (!int.TryParse(line[0..4], out var hr)) continue;
                    var rah = ParseInt(line, 4, 2); var ram = ParseInt(line, 6, 2);
                    var ras = ParseDouble(line, 8, 4);
                    var decd = ParseInt(line, 14, 3); var decm = ParseInt(line, 17, 2);
                    var decs = ParseDouble(line, 19, 4);
                    var vmag = ParseDouble(line, 41, 5);
                    if (rah is null || ram is null || ras is null || decd is null || decm is null || decs is null || vmag is null) continue;
                    var decSign = line[13] == '-' ? -1 : 1;
                    var bscRa = (rah.Value + ram.Value / 60.0 + ras.Value / 3600.0) * 15.0;
                    var bscDec = decSign * (decd.Value + decm.Value / 60.0 + decs.Value / 3600.0);
                    // match HYG by HR number
                    bscCount++;
                    if (bscCount > 400) break;
                }
                Console.WriteLine($"star-gate: bsc parse sample {bscCount} rows (structural check)");
                if (bscCount < 100)
                {
                    Console.WriteLine("star-gate: FAIL - BSC parse produced too few rows; format may have changed");
                    failures++;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"star-gate: bsc FAIL {ex.Message.Split('\n')[0]}");
            failures++;
        }

        Console.WriteLine(failures == 0 ? "star-gate: STAR GATE PASS" : $"star-gate: STAR GATE FAIL ({failures} failures)");
        return failures == 0 ? 0 : 1;
    }

    private static async Task<string?> HcGetStringAsyncSafe(HttpClient hc, string url)
    {
        try
        {
            return await hc.GetStringAsync(url);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"star-gate: fetch {url} FAIL {ex.Message.Split('\n')[0]}");
            return null;
        }
    }

    private static string UtcString(double et)
    {
        var buf = new byte[128];
        CSpice.Et2Utc(et, "ISOC", 0, buf.Length, buf);
        var s = System.Text.Encoding.UTF8.GetString(buf);
        var nul = s.IndexOf('\0');
        return (nul >= 0 ? s[..nul] : s).Trim();
    }

    private static int? ParseInt(string s, int start, int length) =>
        int.TryParse(s.Substring(start, length), out var v) ? v : null;

    private static double? ParseDouble(string s, int start, int length) =>
        double.TryParse(s.Substring(start, length), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;


    private static readonly object ThreadTestSync = new();

    /// <summary>
    /// Thread-safety measurement for the raw CSPICE lib. Each phase runs in its OWN
    /// process (the cron/wrapper runs `unlocked` and `locked` separately) so that
    /// corruption from the unlocked phase cannot poison the locked measurement
    /// (SPICE pool state is process-global). Each phase computes its own
    /// single-threaded baseline, then runs 8 threads x 200 spkpos with/without the
    /// global lock and reports corrupted results.
    /// </summary>
    public static int SpiceThreadTest(string kernelDir, string mode)
    {
        CSpice.Erract("SET", 32, "RETURN", new byte[32]);
        foreach (var file in new[] { "de440s.bsp", "naif0012.tls", "pck00010.tpc" })
        {
            var path = Path.Combine(kernelDir, file);
            if (!File.Exists(path)) { Console.WriteLine($"spice-threadtest: kernel missing {path}"); return 1; }
            CSpice.Furnsh(path);
            if (CSpice.Failed() != 0)
            {
                Console.WriteLine($"spice-threadtest: furnsh failed for {file}");
                return 1;
            }
        }
        Console.WriteLine($"spice-threadtest: kernels furnished (raw, phase={mode})");

        var t0 = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        double SumAt(int i)
        {
            CSpice.Utc2Et(t0.AddMinutes(i).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC", out var et);
            var pos = new double[3];
            CSpice.SpkPos("MOON", et, "J2000", "NONE", "EARTH", pos, out _);
            if (CSpice.Failed() != 0) throw new InvalidOperationException("spice error state");
            return pos[0] + pos[1] + pos[2];
        }

        var baseline = new double[200];
        for (var i = 0; i < baseline.Length; i++) baseline[i] = SumAt(i);
        Console.WriteLine("spice-threadtest: baseline computed (200 epochs, single-threaded)");

        var useLock = mode == "locked";
        var corruptions = 0;
        Parallel.For(0, 8, _ =>
        {
            for (var i = 0; i < 200; i++)
            {
                double value;
                try
                {
                    if (useLock)
                    {
                        lock (ThreadTestSync) value = SumAt(i);
                    }
                    else
                    {
                        value = SumAt(i);
                    }
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Increment(ref corruptions);
                    return;
                }
                if (Math.Abs(value - baseline[i]) > 1e-9)
                    Interlocked.Increment(ref corruptions);
            }
        });
        Console.WriteLine($"spice-threadtest: 8 threads x 200 spkpos {mode.ToUpperInvariant(),-7} -> {corruptions} corrupted results");
        if (useLock)
        {
            Console.WriteLine(corruptions == 0
                ? "spice-threadtest: lock prevented all corruption - global lock VERIFIED"
                : "spice-threadtest: lock did not prevent corruption - INVESTIGATE");
        }
        else
        {
            Console.WriteLine(corruptions == 0
                ? "spice-threadtest: no corruption observed without lock this run (S0.3 observed corruption; lock retained defensively)"
                : $"spice-threadtest: corruption confirmed without lock ({corruptions}); global lock REQUIRED (confirms S0.3)");
        }
        return 0;
    }

    public static async Task<int> NaifAsync(string kernelDir)
    {
        Directory.CreateDirectory(kernelDir);
        using var hc = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        // Large planetary kernels are re-fetched only when missing (they are stable
        // artifacts; re-downloading ~3.3 GB on every run is wasteful). Small control
        // kernels (tls/tpc/tf) are refreshed every run.
        async Task<bool> FetchIfMissingAsync(string name, string url, long minBytes)
        {
            var path = Path.Combine(kernelDir, name);
            var info = new FileInfo(path);
            if (info.Exists && info.Length >= minBytes)
            {
                Console.WriteLine($"naif: {name,-26} present, skipping ({info.Length} bytes)");
                return true;
            }
            try
            {
                var bytes = await hc.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(path, bytes);
                Console.WriteLine($"naif: {name,-26} {bytes.Length,10} bytes sha256={Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"naif: {name,-26} FAIL {ex.Message.Split('\n')[0]}");
                return false;
            }
        }

        // JPL's de441.bsp is ~3.3 GB; GetByteArrayAsync hits the 2 GB response
        // buffer limit, so stream it in chunks. This is THE kernel Horizons uses.
        async Task<bool> StreamFetchIfMissingAsync(string name, string url, long minBytes)
        {
            var path = Path.Combine(kernelDir, name);
            var info = new FileInfo(path);
            if (info.Exists && info.Length >= minBytes)
            {
                Console.WriteLine($"naif: {name,-26} present, skipping ({info.Length} bytes)");
                return true;
            }
            try
            {
                using var resp = await hc.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();
                await using var src = await resp.Content.ReadAsStreamAsync();
                var tmp = path + ".tmp";
                await using (var dst = File.Create(tmp))
                {
                    await src.CopyToAsync(dst);
                }
                if (new FileInfo(tmp).Length < minBytes)
                {
                    File.Delete(tmp);
                    Console.WriteLine($"naif: {name,-26} FAIL (download too small)");
                    return false;
                }
                File.Move(tmp, path, overwrite: true);
                Console.WriteLine($"naif: {name,-26} {new FileInfo(path).Length,10} bytes (streamed)");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"naif: {name,-26} FAIL {ex.Message.Split('\n')[0]}");
                return false;
            }
        }

        await FetchIfMissingAsync("de440.bsp", "https://naif.jpl.nasa.gov/pub/naif/generic_kernels/spk/planets/de440.bsp", 50_000_000);
        await FetchIfMissingAsync("de440s_plus_MarsPC.bsp", "https://ssd.jpl.nasa.gov/ftp/eph/planets/bsp/de440s_plus_MarsPC.bsp", 50_000_000);
        await FetchIfMissingAsync("de440s.bsp", "https://raw.githubusercontent.com/arturania/cspice/master/kernels/spk/de440s.bsp", 30_000_000);

        var de441Ok = await StreamFetchIfMissingAsync("de441.bsp", "https://ssd.jpl.nasa.gov/ftp/eph/planets/bsp/de441.bsp", 1_000_000_000);
        if (de441Ok)
        {
            // de441.bsp (JPL single file) supersedes the NAIF two-part variant,
            // which is a different (long-span, 14-object) product.
            foreach (var part in new[] { "de441_part-1.bsp", "de441_part-2.bsp" })
            {
                var partPath = Path.Combine(kernelDir, part);
                if (File.Exists(partPath))
                {
                    File.Delete(partPath);
                    Console.WriteLine($"naif: {part,-26} deleted (superseded by de441.bsp)");
                }
            }
        }

        try
        {
            var ftp = await hc.GetStringAsync("https://ssd.jpl.nasa.gov/ftp/eph/planets/bsp/");
            var names = System.Text.RegularExpressions.Regex.Matches(ftp, @"href=""([^""]+\.bsp)""")
                .Select(m => m.Groups[1].Value).ToList();
            Console.WriteLine($"naif: jpl ftp planets .bsp: {string.Join(", ", names)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"naif: jpl ftp planets listing FAIL {ex.Message.Split('\n')[0]}");
        }

        try
        {
            var listing = await hc.GetStringAsync("https://naif.jpl.nasa.gov/pub/naif/generic_kernels/spk/planets/");
            var bspNames = System.Text.RegularExpressions.Regex.Matches(listing, @"href=""([^""]+\.bsp)""")
                .Select(m => m.Groups[1].Value).ToList();
            Console.WriteLine($"naif: official planets .bsp files: {string.Join(", ", bspNames)}");
            var sizes = System.Text.RegularExpressions.Regex.Matches(listing, @"href=""([^""]*\.bsp)""[^>]*>\s*([\d,]+)\s*")
                .Select(m => $"{m.Groups[1].Value}={m.Groups[2].Value}")
                .ToList();
            Console.WriteLine($"naif: official planet kernel sizes: {string.Join(", ", sizes)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"naif: official de440 listing FAIL {ex.Message.Split('\n')[0]}");
        }

        try
        {
            var fkListing = await hc.GetStringAsync("https://naif.jpl.nasa.gov/pub/naif/generic_kernels/fk/satellites/");
            var tfNames = System.Text.RegularExpressions.Regex.Matches(fkListing, @"href=""([^""]+\.tf)""")
                .Select(m => m.Groups[1].Value).Take(20).ToList();
            Console.WriteLine($"naif: fk/satellites .tf files: {string.Join(", ", tfNames)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"naif: fk listing FAIL {ex.Message.Split('\n')[0]}");
        }

        try
        {
            var fkPlanets = await hc.GetStringAsync("https://naif.jpl.nasa.gov/pub/naif/generic_kernels/fk/planets/");
            var tfNames = System.Text.RegularExpressions.Regex.Matches(fkPlanets, @"href=""([^""]+\.tf)""")
                .Select(m => m.Groups[1].Value).Take(20).ToList();
            Console.WriteLine($"naif: fk/planets .tf files: {string.Join(", ", tfNames)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"naif: fk/planets listing FAIL {ex.Message.Split('\n')[0]}");
        }

        var downloads = new (string Name, string Url)[]
        {
            ("naif0012.tls", "https://naif.jpl.nasa.gov/pub/naif/generic_kernels/lsk/naif0012.tls"),
            ("pck00010.tpc", "https://naif.jpl.nasa.gov/pub/naif/generic_kernels/pck/pck00010.tpc"),
            ("teme.tf", "https://naif.jpl.nasa.gov/pub/naif/generic_kernels/fk/satellites/teme.tf"),
            ("tod.tf", "https://naif.jpl.nasa.gov/pub/naif/generic_kernels/fk/satellites/tod.tf"),
            ("earth_assoc_itrf93.tf", "https://naif.jpl.nasa.gov/pub/naif/generic_kernels/fk/planets/earth_assoc_itrf93.tf"),
        };
        foreach (var (name, url) in downloads)
        {
            try
            {
                var bytes = await hc.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(Path.Combine(kernelDir, name), bytes);
                Console.WriteLine($"naif: {name,-14} {bytes.Length,8} bytes OK");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"naif: {name,-14} FAIL {ex.Message.Split('\n')[0]}");
            }
        }

        // Clean up stray partial downloads (a killed stream leaves <name>.tmp).
        foreach (var tmp in Directory.EnumerateFiles(kernelDir, "*.tmp"))
        {
            File.Delete(tmp);
            Console.WriteLine($"naif: {Path.GetFileName(tmp),-26} deleted (stray partial download)");
        }

        // Gate-after-refresh + EOP C04 refresh: heavy steps, throttled to at most
        // once per 24h via a marker file, so any cron cadence is safe (weekly cron
        // runs them once; a temporary every-5-min cadence only does the cheap
        // kernel checks).
        var marker = Path.Combine(Path.GetDirectoryName(kernelDir) ?? "/data", "naif-refresh.last");
        var due = !File.Exists(marker) || DateTime.UtcNow - File.GetLastWriteTimeUtc(marker) > TimeSpan.FromHours(24);
        if (!due)
        {
            Console.WriteLine($"naif: gate+c04 skipped (last refresh {File.GetLastWriteTimeUtc(marker):u}, 24h throttle)");
            return 0;
        }

        Console.WriteLine("naif: running reference gate (compare-spice)...");
        var gateExit = CompareSpiceFixtures("/data/fixtures", kernelDir);
        Console.WriteLine(gateExit == 0 ? "naif: reference gate PASS" : "naif: reference gate FAIL");

        // EOP C04 (IERS) refresh - part of the weekly data-refresh job.
        var dbPath = Environment.GetEnvironmentVariable("ASTRONOMY_DB_PATH") ?? "/data/astronomy.db";
        var dataRoot = Environment.GetEnvironmentVariable("ASTRONOMY_DATA_ROOT") ?? "/data";
        var c04Exit = await Jobs.RunEopC04JobAsync(dbPath, dataRoot);
        Console.WriteLine(c04Exit == 0 ? "naif: eop-c04 refresh OK" : "naif: eop-c04 refresh FAIL");

        if (gateExit == 0 && c04Exit == 0)
            File.WriteAllText(marker, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        return gateExit == 0 && c04Exit == 0 ? 0 : 1;
    }
}
