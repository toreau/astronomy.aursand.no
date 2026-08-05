using Astronomy.SharedKernel.Coordinates;
using Astronomy.SharedKernel.Time;

namespace Astronomy.Modules.Satellites.Application;

public sealed record SatellitePass(
    DateTimeOffset RiseUtc,
    DateTimeOffset MaxElevationUtc,
    double MaxElevationDeg,
    DateTimeOffset SetUtc,
    string Direction,
    double MinElevationDeg);

/// <summary>
/// Pass detection over a window: coarse scan at stepSeconds, horizon crossings
/// refined by bisection (altitude is smooth), transit = the maximum-altitude
/// sample within the pass, direction from the altitude slope at rise.
/// </summary>
public static class SatellitePassPredictor
{
    public static IReadOnlyList<SatellitePass> Predict(
        IOrbitalPropagator propagator, OrbitalElementRow elements,
        DateTimeOffset from, DateTimeOffset to, ObserverLocation observer,
        double ut1MinusUtc, double minElevationDeg, double stepSeconds)
    {
        if (to <= from) return [];
        var gmstOffset = Ut1ToGmstOffset(ut1MinusUtc);
        var obsEcef = SatelliteFrames.GeodeticToEcef(observer.Latitude.Degrees, observer.Longitude.Degrees,
            observer.ElevationMeters / 1000.0);

        double AltAt(DateTimeOffset t)
        {
            var teme = propagator.Propagate(elements, t);
            var pef = SatelliteFrames.TemeToPef(teme, SatelliteFrames.GmstDegrees(Jd(t)) + gmstOffset);
            var (alt, _, _) = SatelliteFrames.Topocentric(pef.X, pef.Y, pef.Z, obsEcef.X, obsEcef.Y, obsEcef.Z,
                observer.Latitude.Degrees, observer.Longitude.Degrees, refraction: false);
            return alt;
        }

        var crossings = new List<(DateTimeOffset Time, bool Rising)>();
        var previous = AltAt(from);
        var previousAbove = previous >= minElevationDeg;
        var t = from;
        var step = TimeSpan.FromSeconds(stepSeconds);
        while (t < to)
        {
            var next = t + step;
            if (next > to) next = to;
            var alt = AltAt(next);
            var above = alt >= minElevationDeg;
            if (above != previousAbove)
            {
                // bisect the crossing
                var lo = t;
                var hi = next;
                var loAlt = previous;
                for (var i = 0; i < 12; i++)
                {
                    var mid = lo + (hi - lo) / 2.0;
                    var midAlt = AltAt(mid);
                    if ((midAlt >= minElevationDeg) == above) { hi = mid; }
                    else { lo = mid; loAlt = midAlt; }
                }
                crossings.Add((hi, above));
            }
            previous = alt;
            previousAbove = above;
            t = next;
        }

        var passes = new List<SatellitePass>();
        for (var i = 0; i < crossings.Count; i++)
        {
            var rise = crossings[i];
            if (!rise.Rising) continue;
            DateTimeOffset? set = null;
            for (var j = i + 1; j < crossings.Count; j++)
            {
                if (!crossings[j].Rising) { set = crossings[j].Time; break; }
            }
            if (set is null) continue;

            // transit: max altitude over a fine rescan of the pass
            var best = rise.Time;
            var bestAlt = double.MinValue;
            var fineStep = TimeSpan.FromSeconds(Math.Max(10, Math.Min(stepSeconds, 60)));
            for (var ft = rise.Time; ft <= set.Value; ft += fineStep)
            {
                var alt = AltAt(ft);
                if (alt > bestAlt) { bestAlt = alt; best = ft; }
            }
            var riseAltSlope = AltAt(rise.Time.AddSeconds(10)) - AltAt(rise.Time);
            passes.Add(new SatellitePass(rise.Time, best, bestAlt, set.Value,
                riseAltSlope >= 0 ? "ascending" : "descending", minElevationDeg));
            // continue scanning after this pass's set crossing
            var setIndex = crossings.FindIndex(i + 1, c => !c.Rising);
            if (setIndex > i) i = setIndex;
        }
        return passes;
    }

    private static double Jd(DateTimeOffset utc) => 2451545.0 + (utc - new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero)).TotalDays;

    /// <summary>GMST is a function of UT1; the UTC-based GMST shifted by the UT1-UTC offset (deg).</summary>
    private static double Ut1ToGmstOffset(double ut1MinusUtcSeconds) => ut1MinusUtcSeconds * 360.0 / 86164.0905;
}
