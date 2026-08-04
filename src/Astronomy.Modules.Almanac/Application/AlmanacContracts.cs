using Astronomy.SharedKernel;
using Astronomy.SharedKernel.Datasets;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.Modules.Almanac.Application;

public sealed record DailyAlmanacRequest(
    DateOnly Date,
    double LatitudeDeg,
    double LongitudeDeg,
    double ElevationMeters,
    string Precision);

public sealed record DailyAlmanacResult(string Date, CalculationMetadata Metadata);

public interface IAlmanacService
{
    Task<DailyAlmanacResult> GetDailyAsync(DailyAlmanacRequest request, CancellationToken ct);
}

internal sealed class AlmanacService : IAlmanacService
{
    public Task<DailyAlmanacResult> GetDailyAsync(DailyAlmanacRequest request, CancellationToken ct) =>
        throw new FeatureNotImplementedInPhaseException("almanac composition", "Phase 2");
}

public static class AlmanacModuleRegistrar
{
    public static IServiceCollection AddAlmanacModule(this IServiceCollection services)
    {
        services.AddSingleton<IAlmanacService, AlmanacService>();
        return services;
    }
}
