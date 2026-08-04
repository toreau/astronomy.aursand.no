using System.Globalization;

namespace S10Omm;

public sealed record OmmRow(string Name, string NoradId, DateTime EpochUtc, double MeanMotion, double Eccentricity,
    double Inclination, double RaOfAscNode, double ArgOfPericenter, double MeanAnomaly, double Bstar,
    double MmDot, double MmDdot, int RevAtEpoch, string Raw)
{
    public string ToJson() => string.Create(CultureInfo.InvariantCulture,
        $"{{\"name\":\"{Name}\",\"norad\":\"{NoradId}\",\"epoch\":\"{EpochUtc:O}\",\"mm\":{MeanMotion:F9},\"ecc\":{Eccentricity:F9},\"incl\":{Inclination:F9},\"raan\":{RaOfAscNode:F9},\"argp\":{ArgOfPericenter:F9},\"ma\":{MeanAnomaly:F9},\"bstar\":{Bstar:E4},\"mmdot\":{MmDot:E4},\"mmddot\":{MmDdot:E4},\"rev\":{RevAtEpoch}}}");

}

public static class OmmIngest
{
    public static OmmRow FromJson(string json, string raw)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var r = doc.RootElement;
        return new OmmRow(r.GetProperty("name").GetString()!, r.GetProperty("norad").GetString()!,
            DateTime.Parse(r.GetProperty("epoch").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            r.GetProperty("mm").GetDouble(), r.GetProperty("ecc").GetDouble(), r.GetProperty("incl").GetDouble(),
            r.GetProperty("raan").GetDouble(), r.GetProperty("argp").GetDouble(), r.GetProperty("ma").GetDouble(),
            r.GetProperty("bstar").GetDouble(), r.GetProperty("mmdot").GetDouble(), r.GetProperty("mmddot").GetDouble(),
            r.GetProperty("rev").GetInt32(), raw);
    }

    private const string CelesTrakOmm = "https://celestrak.org/NORAD/elements/gp.php?GROUP=stations&FORMAT=omm";

    public static async Task<(string Payload, List<OmmRow> Rows)> FetchCelesTrakAsync(string? url = null)
    {
        using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        var payload = await hc.GetStringAsync(url ?? CelesTrakOmm);
        return (payload, ParseCsv(payload));
    }

    public static List<OmmRow> ParseCsv(string csv)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) throw new FormatException("empty or header-only OMM payload");
        var rows = new List<OmmRow>();
        foreach (var line in lines.Skip(1))
        {
            var p = line.Split(',');
            if (p.Length < 17) continue;
            var epoch = DateTime.ParseExact(p[2].Trim(), "yyyy-MM-ddTHH:mm:ss.ffffff",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            rows.Add(new OmmRow(
                p[0].Trim(), p[11].Trim(), epoch,
                D(p[3]), D(p[4]), D(p[5]), D(p[6]), D(p[7]), D(p[8]),
                D(p[14]), D(p[15]), D(p[16]),
                int.TryParse(p[13].Trim(), out var rev) ? rev : 0,
                line));
        }
        return rows;

        static double D(string s) => double.Parse(s.Trim(), CultureInfo.InvariantCulture);
    }

    public static List<(int Row, string Field, string Value)> Validate(List<OmmRow> rows, DateTime nowUtc)
    {
        var errors = new List<(int, string, string)>();
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.NoradId.Length is < 5 or > 6 || !r.NoradId.All(char.IsDigit))
                errors.Add((i, "norad", r.NoradId));
            var age = (nowUtc - r.EpochUtc).TotalHours;
            if (age < -2 || age > 48)
                errors.Add((i, "epoch", $"{r.EpochUtc:O} (age {age:F1}h)"));
            if (r.MeanMotion is < 0.05 or > 17.0)
                errors.Add((i, "mean_motion", r.MeanMotion.ToString(CultureInfo.InvariantCulture)));
            if (r.Eccentricity is < 0.0 or > 0.9)
                errors.Add((i, "eccentricity", r.Eccentricity.ToString(CultureInfo.InvariantCulture)));
            if (r.Inclination is < 0.0 or > 180.0)
                errors.Add((i, "inclination", r.Inclination.ToString(CultureInfo.InvariantCulture)));
            if (Math.Abs(r.Bstar) > 0.02)
                errors.Add((i, "bstar", r.Bstar.ToString(CultureInfo.InvariantCulture)));
            if (r.RaOfAscNode is < 0.0 or >= 360.0 || r.ArgOfPericenter is < 0.0 or >= 360.0 || r.MeanAnomaly is < 0.0 or >= 360.0)
                errors.Add((i, "angles", $"{r.RaOfAscNode:F2}/{r.ArgOfPericenter:F2}/{r.MeanAnomaly:F2}"));
        }
        return errors;
    }

    public static (int Fresh, int Warn, int Degraded, int Refuse) FreshnessState(List<(string, DateTime, string)> elements, DateTime nowUtc)
    {
        var fresh = 0; var warn = 0; var degraded = 0; var refuse = 0;
        foreach (var (_, epoch, _) in elements)
        {
            var age = (nowUtc - epoch).TotalHours;
            if (age < 24) fresh++;
            else if (age < 72) warn++;
            else if (age < 168) degraded++;
            else refuse++;
        }
        return (fresh, warn, degraded, refuse);
    }
}
