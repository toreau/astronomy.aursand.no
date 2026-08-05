using Astronomy.SharedKernel.Coordinates;
using Astronomy.SharedKernel.Datasets;
using Astronomy.SharedKernel.Units;
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

public sealed record StarPosition(double RaDeg, double DecDeg, double? AltDeg, double? AzDeg);

public sealed record StarDetailResult(
    string Hip,
    string Name,
    string BayerFlamsteed,
    string Constellation,
    double Vmag,
    string SpectralType,
    double DistLightYears,
    StarPosition Position,
    CalculationMetadata Metadata);

public sealed record StarEventsResult(
    string Hip,
    DateTimeOffset? RiseUtc,
    DateTimeOffset? SetUtc,
    DateTimeOffset? TransitUtc,
    bool Circumpolar,
    CalculationMetadata Metadata);

public sealed record StarListItem(
    string Hip,
    string Name,
    string Constellation,
    double RaDeg,
    double DecDeg,
    double Vmag);

public sealed record StarListResult(
    IReadOnlyList<StarListItem> Stars,
    CalculationMetadata Metadata);

/// <summary>
/// The star catalog dataset is not ingested/active. Maps to HTTP 503 / AST-5031.
/// </summary>
public sealed class StarCatalogUnavailableException(string message) : InvalidOperationException(message);

public interface IStarService
{
    Task<IReadOnlyList<StarSearchResult>> ConeSearchAsync(ConeSearchRequest request, CancellationToken ct);
    Task<StarDetailResult> GetStarAsync(string hip, DateTimeOffset time, CoordinateFrame frame, PositionType positionType, ObserverLocation observer, bool refraction, CancellationToken ct);
    Task<IReadOnlyList<StarSearchResult>> SearchByNameAsync(string query, CancellationToken ct);
    Task<StarEventsResult> GetRiseSetAsync(string hip, DateOnly date, ObserverLocation observer, CancellationToken ct);
    Task<StarListResult> GetBrightestAsync(int limit, double maxMagnitude, string? constellation, CancellationToken ct);
}

public static class StarsModuleRegistrar
{
    public static IServiceCollection AddStarsModule(this IServiceCollection services)
    {
        services.AddSingleton<IStarService, StarService>();
        return services;
    }
}
