namespace Astronomy.Modules.Ephemeris.Reference;

/// <summary>
/// Historical delta-T (TT - UT1) for the pre-leap-second era (1900-1971), from
/// Espenak &amp; Meeus, "Five Millennium Canon of Solar Eclipses" (2006),
/// piecewise polynomials. SPICE's UTC-&gt;ET conversion for pre-1972 epochs
/// extrapolates delta-AT and disagrees with Horizons by tens of seconds
/// (measured ~40s at 1900); this table replaces it for the validated era
/// 1900-01-01 onwards. For 1972+ the leap-second path is used instead.
/// </summary>
public static class HistoricalDeltaT
{
    public static readonly DateTime EraStartUtc = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static readonly DateTime LeapSecondEraStartUtc = new(1972, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>TT - UTC for the given UTC instant in the pre-1972 era (seconds).</summary>
    public static double Seconds(DateTime utc)
    {
        var year = utc.Year + (utc.Month - 1) / 12.0 + (utc.Day - 1) / 365.25;
        double t;
        if (year < 1900) t = -2.79 + 0.494119 * (year - 1900); // crude extrapolation below the validated floor
        else if (year < 1920) { t = year - 1900; return -2.79 + 1.494119 * t - 0.0598939 * t * t + 0.0061966 * t * t * t - 0.000197 * t * t * t * t; }
        else if (year < 1941) { t = year - 1920; return 21.20 + 0.84493 * t - 0.076100 * t * t + 0.0020936 * t * t * t; }
        else if (year < 1961) { t = year - 1950; return 29.07 + 0.407 * t - t * t / 233.0 + t * t * t / 2547.0; }
        else if (year < 1986) { t = year - 1975; return 45.45 + 1.067 * t - t * t / 260.0 - t * t * t / 718.0; }
        else { t = year - 2000; return 63.86 + 0.3345 * t - 0.060374 * t * t + 0.0017275 * t * t * t + 0.000651814 * t * t * t * t + 0.00002373599 * t * t * t * t * t; }
        return t;
    }
}
