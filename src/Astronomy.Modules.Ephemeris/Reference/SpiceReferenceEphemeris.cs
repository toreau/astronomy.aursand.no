using Astronomy.Modules.Ephemeris.Application;

namespace Astronomy.Modules.Ephemeris.Reference;

/// <summary>
/// SPICE-backed J2000 reference ephemeris (de440s.bsp; light-time "LT" astrometric,
/// "LT+S" apparent with stellar aberration). Kernel directory from the
/// ASTRONOMY_KERNEL_PATH env var, defaulting to /data/kernels.
/// </summary>
public sealed class SpiceReferenceEphemeris : IReferenceEphemeris
{
    /// <summary>
    /// The reference tier is validated for the leap-second era. SPICE's UTC->ET
    /// conversion for pre-1972 epochs relies on extrapolated delta-AT values and
    /// disagrees with Horizons' historical delta-T by tens of seconds (measured
    /// ~40s at 1900-01-01: moon 21" vs skyfield on the same kernel).
    /// </summary>
    private static readonly DateTime LeapSecondEraStart = new(1972, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly SpiceKernelPool _pool;

    public SpiceReferenceEphemeris(string kernelDir)
    {
        _pool = new SpiceKernelPool(kernelDir);
    }

    public bool IsAvailable => _pool.IsAvailable;

    public string UnavailableReason => _pool.Reason;

    public IReadOnlyDictionary<string, string> KernelVersions => _pool.KernelVersions;

    public ReferencePosition Position(BodyId body, DateTimeOffset utc, bool apparent)
    {
        if (!_pool.IsAvailable)
            throw new ReferenceEphemerisUnavailableException(
                $"reference tier unavailable: {_pool.Reason}");

        var utcUtc = utc.ToUniversalTime().UtcDateTime;
        if (utcUtc < LeapSecondEraStart)
            throw new ArgumentException(
                "precision=advanced|reference is validated for the leap-second era (1972-01-01 onwards); use precision=consumer for earlier epochs");
        if (utcUtc > _pool.CoverageEndUtc)
            throw new ArgumentException(
                $"reference tier covers up to {_pool.CoverageEndUtc:yyyy-MM-dd} (loaded planetary kernel coverage); requested {utc:yyyy-MM-dd}");

        var abcorr = apparent ? "LT+S" : "LT";
        var et = _pool.Et(utc);
        var (xyz, _) = _pool.SpkPos(SpiceName(body, _pool.HasKernel("de440s_plus_MarsPC.bsp")), et, abcorr);
        var (rangeKm, raDeg, decDeg) = _pool.RecRad(xyz);
        return new ReferencePosition(raDeg, decDeg, rangeKm, abcorr);
    }

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
}
