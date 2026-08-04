using System.Globalization;

namespace S05TimeScales;

public static class TimeScales
{
    public static double UtcToTai(DateTime utc) => utc.AddSeconds(LeapSeconds.TaiMinusUtc(utc)).Ticks;
    public static DateTime TaiToUtc(DateTime tai) => tai.AddSeconds(-LeapSeconds.TaiMinusUtc(tai.AddSeconds(-50)));

    public static double TtJd(DateTime utc)
    {
        var tai = utc.AddSeconds(LeapSeconds.TaiMinusUtc(utc));
        var tt = tai.AddSeconds(LeapSeconds.TaiMinusTt);
        return Julian.ToJd(tt);
    }

    public static DateTime TtToUtc(double ttJd)
    {
        var tt = Julian.FromJd(ttJd);
        var tai = tt.AddSeconds(-LeapSeconds.TaiMinusTt);
        return tai.AddSeconds(-LeapSeconds.TaiMinusUtc(tai.AddSeconds(-50)));
    }

    public static double Ut1Jd(DateTime utc, double dut1Seconds)
    {
        var ut1 = utc.AddSeconds(dut1Seconds);
        return Julian.ToJd(ut1);
    }

    public static double TdbMinusTtSeconds(double ttJd)
    {
        var g = 357.53 + 0.9856003 * (ttJd - 2451545.0);
        var l = 246.11 + 0.90251792 * (ttJd - 2451545.0);
        var gRad = g * Math.PI / 180.0;
        var lRad = l * Math.PI / 180.0;
        return 0.001657 * Math.Sin(gRad + 0.01671 * Math.Sin(gRad)) + 0.000022 * Math.Sin(lRad);
    }

    public static async Task<double?> FetchUt1MinusUtcAsync(DateTime utc)
    {
        try
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var text = await client.GetStringAsync("https://maia.usno.navy.mil/ser7/ser7.dat");
            var targetMjd = Julian.ToMjd(utc);
            var best = (double?)null;
            foreach (var line in text.Split('\n'))
            {
                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var mjd)) continue;
                if (mjd > targetMjd + 1.5) break;
                if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var dut1))
                    best = dut1;
            }
            return best;
        }
        catch
        {
            return null;
        }
    }
}
