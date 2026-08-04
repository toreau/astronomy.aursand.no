using Astronomy.SharedKernel;
using Astronomy.SharedKernel.Datasets;
using Microsoft.EntityFrameworkCore;
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

public interface ISatelliteElementIngestionService
{
    Task<int> FetchAndStageAsync(string version, CancellationToken ct = default);
    Task<int> StageFileAsync(string version, string csvPath, CancellationToken ct = default);
    Task<int> ActivateAsync(string version, CancellationToken ct = default);
    Task<int> RollbackAsync(string version, CancellationToken ct = default);
    Task<IngestionStatus> GetStatusAsync(CancellationToken ct = default);
}

public interface ISatelliteService
{
    Task<object> GetPositionAsync(string noradId, DateTimeOffset time, CancellationToken ct);
}

internal sealed class SatelliteService : ISatelliteService
{
    public Task<object> GetPositionAsync(string noradId, DateTimeOffset time, CancellationToken ct) =>
        throw new FeatureNotImplementedInPhaseException("satellite propagation", "Phase 5");
}

public static class SatellitesModuleRegistrar
{
    public static IServiceCollection AddSatellitesModule(this IServiceCollection services, string dbPath)
    {
        services.AddSingleton<ISatelliteElementIngestionService>(sp =>
            new SatelliteElementIngestionService(dbPath, sp.GetRequiredService<Astronomy.SharedKernel.Datasets.IDatasetRegistry>()));
        services.AddSingleton<ISatelliteService, SatelliteService>();
        return services;
    }
}
