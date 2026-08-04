using System.Globalization;
using System.Net.Http.Headers;
using CosineKitty;

namespace S02AstronomyEngine;

public sealed record FixtureRow(
    DateTime Utc,
    double RaJ2000Deg, double DecJ2000Deg,
    double RaOfDateDeg, double DecOfDateDeg,
    double DistAu);

public static class HorizonsApi
{
    private const string Endpoint = "https://ssd.jpl.nasa.gov/api/horizons.api";

    public static readonly Dictionary<string, (int Id, Body EngineBody)> Bodies = new()
    {
        ["sun"] = (10, Body.Sun),
        ["moon"] = (301, Body.Moon),
        ["mercury"] = (199, Body.Mercury),
        ["venus"] = (299, Body.Venus),
        ["mars"] = (499, Body.Mars),
        ["jupiter"] = (599, Body.Jupiter),
        ["saturn"] = (699, Body.Saturn),
        ["uranus"] = (799, Body.Uranus),
        ["neptune"] = (899, Body.Neptune),
        ["pluto"] = (999, Body.Pluto),
    };

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("astronomy-phase0-spike", "1.0"));
        return c;
    }

    public static async Task<List<FixtureRow>> FetchAsync(
        string bodyName, DateTime start, DateTime stop, string step)
    {
        var (id, _) = Bodies[bodyName];
        var query = string.Join("&", new[]
        {
            "format=text",
            $"COMMAND='{id}'",
            "OBJ_DATA='NO'",
            "MAKE_EPHEM='YES'",
            "EPHEM_TYPE='OBSERVER'",
            "CENTER='500@399'",
            $"START_TIME='{start:yyyy-MM-dd}'",
            $"STOP_TIME='{stop:yyyy-MM-dd}'",
            $"STEP_SIZE='{step}'",
            "QUANTITIES='1,2,9'",
            "CSV_FORMAT='YES'",
            "ANG_FORMAT='DEG'",
            "CAL_FORMAT='CAL'",
            "EXTRA_PREC='NO'",
        });

        var url = $"{Endpoint}?{query}";
        var text = await Http.GetStringAsync(url);
        return Parse(text, bodyName);
    }

    public static List<FixtureRow> Parse(string responseText, string bodyName)
    {
        var rows = new List<FixtureRow>();
        var inData = false;
        foreach (var rawLine in responseText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line == "$$SOE") { inData = true; continue; }
            if (line == "$$EOE") { break; }
            if (!inData || line.Length == 0 || line.StartsWith('#')) continue;

            var cols = line.Split(',').Select(c => c.Trim().Trim('"')).ToArray();
            if (cols.Length < 6) continue;
            if (!DateTime.TryParseExact(cols[0], "yyyy-MMM-dd HH:mm:ss.ffff",
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var utc))
            {
                Console.WriteLine($"[warn] {bodyName}: unparseable date '{cols[0]}'");
                continue;
            }
            var ra1 = double.Parse(cols[1], CultureInfo.InvariantCulture);
            var dec1 = double.Parse(cols[2], CultureInfo.InvariantCulture);
            var ra2 = double.Parse(cols[3], CultureInfo.InvariantCulture);
            var dec2 = double.Parse(cols[4], CultureInfo.InvariantCulture);
            var dist = double.Parse(cols[5], CultureInfo.InvariantCulture);
            rows.Add(new FixtureRow(utc, ra1, dec1, ra2, dec2, dist));
        }
        return rows;
    }
}
