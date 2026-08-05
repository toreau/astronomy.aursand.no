using Astronomy.SharedKernel.Coordinates;
using CosineKitty;
using Astr = CosineKitty.Astronomy;

namespace Astronomy.Modules.Ephemeris.Application;

internal sealed record EquatorialDegrees(double RaDeg, double DecDeg, double DistanceKm);

internal sealed class EphemerisCalculator
{
    public const string EngineVersion = "2.1.19";

    public Body ToEngineBody(BodyId body) => body.Name switch
    {
        "sun" => Body.Sun,
        "moon" => Body.Moon,
        "mercury" => Body.Mercury,
        "venus" => Body.Venus,
        "mars" => Body.Mars,
        "jupiter" => Body.Jupiter,
        "saturn" => Body.Saturn,
        "uranus" => Body.Uranus,
        "neptune" => Body.Neptune,
        _ => throw new ArgumentException($"unsupported body '{body.Name}'"),
    };

    public EquatorialDegrees GeocentricEquatorial(BodyId body, DateTimeOffset utc, bool apparent)
    {
        var t = new AstroTime(utc.UtcDateTime);
        var aberration = apparent ? Aberration.Corrected : Aberration.None;
        var v = Astr.GeoVector(ToEngineBody(body), t, aberration);
        if (apparent)
        {
            var rot = Astr.Rotation_EQJ_EQD(t);
            v = Astr.RotateVector(rot, v);
        }
        var eq = Astr.EquatorFromVector(v);
        return new EquatorialDegrees(eq.ra * 15.0, eq.dec, eq.dist * 149597870.7);
    }

    public (double AltitudeDeg, double AzimuthDeg) Horizontal(
        BodyId body, DateTimeOffset utc, ObserverLocation observer, bool refraction)
    {
        var t = new AstroTime(utc.UtcDateTime);
        var eq = GeocentricEquatorial(body, utc, apparent: true);
        var engineObserver = new Observer(observer.Latitude.Degrees, observer.Longitude.Degrees, observer.ElevationMeters);
        var hor = Astr.Horizon(t, engineObserver, eq.RaDeg, eq.DecDeg,
            refraction ? Refraction.Normal : Refraction.None);
        return (hor.altitude, hor.azimuth);
    }

    public DateTimeOffset? SearchRiseSet(BodyId body, DateOnly date, ObserverLocation observer, bool rise)
    {
        var t = new AstroTime(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var engineObserver = new Observer(observer.Latitude.Degrees, observer.Longitude.Degrees, observer.ElevationMeters);
        var found = Astr.SearchRiseSet(ToEngineBody(body), engineObserver,
            rise ? Direction.Rise : Direction.Set, t, 1.5, 0.0);
        return found?.ToUtcDateTime();
    }

    public DateTimeOffset? SearchTransit(BodyId body, DateOnly date, ObserverLocation observer)
    {
        var t = new AstroTime(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var engineObserver = new Observer(observer.Latitude.Degrees, observer.Longitude.Degrees, observer.ElevationMeters);
        var found = Astr.SearchHourAngle(ToEngineBody(body), engineObserver, 0.0, t, 1);
        return found.time.ToUtcDateTime();
    }

    public DateTimeOffset? SearchAltitude(BodyId body, DateOnly date, ObserverLocation observer, double altitudeDeg, bool rising)
    {
        var t = new AstroTime(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var engineObserver = new Observer(observer.Latitude.Degrees, observer.Longitude.Degrees, observer.ElevationMeters);
        var found = Astr.SearchAltitude(ToEngineBody(body), engineObserver,
            rising ? Direction.Rise : Direction.Set, t, 1.5, altitudeDeg);
        return found?.ToUtcDateTime();
    }

    public List<(DateTimeOffset Utc, int Quarter)> MoonQuarters(DateTimeOffset from, DateTimeOffset to)
    {
        var result = new List<(DateTimeOffset, int)>();
        var t = new AstroTime(from.UtcDateTime);
        var end = new AstroTime(to.UtcDateTime);
        while (t.tt < end.tt)
        {
            var q = Astr.SearchMoonQuarter(t);
            if (q.time.tt > end.tt) break;
            result.Add((q.time.ToUtcDateTime(), q.quarter));
            t = q.time.AddDays(1.0);
        }
        return result;
    }

    public (double Fraction, double PhaseAngleDeg) MoonIllumination(DateTimeOffset utc)
    {
        var info = Astr.Illumination(Body.Moon, new AstroTime(utc.UtcDateTime));
        return (info.phase_fraction, info.phase_angle);
    }

    public (double Fraction, double PhaseAngleDeg) IlluminationFor(BodyId body, DateTimeOffset utc)
    {
        var info = Astr.Illumination(ToEngineBody(body), new AstroTime(utc.UtcDateTime));
        return (info.phase_fraction, info.phase_angle);
    }

    public (double ElongationDeg, string Visibility, double EclipticSeparationDeg) Elongation(BodyId body, DateTimeOffset utc)
    {
        var info = Astr.Elongation(ToEngineBody(body), new AstroTime(utc.UtcDateTime));
        return (info.elongation, info.visibility.ToString(), info.ecliptic_separation);
    }

    public string ConstellationOf(BodyId body, DateTimeOffset utc)
    {
        var eq = GeocentricEquatorial(body, utc, apparent: false);
        var info = Astr.Constellation(eq.RaDeg / 15.0, eq.DecDeg);
        return info.Name;
    }

    public DateTimeOffset? NextRelativeLongitude(BodyId body, double targetRelativeLongitudeDeg, DateTimeOffset from)
    {
        var found = Astr.SearchRelativeLongitude(ToEngineBody(body), targetRelativeLongitudeDeg, new AstroTime(from.UtcDateTime));
        return found.ToUtcDateTime();
    }

    public DateTimeOffset? NextMaxElongation(BodyId body, DateTimeOffset from)
    {
        var found = Astr.SearchMaxElongation(ToEngineBody(body), new AstroTime(from.UtcDateTime));
        return found.time.ToUtcDateTime();
    }

    public static string MoonPhaseName(int quarter) => quarter switch
    {
        0 => "New Moon",
        1 => "First Quarter",
        2 => "Full Moon",
        3 => "Last Quarter",
        _ => "Unknown",
    };

    public static string MoonPhaseNameFromIllumination(double fraction, double phaseAngleDeg)
    {
        if (fraction < 0.03) return "New Moon";
        if (fraction > 0.97) return "Full Moon";
        if (Math.Abs(phaseAngleDeg) < 90) return fraction < 0.5 ? "Waxing Crescent" : "Waxing Gibbous";
        return fraction < 0.5 ? "Waning Crescent" : "Waning Gibbous";
    }
}
