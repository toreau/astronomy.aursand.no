using Astronomy.Modules.Ephemeris.Application;

namespace Astronomy.Modules.Ephemeris.Reference;

/// <summary>
/// SPICE-backed J2000 reference ephemeris (de440s.bsp; light-time "LT" astrometric,
/// "LT+S" apparent with stellar aberration). Kernel directory from the
/// ASTRONOMY_KERNEL_PATH env var, defaulting to /data/kernels.
/// </summary>
public sealed class SpiceReferenceEphemeris : IReferenceEphemeris
{
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
        if (utcUtc < _pool.CoverageStartUtc || utcUtc > _pool.CoverageEndUtc)
            throw new ArgumentException(
                $"reference tier covers {_pool.CoverageStartUtc:yyyy-MM-dd}..{_pool.CoverageEndUtc:yyyy-MM-dd} (loaded planetary kernel coverage); requested {utc:yyyy-MM-dd}");

        var abcorr = apparent ? "LT+S" : "LT";
        var et = _pool.Et(utc);
        var (xyz, _) = _pool.SpkPos(SpiceName(body), et, abcorr);
        var (rangeKm, raDeg, decDeg) = _pool.RecRad(xyz);
        return new ReferencePosition(raDeg, decDeg, rangeKm, abcorr);
    }

    private static string SpiceName(BodyId body) => body.Name switch
    {
        "sun" => "SUN",
        "moon" => "MOON",
        "mercury" => "MERCURY",
        "venus" => "VENUS",
        "mars" => "MARS",
        "jupiter" => "JUPITER",
        "saturn" => "SATURN",
        "uranus" => "URANUS",
        "neptune" => "NEPTUNE",
        _ => throw new ArgumentException($"unsupported body '{body.Name}'"),
    };
}
