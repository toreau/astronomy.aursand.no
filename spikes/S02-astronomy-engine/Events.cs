using System.Globalization;
using CosineKitty;

namespace S02AstronomyEngine;

public static class Events
{
    private static readonly Observer Oslo = new(59.9139, 10.7522, 25.0);

    public static async Task RunRtsAsync(string startDate, int days)
    {
        Console.WriteLine($"RTS comparison vs USNO at Oslo (lat 59.9139, lon 10.7522, elev 25m), tz=0, {startDate} + {days} days");
        Console.WriteLine($"{new string(' ', 12)} | {"event",-18} | {"USNO UTC",-12} | {"Engine UTC",-12} | {"Δ min",6}");
        var start = DateTime.Parse(startDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).Date;
        var deltas = new List<double>();
        for (var i = 0; i < days; i++)
        {
            var date = start.AddDays(i);
            var day = await UsnoApi.OneDayAsync(date, 59.9139, 10.7522);
            var dayStart = new AstroTime(date);

            foreach (var (label, body, usnoPhen, engineTime) in new[]
            {
                ("sun-rise", Body.Sun, "Rise", Search(() => Astronomy.SearchRiseSet(Body.Sun, Oslo, Direction.Rise, dayStart, 1.5, 0.0))),
                ("sun-set", Body.Sun, "Set", Search(() => Astronomy.SearchRiseSet(Body.Sun, Oslo, Direction.Set, dayStart, 1.5, 0.0))),
                ("sun-transit", Body.Sun, "Upper Transit", Search(() => Astronomy.SearchHourAngle(Body.Sun, Oslo, 0.0, dayStart, 1).time)),
                ("sun-civil-twilight-start", Body.Sun, "Begin Civil Twilight", Search(() => Astronomy.SearchAltitude(Body.Sun, Oslo, Direction.Rise, dayStart, 1.5, -6.0))),
                ("moon-rise", Body.Moon, "Rise", Search(() => Astronomy.SearchRiseSet(Body.Moon, Oslo, Direction.Rise, dayStart, 1.5, 0.0))),
                ("moon-set", Body.Moon, "Set", Search(() => Astronomy.SearchRiseSet(Body.Moon, Oslo, Direction.Set, dayStart, 1.5, 0.0))),
                ("moon-transit", Body.Moon, "Upper Transit", Search(() => Astronomy.SearchHourAngle(Body.Moon, Oslo, 0.0, dayStart, 1).time)),
            })
            {
                var usnoTime = ParseUsnoTime(date, day, usnoPhen, body == Body.Sun);
                if (usnoTime == null || engineTime == null) continue;
                var deltaMin = (engineTime.ToUtcDateTime() - usnoTime.Value).TotalMinutes;
                deltas.Add(Math.Abs(deltaMin));
                Console.WriteLine($"{date:yyyy-MM-dd} | {label,-18} | {usnoTime:HH:mm}          | {engineTime:HH:mm}          | {deltaMin,6:F1}");
            }
        }
        if (deltas.Count > 0)
            Console.WriteLine($"\nsummary: N={deltas.Count} mean |Δ|={deltas.Average():F1} min, max |Δ|={deltas.Max():F1} min");
    }

    public static async Task RunPhasesAsync(int year)
    {
        Console.WriteLine($"Moon phase times vs USNO ({year}), tz=0");
        var usnoRows = await UsnoApi.PhasesForYearAsync(year);
        var engineRows = new List<(DateTime Utc, string Phase)>();
        var t = new AstroTime(new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var end = new AstroTime(new DateTime(year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        while (t.tt < end.tt)
        {
            var q = Astronomy.SearchMoonQuarter(t);
            var phaseName = q.quarter switch
            {
                1 => "First Quarter",
                2 => "Full Moon",
                3 => "Last Quarter",
                _ => "New Moon",
            };
            engineRows.Add((q.time.ToUtcDateTime(), phaseName));
            t = q.time.AddDays(1.0);
        }

        Console.WriteLine($"{"date",-12} | {"USNO",-16} | {"Engine",-16} | {"Δ min",6}");
        var deltas = new List<double>();
        foreach (var usno in usnoRows)
        {
            var engine = engineRows.OrderBy(r => Math.Abs((r.Utc - usno.Utc).TotalMinutes)).First();
            var deltaMin = (engine.Utc - usno.Utc).TotalMinutes;
            deltas.Add(Math.Abs(deltaMin));
            Console.WriteLine($"{usno.Utc:yyyy-MM-dd} | {usno.Phase,-16} | {engine.Utc:yyyy-MM-dd HH:mm,-16} | {deltaMin,6:F1}");
        }
        if (deltas.Count > 0)
            Console.WriteLine($"\nsummary: N={deltas.Count} mean |Δ|={deltas.Average():F1} min, max |Δ|={deltas.Max():F1} min");
    }

    private static DateTime? ParseUsnoTime(DateTime date, UsnoDay day, string phen, bool fromSun)
    {
        var events = fromSun ? day.Sun : day.Moon;
        var match = events.FirstOrDefault(e => e.Phen == phen);
        if (match == null || match.Time == "-----") return null;
        var hhmm = match.Time.Split(':');
        return date.AddHours(int.Parse(hhmm[0])).AddMinutes(int.Parse(hhmm[1]));
    }

    private static AstroTime? Search(Func<AstroTime?> search) => search();
}
