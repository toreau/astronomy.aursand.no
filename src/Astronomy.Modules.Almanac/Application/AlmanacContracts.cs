using Astronomy.Modules.Calendars.Application;
using Astronomy.Modules.Ephemeris.Application;
using Astronomy.SharedKernel;
using Astronomy.SharedKernel.Coordinates;
using Astronomy.SharedKernel.Datasets;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.Modules.Almanac.Application;

public sealed record SunSection(
    DateTimeOffset? SunriseUtc,
    DateTimeOffset? SunsetUtc,
    DateTimeOffset? SolarNoonUtc,
    DateTimeOffset? CivilTwilightBeginUtc,
    DateTimeOffset? CivilTwilightEndUtc,
    DateTimeOffset? NauticalTwilightBeginUtc,
    DateTimeOffset? NauticalTwilightEndUtc,
    DateTimeOffset? AstronomicalTwilightBeginUtc,
    DateTimeOffset? AstronomicalTwilightEndUtc,
    CalculationMetadata Metadata);

public sealed record MoonSection(
    DateTimeOffset? MoonriseUtc,
    DateTimeOffset? MoonsetUtc,
    DateTimeOffset? MoonTransitUtc,
    string PhaseName,
    double IlluminationFraction,
    CalculationMetadata Metadata);

public sealed record DailyAlmanacRequest(
    DateOnly Date,
    double LatitudeDeg,
    double LongitudeDeg,
    double ElevationMeters,
    string Precision);

public sealed record DailyAlmanacResult(
    string Date,
    SunSection Sun,
    MoonSection Moon,
    CalculationMetadata Metadata);

public interface IAlmanacService
{
    Task<DailyAlmanacResult> GetDailyAsync(DailyAlmanacRequest request, CancellationToken ct);
}

internal sealed class AlmanacService : IAlmanacService
{
    private readonly IEphemerisService _ephemeris;
    private readonly ICalendarService _calendar;

    public AlmanacService(IEphemerisService ephemeris, ICalendarService calendar)
    {
        _ephemeris = ephemeris;
        _calendar = calendar;
    }

    public async Task<DailyAlmanacResult> GetDailyAsync(DailyAlmanacRequest request, CancellationToken ct)
    {
        var observer = ObserverLocation.FromDegrees(request.LatitudeDeg, request.LongitudeDeg, request.ElevationMeters);
        var precision = ParsePrecision(request.Precision);

        var sun = await _ephemeris.GetRiseSetAsync(BodyId.Sun, request.Date, observer, precision, ct);
        var moon = await _ephemeris.GetRiseSetAsync(BodyId.Moon, request.Date, observer, precision, ct);
        var civil = await _ephemeris.GetTwilightAsync(request.Date, observer, TwilightType.Civil, precision, ct);
        var nautical = await _ephemeris.GetTwilightAsync(request.Date, observer, TwilightType.Nautical, precision, ct);
        var astronomical = await _ephemeris.GetTwilightAsync(request.Date, observer, TwilightType.Astronomical, precision, ct);
        var noon = await _ephemeris.GetMoonIlluminationAsync(
            request.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(12), ct);

        var sunSection = new SunSection(
            sun.RiseUtc, sun.SetUtc, sun.TransitUtc,
            civil.BeginUtc, civil.EndUtc,
            nautical.BeginUtc, nautical.EndUtc,
            astronomical.BeginUtc, astronomical.EndUtc,
            sun.Metadata);
        var moonSection = new MoonSection(
            moon.RiseUtc, moon.SetUtc, moon.TransitUtc,
            noon.PhaseName, noon.IlluminationFraction, moon.Metadata);

        var metadata = new CalculationMetadata(
            [new DatasetRef("tzdb", "2026c")],
            [new AlgorithmRef("almanac-composer", "1.0")], []);
        return new DailyAlmanacResult(request.Date.ToString("yyyy-MM-dd"), sunSection, moonSection, metadata);
    }

    private static PrecisionMode ParsePrecision(string precision) => precision.ToLowerInvariant() switch
    {
        "consumer" => PrecisionMode.Consumer,
        "advanced" => PrecisionMode.Advanced,
        "reference" => PrecisionMode.Reference,
        _ => throw new ArgumentException($"unknown precision '{precision}'"),
    };
}

public static class AlmanacModuleRegistrar
{
    public static IServiceCollection AddAlmanacModule(this IServiceCollection services)
    {
        services.AddSingleton<IAlmanacService, AlmanacService>();
        return services;
    }
}
