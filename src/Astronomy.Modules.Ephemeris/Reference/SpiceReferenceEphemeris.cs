using Astronomy.Modules.Ephemeris.Application;
using Astronomy.SharedKernel.Coordinates;
using Astronomy.SharedKernel.Time;

namespace Astronomy.Modules.Ephemeris.Reference;

/// <summary>
/// SPICE-backed reference ephemeris with the ERFA correction chain:
/// J2000 astrometric ("LT") / apparent ("LT+S") positions, of-date apparent
/// positions (IAU 2006/2000A precession-nutation via eraPnm06a) and topocentric
/// horizontal positions (eraC2t06a fed by EOP C04 UT1 + polar motion).
/// Validated era 1900-01-01 onwards; pre-1972 epochs use the Espenak-Meeus
/// historical delta-T instead of SPICE's extrapolated UTC-&gt;ET.
/// Kernel directory from the ASTRONOMY_KERNEL_PATH env var, defaulting to
/// /data/kernels.
/// </summary>
public sealed class SpiceReferenceEphemeris : IReferenceEphemeris
{
    private static readonly DateTime J2000Utc = new(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly SpiceKernelPool _pool;
    private readonly IReadOnlyList<EopC04Sample> _eopC04;

    public SpiceReferenceEphemeris(string kernelDir, IReadOnlyList<EopC04Sample>? eopC04 = null)
    {
        _pool = new SpiceKernelPool(kernelDir);
        _eopC04 = eopC04 ?? [];
    }

    public bool IsAvailable => _pool.IsAvailable;

    public string UnavailableReason => _pool.Reason;

    public IReadOnlyDictionary<string, string> KernelVersions => _pool.KernelVersions;

    public bool CanDoHorizontal => _eopC04.Count > 0;

    public ReferencePosition Position(BodyId body, DateTimeOffset utc, bool apparent)
    {
        EnsureAvailable();
        var utcUtc = EnsureEra(utc);
        var abcorr = apparent ? "LT+S" : "LT";
        var et = Et(utcUtc);
        var (xyz, rangeKm) = SpkVector(body, utcUtc, et, abcorr);
        var raDec = _pool.RecRad(xyz);
        return new ReferencePosition(raDec.RaDeg, raDec.DecDeg, rangeKm, abcorr);
    }

    public ReferencePosition OfDatePosition(BodyId body, DateTimeOffset utc)
    {
        EnsureAvailable();
        var utcUtc = EnsureEra(utc);
        var et = Et(utcUtc);
        var (xyz, rangeKm) = SpkVector(body, utcUtc, et, "LT+S");
        var rnpb = new double[9];
        Erfa.Pnm06a(2451545.0, et / 86400.0, rnpb);
        var rotated = RotateBy(rnpb, xyz);
        var raDec = _pool.RecRad(rotated);
        return new ReferencePosition(raDec.RaDeg, raDec.DecDeg, rangeKm, "LT+S:of-date");
    }

    public (double AltDeg, double AzDeg) HorizontalPosition(
        BodyId body, DateTimeOffset utc, ObserverLocation observer, bool refraction)
    {
        EnsureAvailable();
        var utcUtc = EnsureEra(utc);
        if (!CanDoHorizontal)
            throw new InvalidOperationException("horizontal reference chain unavailable: eop-c04 dataset not loaded");
        var et = Et(utcUtc);
        var (xyz, _) = SpkVector(body, utcUtc, et, "LT+S");

        var (dut1, xArcsec, yArcsec) = EopC04Interpolator.Interpolate(_eopC04, utc);
        // et is TDB seconds past J2000; TT-UTC follows directly. TT-UT1 = TT-UTC - (UT1-UTC).
        var ttMinusUtc = et - (utcUtc - J2000Utc).TotalSeconds;
        var ut1Jd = ttJd(et) - (ttMinusUtc - dut1) / 86400.0;

        var rc2t = new double[9];
        Erfa.C2t06a(2451545.0, et / 86400.0, 2451545.0, ut1Jd - 2451545.0,
            xArcsec * Math.PI / 180 / 3600, yArcsec * Math.PI / 180 / 3600, rc2t);
        var itrs = RotateBy(rc2t, xyz);

        // Topocentric correction: subtract the observer's geocentric ITRS position
        // so the Moon's parallax (~1 deg max) is applied.
        var (ox, oy, oz) = GeodeticToItrs(observer.Latitude.Degrees, observer.Longitude.Degrees,
            observer.ElevationMeters / 1000.0);
        var tx = itrs[0] - ox;
        var ty = itrs[1] - oy;
        var tz = itrs[2] - oz;

        var lat = observer.Latitude.Degrees * Math.PI / 180;
        var lon = observer.Longitude.Degrees * Math.PI / 180;
        var len = Math.Sqrt(tx * tx + ty * ty + tz * tz);
        var (ux, uy, uz) = (tx / len, ty / len, tz / len);

        var (ex, ey, ez) = (-Math.Sin(lon), Math.Cos(lon), 0.0);
        var (nx, ny, nz) = (-Math.Sin(lat) * Math.Cos(lon), -Math.Sin(lat) * Math.Sin(lon), Math.Cos(lat));
        var (px, py, pz) = (Math.Cos(lat) * Math.Cos(lon), Math.Cos(lat) * Math.Sin(lon), Math.Sin(lat));

        var alt = Math.Asin(Math.Clamp(ux * px + uy * py + uz * pz, -1, 1)) * 180 / Math.PI;
        var az = Math.Atan2(ux * ex + uy * ey + uz * ez, ux * nx + uy * ny + uz * nz) * 180 / Math.PI;
        if (az < 0) az += 360;
        if (refraction && alt > -1.0)
        {
            // Bennett's refraction formula (standard, ~0.5" accuracy to 85 deg).
            var r = 0.0167 / Math.Tan((alt + 7.31 / (alt + 4.4)) * Math.PI / 180);
            alt += r;
        }
        return (alt, az);
    }

    private void EnsureAvailable()
    {
        if (!_pool.IsAvailable)
            throw new ReferenceEphemerisUnavailableException(
                $"reference tier unavailable: {_pool.Reason}");
    }

    private DateTime EnsureEra(DateTimeOffset utc)
    {
        var utcUtc = utc.ToUniversalTime().UtcDateTime;
        if (utcUtc < HistoricalDeltaT.EraStartUtc)
            throw new ArgumentException(
                "precision=advanced|reference is validated from 1900-01-01 (historical delta-T era); use precision=consumer for earlier epochs");
        if (utcUtc > _pool.CoverageEndUtc)
            throw new ArgumentException(
                $"reference tier covers up to {_pool.CoverageEndUtc:yyyy-MM-dd} (loaded planetary kernel coverage); requested {utc:yyyy-MM-dd}");
        return utcUtc;
    }

    /// <summary>
    /// ET (TDB seconds past J2000): the leap-second path via SPICE for 1972+,
    /// otherwise TT from the historical delta-T (TDB-TT ~1.6ms is neglected -
    /// sub-milliarcsecond at the moon's rate).
    /// </summary>
    private double Et(DateTime utcUtc) =>
        utcUtc >= HistoricalDeltaT.LeapSecondEraStartUtc
            ? _pool.Et(new DateTimeOffset(utcUtc, TimeSpan.Zero))
            : (utcUtc - J2000Utc).TotalSeconds + HistoricalDeltaT.Seconds(utcUtc);

    private (double[] Xyz, double RangeKm) SpkVector(BodyId body, DateTime utcUtc, double et, string abcorr)
    {
        var target = SpiceName(body, MarsCenterAvailable(utcUtc));
        var (xyz, _) = _pool.SpkPos(target, et, abcorr);
        var (rangeKm, _, _) = _pool.RecRad(xyz);
        return (xyz, rangeKm);
    }

    private static double ttJd(double et) => 2451545.0 + et / 86400.0;

    private static double[] RotateBy(double[] matrix, double[] vector)
    {
        var result = new double[3];
        for (var i = 0; i < 3; i++)
            result[i] = matrix[i * 3] * vector[0] + matrix[i * 3 + 1] * vector[1] + matrix[i * 3 + 2] * vector[2];
        return result;
    }

    /// <summary>
    /// de440s_plus_MarsPC.bsp (JPL) provides the Mars planet-center segment for
    /// 1950-01-01..2050-01-01; outside that window the de440/de441 Mars
    /// barycenter is used (center-vs-barycenter offset <= 0.05").
    /// </summary>
    private static readonly DateTime MarsPcStart = new(1950, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime MarsPcEnd = new(2050, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private bool MarsCenterAvailable(DateTime utc) =>
        _pool.HasKernel("de440s_plus_MarsPC.bsp") && utc >= MarsPcStart && utc < MarsPcEnd;

    /// <summary>
    /// The DE-series SPKs (de441/de440/de440s) provide planet CENTER segments for
    /// Sun, Moon, Mercury, Venus (and Earth); for the outer planets only BARYCENTER
    /// segments exist. The barycenter-vs-center offset is <= 0.05" for all outer
    /// planets (Jupiter ~100 km at 5.2 AU), so barycenter targets are used for
    /// Mars..Neptune unless de440s_plus_MarsPC.bsp is loaded (Mars planet center).
    /// </summary>
    private static string SpiceName(BodyId body, bool marsCenterLoaded) => body.Name switch
    {
        "sun" => "SUN",
        "moon" => "MOON",
        "mercury" => "MERCURY",
        "venus" => "VENUS",
        "mars" => marsCenterLoaded ? "MARS" : "MARS BARYCENTER",
        "jupiter" => "JUPITER BARYCENTER",
        "saturn" => "SATURN BARYCENTER",
        "uranus" => "URANUS BARYCENTER",
        "neptune" => "NEPTUNE BARYCENTER",
        _ => throw new ArgumentException($"unsupported body '{body.Name}'"),
    };

    /// <summary>
    /// Observer geocentric position in ITRS (km) from geodetic coordinates on the
    /// WGS-84 ellipsoid. Used for the topocentric (parallax) correction in the
    /// horizontal chain.
    /// </summary>
    internal static (double X, double Y, double Z) GeodeticToItrs(double latDeg, double lonDeg, double altKm)
    {
        const double a = 6378.137;            // WGS-84 equatorial radius (km)
        const double f = 1.0 / 298.257223563; // WGS-84 flattening
        var e2 = f * (2.0 - f);
        var lat = latDeg * Math.PI / 180;
        var lon = lonDeg * Math.PI / 180;
        var sinLat = Math.Sin(lat);
        var n = a / Math.Sqrt(1.0 - e2 * sinLat * sinLat);
        return ((n + altKm) * Math.Cos(lat) * Math.Cos(lon),
                (n + altKm) * Math.Cos(lat) * Math.Sin(lon),
                (n * (1.0 - e2) + altKm) * sinLat);
    }
}
