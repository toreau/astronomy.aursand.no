using Astronomy.SharedKernel.Coordinates;

namespace Astronomy.Modules.Satellites.Application;

/// <summary>
/// Frame transforms for SGP4 output: TEME -&gt; PEF (GMST rotation) -&gt; geodetic
/// subpoint and observer-topocentric alt/az/range. WGS-72 constants match the
/// propagator. The TEME pole offset vs the true equator (~9") and the
/// equation-of-the-equinoxes term (~1") are below pass-prediction needs.
/// </summary>
public static class SatelliteFrames
{
    public const double EarthRadiusKm = 6378.135;  // WGS-72
    public const double Flattening = 1.0 / 298.26; // WGS-72

    public static double GmstDegrees(double ut1Jd)
    {
        var t = (ut1Jd - 2451545.0) / 36525.0;
        var gmst = 280.46061837 + 360.98564736629 * (ut1Jd - 2451545.0)
                   + 0.000387933 * t * t - t * t * t / 38710000.0;
        gmst %= 360.0;
        if (gmst < 0) gmst += 360.0;
        return gmst;
    }

    /// <summary>Rotates TEME into the pseudo-Earth-fixed frame by GMST.</summary>
    public static (double X, double Y, double Z) TemeToPef(TemeVector teme, double gmstDeg)
    {
        var th = gmstDeg * Math.PI / 180.0;
        var ct = Math.Cos(th);
        var st = Math.Sin(th);
        return (teme.XKm * ct + teme.YKm * st,
                -teme.XKm * st + teme.YKm * ct,
                teme.ZKm);
    }

    /// <summary>Geodetic position from ECEF (WGS-72), Bowring-style iteration.</summary>
    public static (double LatDeg, double LonDeg, double AltKm) GeodeticFromEcef(double x, double y, double z)
    {
        var lon = Math.Atan2(y, x);
        var p = Math.Sqrt(x * x + y * y);
        var e2 = Flattening * (2.0 - Flattening);
        var lat = Math.Atan2(z, p * (1.0 - e2));
        double alt = 0;
        for (var i = 0; i < 4; i++)
        {
            var sinLat = Math.Sin(lat);
            var n = EarthRadiusKm / Math.Sqrt(1.0 - e2 * sinLat * sinLat);
            alt = p / Math.Cos(lat) - n;
            lat = Math.Atan2(z, p * (1.0 - e2 * n / (n + alt)));
        }
        return (lat * 180.0 / Math.PI, lon * 180.0 / Math.PI, alt);
    }

    public static (double X, double Y, double Z) GeodeticToEcef(double latDeg, double lonDeg, double altKm)
    {
        var lat = latDeg * Math.PI / 180.0;
        var lon = lonDeg * Math.PI / 180.0;
        var e2 = Flattening * (2.0 - Flattening);
        var sinLat = Math.Sin(lat);
        var n = EarthRadiusKm / Math.Sqrt(1.0 - e2 * sinLat * sinLat);
        return ((n + altKm) * Math.Cos(lat) * Math.Cos(lon),
                (n + altKm) * Math.Cos(lat) * Math.Sin(lon),
                (n * (1.0 - e2) + altKm) * sinLat);
    }

    /// <summary>Topocentric alt/az/range of an ECEF satellite from an ECEF observer.</summary>
    public static (double AltDeg, double AzDeg, double RangeKm) Topocentric(
        double satX, double satY, double satZ, double obsX, double obsY, double obsZ,
        double obsLatDeg, double obsLonDeg, bool refraction)
    {
        var tx = satX - obsX;
        var ty = satY - obsY;
        var tz = satZ - obsZ;
        var range = Math.Sqrt(tx * tx + ty * ty + tz * tz);
        if (range < 1e-9) return (0, 0, 0);

        var lat = obsLatDeg * Math.PI / 180.0;
        var lon = obsLonDeg * Math.PI / 180.0;
        var (ex, ey, ez) = (-Math.Sin(lon), Math.Cos(lon), 0.0);
        var (nx, ny, nz) = (-Math.Sin(lat) * Math.Cos(lon), -Math.Sin(lat) * Math.Sin(lon), Math.Cos(lat));
        var (px, py, pz) = (Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));

        var alt = Math.Asin(Math.Clamp((tx * px + ty * py + tz * pz) / range, -1, 1)) * 180.0 / Math.PI;
        var az = Math.Atan2((tx * ex + ty * ey + tz * ez) / range, (tx * nx + ty * ny + tz * nz) / range) * 180.0 / Math.PI;
        if (az < 0) az += 360.0;
        if (refraction && alt > -1.0)
        {
            var r = 0.0167 / Math.Tan((alt + 7.31 / (alt + 4.4)) * Math.PI / 180.0);
            alt += r;
        }
        return (alt, az, range);
    }

    public static ObserverLocation ObserverFromDegrees(double latitude, double longitude, double? elevationMeters) =>
        ObserverLocation.FromDegrees(latitude, longitude, (elevationMeters ?? 0) / 1000.0);
}
