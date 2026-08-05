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
    public static readonly BodyId Mercury = new("mercury");
    public static readonly BodyId Venus = new("venus");
    public static readonly BodyId Mars = new("mars");
    public static readonly BodyId Jupiter = new("jupiter");
    public static readonly BodyId Saturn = new("saturn");
    public static readonly BodyId Uranus = new("uranus");
    public static readonly BodyId Neptune = new("neptune");

    public static readonly BodyId[] AllBodies =
    [
        Sun, Moon, Mercury, Venus, Mars, Jupiter, Saturn, Uranus, Neptune,
    ];

    public static readonly BodyId[] Planets =
    [
        Mercury, Venus, Mars, Jupiter, Saturn, Uranus, Neptune,
    ];

    public static bool TryParse(string name, out BodyId body)
    {
        body = AllBodies.FirstOrDefault(b => b.Name == name.ToLowerInvariant()) ?? new BodyId(name);
        return AllBodies.Contains(body);
    }

    public static bool TryParseList(string names, out List<BodyId> bodies)
    {
        bodies = [];
        foreach (var part in names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryParse(part, out var body)) return false;
            bodies.Add(body);
        }
        return bodies.Count > 0;
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

public enum EventType
{
    Opposition,
    Conjunction,
    MaxElongation,
}

public sealed record VisibilityResult(
    string Body,
    double Magnitude,
    double ElongationDeg,
    string VisibilityStatus,
    string? Constellation,
    double AltitudeDeg,
    double AzimuthDeg,
    bool VisibleTonight,
    bool NakedEyeVisible,
    CalculationMetadata Metadata);

public sealed record PlanetEvent(
    string Body,
    string Type,
    DateTimeOffset Utc,
    double ElongationDeg,
    CalculationMetadata Metadata);

public sealed record EventsResult(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<PlanetEvent> Events,
    CalculationMetadata Metadata);

public interface IEphemerisService
{
    Task<EphemerisPositionResult> GetPositionAsync(PositionRequest request, CancellationToken ct);
    Task<RiseSetTransitResult> GetRiseSetAsync(BodyId body, DateOnly date, ObserverLocation observer, PrecisionMode precision, CancellationToken ct);
    Task<TwilightResult> GetTwilightAsync(DateOnly date, ObserverLocation observer, TwilightType type, PrecisionMode precision, CancellationToken ct);
    Task<MoonPhasesResult> GetMoonPhasesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
    Task<MoonIlluminationResult> GetMoonIlluminationAsync(DateTimeOffset time, CancellationToken ct);
    Task<VisibilityResult> GetVisibilityAsync(BodyId body, DateTimeOffset time, ObserverLocation observer, PrecisionMode precision, CancellationToken ct);
    Task<EventsResult> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, IReadOnlyList<BodyId> bodies, IReadOnlyList<EventType> types, CancellationToken ct);
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
