using Astronomy.Modules.Ephemeris.Application;
using Astronomy.SharedKernel.Coordinates;

namespace Astronomy.Modules.Ephemeris.Reference;

/// <summary>
/// Reference-tier positions backed by the CSPICE chain (de441.bsp + naif0012.tls)
/// plus the ERFA correction chain (IAU 2006/2000A) for of-date and horizontal
/// frames. J2000 astrometric uses light-time only ("LT"); apparent adds stellar
/// aberration ("LT+S"). Validated era: 1900-01-01 onwards (pre-1972 epochs use
/// the Espenak-Meeus historical delta-T; 1972+ uses the leap-second path).
/// </summary>
public interface IReferenceEphemeris
{
    bool IsAvailable { get; }

    string UnavailableReason { get; }

    /// <summary>Loaded kernel files mapped to their sha256 prefixes (metadata provenance).</summary>
    IReadOnlyDictionary<string, string> KernelVersions { get; }

    ReferencePosition Position(BodyId body, DateTimeOffset utc, bool apparent);

    /// <summary>Apparent position in the true equator/equinox of date (ERFA IAU2000A rotation of the J2000 LT+S vector).</summary>
    ReferencePosition OfDatePosition(BodyId body, DateTimeOffset utc);

    /// <summary>True when EOP C04 data is loaded, enabling the ERFA C2T horizontal chain.</summary>
    bool CanDoHorizontal { get; }

    /// <summary>Topocentric alt/az via the ERFA celestial-to-terrestrial rotation fed by EOP C04 (UT1 + polar motion).</summary>
    (double AltDeg, double AzDeg) HorizontalPosition(BodyId body, DateTimeOffset utc, ObserverLocation observer, bool refraction);
}

public sealed record ReferencePosition(
    double RaDeg,
    double DecDeg,
    double DistanceKm,
    string AberrationCorrection);

/// <summary>
/// The reference chain cannot serve requests (native lib missing, kernels missing,
/// or kernel load failed). Maps to HTTP 503 / AST-5030.
/// </summary>
public sealed class ReferenceEphemerisUnavailableException(string message) : InvalidOperationException(message);
