using Astronomy.SharedKernel;
using Astronomy.SharedKernel.Coordinates;
using Astronomy.SharedKernel.Datasets;
using Astronomy.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.Modules.Ephemeris.Application;

public sealed record PositionRequest(
    string Body,
    DateTimeOffset Time,
    ObserverLocation Observer,
    CoordinateFrame Frame,
    PositionType PositionType,
    RefractionModel Refraction,
    PrecisionMode Precision);

public sealed record EphemerisPositionResult(
    string Body,
    double RightAscensionDeg,
    double DeclinationDeg,
    double? AltitudeDeg,
    double? AzimuthDeg,
    double DistanceKm,
    CalculationMetadata Metadata);

public interface IEphemerisService
{
    Task<EphemerisPositionResult> GetPositionAsync(PositionRequest request, CancellationToken ct);
}

internal sealed class EphemerisService : IEphemerisService
{
    public Task<EphemerisPositionResult> GetPositionAsync(PositionRequest request, CancellationToken ct) =>
        throw new FeatureNotImplementedInPhaseException("ephemeris positions", "Phase 2");
}

public static class EphemerisModuleRegistrar
{
    public static IServiceCollection AddEphemerisModule(this IServiceCollection services)
    {
        services.AddSingleton<IEphemerisService, EphemerisService>();
        return services;
    }
}
