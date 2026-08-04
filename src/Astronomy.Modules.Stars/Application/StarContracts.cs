using Astronomy.SharedKernel;
using Astronomy.SharedKernel.Units;
using Astronomy.SharedKernel.Coordinates;
using Astronomy.SharedKernel.Datasets;
using Astronomy.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.Modules.Stars.Application;

public sealed record ConeSearchRequest(
    Angle CenterRa,
    Angle CenterDec,
    Angle Radius,
    double MaxMagnitude,
    int Limit,
    DateTimeOffset? Time);

public sealed record StarSearchResult(
    string Catalogue,
    string CatalogueId,
    string? Name,
    double RaDeg,
    double DecDeg,
    double Vmag,
    CalculationMetadata Metadata);

public interface IStarService
{
    Task<IReadOnlyList<StarSearchResult>> ConeSearchAsync(ConeSearchRequest request, CancellationToken ct);
}

internal sealed class StarService : IStarService
{
    public Task<IReadOnlyList<StarSearchResult>> ConeSearchAsync(ConeSearchRequest request, CancellationToken ct) =>
        throw new FeatureNotImplementedInPhaseException("star catalogue queries", "Phase 4");
}

public static class StarsModuleRegistrar
{
    public static IServiceCollection AddStarsModule(this IServiceCollection services)
    {
        services.AddSingleton<IStarService, StarService>();
        return services;
    }
}
