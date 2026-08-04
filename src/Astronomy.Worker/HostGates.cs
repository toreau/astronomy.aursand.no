using System.Globalization;
using CosineKitty;
using System.Security.Cryptography;

namespace Astronomy.Worker;

public static class HostGates
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    private static readonly (string Name, int Id)[] Bodies =
    {
        ("sun", 10), ("moon", 301), ("venus", 299), ("mars", 499),
        ("jupiter", 599), ("saturn", 699),
    };

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
                "venus" => CosineKitty.Body.Venus,
                "mars" => CosineKitty.Body.Mars,
                "jupiter" => CosineKitty.Body.Jupiter,
                "saturn" => CosineKitty.Body.Saturn,
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

    public static async Task<int> NaifAsync(string kernelDir)
    {
        Directory.CreateDirectory(kernelDir);
        using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        try
        {
            var r = await hc.GetAsync("https://naif.jpl.nasa.gov/pub/naif/generic_kernels/spk/planets/", HttpCompletionOption.ResponseHeadersRead);
            Console.WriteLine($"naif: spk dir reachable HTTP {(int)r.StatusCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"naif: spk dir NOT reachable: {ex.Message.Split('\n')[0]}");
            return 1;
        }

        var mirrors = new[]
        {
            "https://raw.githubusercontent.com/arturania/cspice/master/kernels/spk/de440s.bsp",
        };
        var kernelPath = Path.Combine(kernelDir, "de440s.bsp");
        var data = await hc.GetByteArrayAsync(mirrors[0]);
        var sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        Console.WriteLine($"naif: de440s.bsp {data.Length} bytes sha256={sha}");
        Console.WriteLine($"naif: expected (dual-mirror, S0.3) = c1c7feeab882263fc493a9d5a5b2ddd71b54826cdf65d8d17a76126b260a49f2 match={sha == "c1c7feeab882263fc493a9d5a5b2ddd71b54826cdf65d8d17a76126b260a49f2"}");
        await File.WriteAllBytesAsync(kernelPath, data);

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
            ("earth_fixed.tf", "https://naif.jpl.nasa.gov/pub/naif/generic_kernels/fk/planets/earth_fixed.tf"),
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
        return 0;
    }
}
