using System.Globalization;

namespace S04Sgp4;

public sealed record VerCase(string Id, string Line1, string Line2, List<(double Min, double X, double Y, double Z)> Rows);

public static class Fixtures
{
    public static List<VerCase> Load(string fixtureDir)
    {
        var tleLines = File.ReadAllLines(Path.Combine(fixtureDir, "SGP4-VER.TLE"));
        var tles = new List<(string L1, string L2)>();
        for (var i = 0; i + 1 < tleLines.Length; i++)
        {
            if (tleLines[i].StartsWith("1 ") && tleLines[i + 1].StartsWith("2 "))
            {
                tles.Add((Normalize(tleLines[i]), Normalize(tleLines[i + 1])));
                i++;
            }
        }

        var cases = new List<VerCase>();
        var outLines = File.ReadAllLines(Path.Combine(fixtureDir, "tcppver.out"));
        VerCase? current = null;
        foreach (var raw in outLines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.EndsWith("xx") && line.Length < 12)
            {
                current = new VerCase(line[..^3].Trim(), "", "", new List<(double, double, double, double)>());
                cases.Add(current);
                continue;
            }
            if (current == null) continue;
            if (current.Line1.Length == 0 && current.Line2.Length == 0 && line.StartsWith("1 ") == false)
            {
                if (double.TryParse(line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var t))
                {
                    var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (p.Length >= 4)
                        current.Rows.Add((double.Parse(p[0], CultureInfo.InvariantCulture), double.Parse(p[1], CultureInfo.InvariantCulture), double.Parse(p[2], CultureInfo.InvariantCulture), double.Parse(p[3], CultureInfo.InvariantCulture)));
                }
            }
        }

        if (tles.Count != cases.Count)
            throw new InvalidOperationException($"TLE/case count mismatch: {tles.Count} vs {cases.Count}");

        for (var i = 0; i < cases.Count; i++)
            cases[i] = cases[i] with { Line1 = tles[i].L1, Line2 = tles[i].L2 };

        return cases;
    }

    public static double MeanMotion(VerCase c)
    {
        var p = c.Line2.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return double.Parse(p[7], CultureInfo.InvariantCulture);
    }

    public static (int Year, double Day) Epoch(VerCase c)
    {
        var s = c.Line1.Substring(18, 14).Trim();
        var year2 = int.Parse(s[..2]);
        var year = year2 >= 57 ? 1900 + year2 : 2000 + year2;
        return (year, double.Parse(s[2..], CultureInfo.InvariantCulture));
    }

    private static string Normalize(string tleLine) =>
        tleLine.Length >= 69 ? tleLine[..69] : tleLine.PadRight(69);
}
