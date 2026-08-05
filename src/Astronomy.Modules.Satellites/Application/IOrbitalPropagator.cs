namespace Astronomy.Modules.Satellites.Application;

/// <summary>Position in the TEME frame (km), the native SGP4 output frame.</summary>
public readonly record struct TemeVector(double XKm, double YKm, double ZKm);

/// <summary>
/// ADR 9: SGP4 propagation behind an abstraction (One_Sgp4 chosen in S0.4:
/// bit-exact/&lt;=1m vs the Vallado reference incl. deep space, thread-safe, MIT).
/// </summary>
public interface IOrbitalPropagator
{
    TemeVector Propagate(OrbitalElementRow elements, DateTimeOffset utc);
}
