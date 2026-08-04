using System.Globalization;
using System.Text.Json;

namespace S02AstronomyEngine;

public sealed record UsnoEvent(string Phen, string Time);

public sealed record UsnoDay(
    DateTime DateUtc,
    IReadOnlyList<UsnoEvent> Sun,
    IReadOnlyList<UsnoEvent> Moon,
    string CurPhase,
    int FracIllum,
    (int Year, int Month, int Day, string Time, string Phase)? ClosestPhase);

public sealed record UsnoPhaseRow(DateTime Utc, string Phase);

public static class UsnoApi
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static async Task<UsnoDay> OneDayAsync(DateTime dateUtc, double lat, double lon)
    {
        var url = $"https://aa.usno.navy.mil/api/rstt/oneday?date={dateUtc:yyyy-MM-dd}" +
                  $"&coords={lat.ToString("F4", CultureInfo.InvariantCulture)},{lon.ToString("F4", CultureInfo.InvariantCulture)}&tz=0";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        var data = doc.RootElement.GetProperty("properties").GetProperty("data");
        var sun = ParseEvents(data.GetProperty("sundata"));
        var moon = ParseEvents(data.GetProperty("moondata"));
        var cur = data.TryGetProperty("curphase", out var cp) ? cp.GetString() ?? "" : "";
        var illum = data.TryGetProperty("fracillum", out var fi) && fi.ValueKind == JsonValueKind.String
            ? int.TryParse(fi.GetString()!.TrimEnd('%'), out var p) ? p : 0
            : 0;
        (int, int, int, string, string)? closest = null;
        if (data.TryGetProperty("closestphase", out var cph) && cph.ValueKind == JsonValueKind.Object)
        {
            closest = (
                cph.GetProperty("year").GetInt32(),
                cph.GetProperty("month").GetInt32(),
                cph.GetProperty("day").GetInt32(),
                cph.GetProperty("time").GetString() ?? "",
                cph.GetProperty("phase").GetString() ?? "");
        }
        return new UsnoDay(dateUtc, sun, moon, cur, illum, closest);
    }

    public static async Task<List<UsnoPhaseRow>> PhasesForYearAsync(int year)
    {
        var url = $"https://aa.usno.navy.mil/api/moon/phases/year?year={year}&tz=0";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        var rows = new List<UsnoPhaseRow>();
        if (!doc.RootElement.TryGetProperty("phasedata", out var phasedata)) return rows;
        foreach (var item in phasedata.EnumerateArray())
        {
            var (y, m, d) = (item.GetProperty("year").GetInt32(), item.GetProperty("month").GetInt32(), item.GetProperty("day").GetInt32());
            var time = item.GetProperty("time").GetString() ?? "00:00";
            if (DateTime.TryParseExact($"{d:00}:{time}", "dd:HH:mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc))
            {
                rows.Add(new UsnoPhaseRow(new DateTime(y, m, utc.Day, utc.Hour, utc.Minute, 0, DateTimeKind.Utc), item.GetProperty("phase").GetString() ?? ""));
            }
        }
        return rows;
    }

    private static List<UsnoEvent> ParseEvents(JsonElement el)
    {
        var list = new List<UsnoEvent>();
        foreach (var item in el.EnumerateArray())
            list.Add(new UsnoEvent(item.GetProperty("phen").GetString() ?? "", item.GetProperty("time").GetString() ?? ""));
        return list;
    }
}
