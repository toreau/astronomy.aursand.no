using Astronomy.SharedKernel;
using Astronomy.SharedKernel.Coordinates;
using Astronomy.SharedKernel.Datasets;
using Astronomy.SharedKernel.Time;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.Modules.Satellites.Application;

public sealed record OrbitalElementRow(
    string Name,
    string NoradId,
    DateTimeOffset EpochUtc,
    double MeanMotion,
    double Eccentricity,
    double Inclination,
    double RaOfAscNode,
    double ArgOfPericenter,
    double MeanAnomaly,
    double Bstar,
    double MmDot,
    double MmDdot,
    int RevAtEpoch);

public sealed record ElementValidationError(int Row, string Field, string Value);

public sealed record IngestionStatus(
    string? ActiveVersion,
    int ElementCount,
    int Fresh,
    int Warn,
    int Degraded,
    int Refuse);

public sealed record SatellitePositionResult(
    string NoradId,
    string Name,
    double AltitudeDeg,
    double AzimuthDeg,
    double RangeKm,
    double SubpointLatDeg,
    double SubpointLonDeg,
    double SubpointAltKm,
    double TleAgeHours,
    CalculationMetadata Metadata);

public sealed record SatellitePassesResult(
    string NoradId,
    string Name,
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<SatellitePass> Passes,
    CalculationMetadata Metadata);

public sealed record SatelliteSearchResult(
    string NoradId,
    string Name,
    DateTimeOffset EpochUtc,
    double TleAgeHours);

public interface ISatelliteElementIngestionService
{
    Task<int> FetchAndStageAsync(string version, CancellationToken ct = default);
    Task<int> StageFileAsync(string version, string csvPath, CancellationToken ct = default);
    Task<int> ActivateAsync(string version, CancellationToken ct = default);
    Task<int> RollbackAsync(string version, CancellationToken ct = default);
    Task<IngestionStatus> GetStatusAsync(CancellationToken ct = default);
}

/// <summary>
/// The satellite-elements dataset is not ingested/active. Maps to 503 / AST-5032.
/// </summary>
public sealed class SatelliteElementsUnavailableException(string message) : InvalidOperationException(message);

public interface ISatelliteService
{
    Task<SatellitePositionResult> GetPositionAsync(string noradId, DateTimeOffset time, ObserverLocation observer, bool refraction, CancellationToken ct);
    Task<SatellitePassesResult> GetPassesAsync(string noradId, DateTimeOffset from, DateTimeOffset to, ObserverLocation observer, double minElevationDeg, double stepSeconds, CancellationToken ct);
    Task<IReadOnlyList<SatelliteSearchResult>> SearchAsync(string name, CancellationToken ct);
    Task<IngestionStatus> GetStatusAsync(CancellationToken ct);
}

public static class SatellitesModuleRegistrar
{
    public static IServiceCollection AddSatellitesModule(this IServiceCollection services, string dbPath)
    {
        services.AddSingleton<ISatelliteElementIngestionService>(sp =>
            new SatelliteElementIngestionService(dbPath, sp.GetRequiredService<Astronomy.SharedKernel.Datasets.IDatasetRegistry>()));
        services.AddSingleton<ISatelliteService>(sp =>
            new SatelliteService(dbPath,
                sp.GetRequiredService<Astronomy.SharedKernel.Datasets.IDatasetRegistry>(),
                sp.GetRequiredService<TimeScaleConverter>(),
                cache: sp.GetService<IMemoryCache>()));
        return services;
    }
}
