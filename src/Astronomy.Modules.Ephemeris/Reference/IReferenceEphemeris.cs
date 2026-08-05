using Astronomy.Modules.Ephemeris.Application;

namespace Astronomy.Modules.Ephemeris.Reference;

/// <summary>
/// J2000 reference-tier positions backed by the CSPICE chain (de440s.bsp + naif0012.tls).
/// Astrometric uses light-time only ("LT"); apparent adds stellar aberration ("LT+S").
/// Phase 4 scope: J2000 frames only; of-date/horizontal reference positions are not
/// computed by this tier (of-date requests are rejected, horizontal falls back with a warning).
/// </summary>
public interface IReferenceEphemeris
{
    bool IsAvailable { get; }

    string UnavailableReason { get; }

    /// <summary>Loaded kernel files mapped to their sha256 prefixes (metadata provenance).</summary>
    IReadOnlyDictionary<string, string> KernelVersions { get; }

    ReferencePosition Position(BodyId body, DateTimeOffset utc, bool apparent);
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
