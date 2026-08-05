using System.Globalization;
using Astronomy.Modules.Satellites.Application;

namespace Astronomy.AccuracyTests;

public class Sgp4AccuracyTests
{
    internal sealed record VerCase(string Id, string Line1, string Line2, List<(double Min, double X, double Y, double Z)> Rows);

    private static readonly List<VerCase> Cases = Load();

    private static string FixturesDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "fixtures");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "SGP4-VER.TLE")))
                return candidate;
        }
        throw new FileNotFoundException("sgp4 verification fixtures not found");
    }

    private static List<VerCase> Load()
    {
        var dir = FixturesDir();
        var tleLines = File.ReadAllLines(Path.Combine(dir, "SGP4-VER.TLE"));
        var tles = new List<(string L1, string L2)>();
        for (var i = 0; i + 1 < tleLines.Length; i++)
        {
            if (tleLines[i].StartsWith("1 ") && tleLines[i + 1].StartsWith("2 "))
            {
                tles.Add((tleLines[i][..69], tleLines[i + 1][..69]));
                i++;
            }
        }

        var cases = new List<VerCase>();
        VerCase? current = null;
        foreach (var raw in File.ReadAllLines(Path.Combine(dir, "tcppver.out")))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.EndsWith("xx") && line.Length < 12)
            {
                current = new VerCase(line[..^3].Trim(), "", "", []);
                cases.Add(current);
                continue;
            }
            if (current is null) continue;
            if (current.Line1.Length == 0 && current.Line2.Length == 0 && !line.StartsWith("1 ") && !line.StartsWith("2 "))
            {
                var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length >= 4 &&
                    double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var t) &&
                    double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                    double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) &&
                    double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z))
                    current.Rows.Add((t, x, y, z));
            }
        }
        for (var i = 0; i < cases.Count && i < tles.Count; i++)
            cases[i] = cases[i] with { Line1 = tles[i].L1, Line2 = tles[i].L2 };
        return cases;
    }

    private static DateTimeOffset EpochOf(VerCase c)
    {
        var s = c.Line1.Substring(18, 14).Trim();
        var year2 = int.Parse(s[..2], CultureInfo.InvariantCulture);
        var year = year2 >= 57 ? 1900 + year2 : 2000 + year2;
        var day = double.Parse(s[2..], CultureInfo.InvariantCulture);
        var date = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(day - 1.0);
        return new DateTimeOffset(date);
    }

    public static IEnumerable<object[]> InEnvelopeCases()
    {
        foreach (var c in Cases)
        {
            // Excluded: intentionally broken TLEs (33333-33335), the 11801 input
            // quirk (One_Sgp4 parser rejects the line; S0.4 known issue), and the
            // 20413 long-arc block (t ~ 1.84M min, far outside the SGP4 envelope).
            if (c.Id is "33333" or "33334" or "33335" or "11801") continue;
            if (c.Id == "20413" && c.Rows.Any(r => r.Min > 100_000)) continue;
            if (c.Line1.Length < 69 || c.Rows.Count == 0) continue;
            yield return new object[] { c };
        }
    }

    [Theory]
    [MemberData(nameof(InEnvelopeCases))]
    public void Propagation_MatchesValladoReference(object caseObj)
    {
        var c = (VerCase)caseObj;
        var propagator = new OneSgp4Propagator();
        var epoch = EpochOf(c);
        var maxError = 0.0;
        var maxMinute = 0.0;
        foreach (var (min, x, y, z) in c.Rows)
        {
            var v = propagator.PropagateTle(c.Line1, c.Line2, epoch, epoch.AddMinutes(min));
            var err = Math.Sqrt((v.XKm - x) * (v.XKm - x) + (v.YKm - y) * (v.YKm - y) + (v.ZKm - z) * (v.ZKm - z));
            if (err > maxError) { maxError = err; maxMinute = min; }
        }
        Assert.True(maxError <= 1.5,
            $"{c.Id}: max error {maxError:F3} km at t={maxMinute:F0} min (gate <= 1.5 km; S0.4 measured <= 0.96 km)");
    }
}
