using Astronomy.SharedKernel.Coordinates;
using Astronomy.SharedKernel.Datasets;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.Modules.Ephemeris.Application;

public enum TwilightType
{
    Civil,
    Nautical,
    Astronomical,
}

public sealed record BodyId(string Name)
{
    public static readonly BodyId Sun = new("sun");
    public static readonly BodyId Moon = new("moon");

    public static bool TryParse(string name, out BodyId body)
    {
        body = name.ToLowerInvariant() switch
        {
            "sun" => Sun,
            "moon" => Moon,
            _ => new BodyId(name),
        };
        return body is { Name: "sun" or "moon" };
    }
}

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

public sealed record RiseSetTransitResult(
    string Body,
    DateTimeOffset? RiseUtc,
    DateTimeOffset? SetUtc,
    DateTimeOffset? TransitUtc,
    CalculationMetadata Metadata);

public sealed record TwilightResult(
    TwilightType Type,
    DateTimeOffset? BeginUtc,
    DateTimeOffset? EndUtc,
    CalculationMetadata Metadata);

public sealed record MoonPhaseEvent(
    DateTimeOffset Utc,
    string Phase,
    double IlluminationFraction);

public sealed record MoonPhasesResult(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<MoonPhaseEvent> Events,
    CalculationMetadata Metadata);

public sealed record MoonIlluminationResult(
    DateTimeOffset Time,
    double IlluminationFraction,
    string PhaseName,
    CalculationMetadata Metadata);

public interface IEphemerisService
{
    Task<EphemerisPositionResult> GetPositionAsync(PositionRequest request, CancellationToken ct);
    Task<RiseSetTransitResult> GetRiseSetAsync(BodyId body, DateOnly date, ObserverLocation observer, PrecisionMode precision, CancellationToken ct);
    Task<TwilightResult> GetTwilightAsync(DateOnly date, ObserverLocation observer, TwilightType type, PrecisionMode precision, CancellationToken ct);
    Task<MoonPhasesResult> GetMoonPhasesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<MoonIlluminationResult> GetMoonIlluminationAsync(DateTimeOffset time, CancellationToken ct);
}

public static class EphemerisModuleRegistrar
{
    public static IServiceCollection AddEphemerisModule(this IServiceCollection services)
    {
        services.AddSingleton<IEphemerisService>(sp =>
            new EphemerisService(sp.GetRequiredService<Astronomy.SharedKernel.Datasets.IDatasetCatalog>()));
        return services;
    }
}
