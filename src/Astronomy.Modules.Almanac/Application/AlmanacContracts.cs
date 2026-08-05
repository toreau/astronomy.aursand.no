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

public sealed record PlanetSectionEntry(
    string Body,
    DateTimeOffset? RiseUtc,
    DateTimeOffset? TransitUtc,
    DateTimeOffset? SetUtc,
    double Magnitude,
    double ElongationDeg,
    string Constellation,
    bool VisibleTonight,
    bool NakedEyeVisible,
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
    IReadOnlyList<PlanetSectionEntry> Planets,
    CalculationMetadata Metadata);

public sealed record MonthlyDayPlanetEntry(
    string Body,
    DateTimeOffset? RiseUtc,
    DateTimeOffset? TransitUtc,
    DateTimeOffset? SetUtc,
    double Magnitude,
    double ElongationDeg,
    string Constellation);

public sealed record MonthlyDayEntry(
    string Date,
    DateTimeOffset? SunRiseUtc,
    DateTimeOffset? SunSetUtc,
    DateTimeOffset? SolarNoonUtc,
    DateTimeOffset? MoonRiseUtc,
    DateTimeOffset? MoonSetUtc,
    string MoonPhaseName,
    IReadOnlyList<MonthlyDayPlanetEntry> Planets);

public sealed record MonthlyAlmanacResult(
    string Month,
    string MagnitudeReferenceUtc,
    IReadOnlyList<MonthlyDayEntry> Days,
    IReadOnlyList<PlanetEvent> Events,
    CalculationMetadata Metadata);

public interface IAlmanacService
{
    Task<DailyAlmanacResult> GetDailyAsync(DailyAlmanacRequest request, CancellationToken ct);
    Task<MonthlyAlmanacResult> GetMonthlyAsync(int year, int month, ObserverLocation observer, CancellationToken ct);
}

internal sealed class AlmanacService : IAlmanacService
{
    private readonly IEphemerisService _ephemeris;

    public AlmanacService(IEphemerisService ephemeris)
    {
        _ephemeris = ephemeris;
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
        var illumination = await _ephemeris.GetMoonIlluminationAsync(
            request.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(12), ct);

        var sunSection = new SunSection(
            sun.RiseUtc, sun.SetUtc, sun.TransitUtc,
            civil.BeginUtc, civil.EndUtc,
            nautical.BeginUtc, nautical.EndUtc,
            astronomical.BeginUtc, astronomical.EndUtc,
            sun.Metadata);
        var moonSection = new MoonSection(
            moon.RiseUtc, moon.SetUtc, moon.TransitUtc,
            illumination.PhaseName, illumination.IlluminationFraction, moon.Metadata);

        var planets = new List<PlanetSectionEntry>();
        var noon = request.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(12);
        foreach (var body in BodyId.Planets)
        {
            var riseSet = await _ephemeris.GetRiseSetAsync(body, request.Date, observer, precision, ct);
            var visibility = await _ephemeris.GetVisibilityAsync(body, noon, observer, precision, ct);
            planets.Add(new PlanetSectionEntry(
                body.Name, riseSet.RiseUtc, riseSet.TransitUtc, riseSet.SetUtc,
                visibility.Magnitude, visibility.ElongationDeg, visibility.Constellation ?? "",
                visibility.VisibleTonight, visibility.NakedEyeVisible, riseSet.Metadata));
        }

        var metadata = new CalculationMetadata(
            [new DatasetRef("tzdb", "2026c")],
            [new AlgorithmRef("almanac-composer", "1.0")], []);
        return new DailyAlmanacResult(request.Date.ToString("yyyy-MM-dd"), sunSection, moonSection, planets, metadata);
    }

    public async Task<MonthlyAlmanacResult> GetMonthlyAsync(int year, int month, ObserverLocation observer, CancellationToken ct)
    {
        var precision = PrecisionMode.Consumer;
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var days = new MonthlyDayEntry[daysInMonth];
        var referenceUtc = "12:00:00Z";
        var events = new List<PlanetEvent>();

        // Each day is ~10 engine searches (sun/moon/7 planets rise-set + visibility);
        // the consumer chain is thread-safe, so compute days in parallel.
        await Task.WhenAll(Enumerable.Range(0, daysInMonth).Select(day => Task.Run(async () =>
        {
            var date = new DateOnly(year, month, day + 1);
            var noon = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(12);

            var sun = await _ephemeris.GetRiseSetAsync(BodyId.Sun, date, observer, precision, ct);
            var moon = await _ephemeris.GetRiseSetAsync(BodyId.Moon, date, observer, precision, ct);
            var illumination = await _ephemeris.GetMoonIlluminationAsync(noon, ct);

            var planets = new List<MonthlyDayPlanetEntry>();
            foreach (var body in BodyId.Planets)
            {
                var riseSet = await _ephemeris.GetRiseSetAsync(body, date, observer, precision, ct);
                var visibility = await _ephemeris.GetVisibilityAsync(body, noon, observer, precision, ct);
                planets.Add(new MonthlyDayPlanetEntry(
                    body.Name, riseSet.RiseUtc, riseSet.TransitUtc, riseSet.SetUtc,
                    visibility.Magnitude, visibility.ElongationDeg, visibility.Constellation ?? ""));
            }

            days[day] = new MonthlyDayEntry(
                date.ToString("yyyy-MM-dd"),
                sun.RiseUtc, sun.SetUtc, sun.TransitUtc,
                moon.RiseUtc, moon.SetUtc, illumination.PhaseName,
                planets);
        })));

        var from = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
        var to = from.AddMonths(1);
        var monthEvents = await _ephemeris.GetEventsAsync(from, to,
            BodyId.Planets.Where(p => p != BodyId.Mercury && p != BodyId.Venus).ToList(),
            [EventType.Opposition, EventType.Conjunction], ct);
        var innerEvents = await _ephemeris.GetEventsAsync(from, to,
            [BodyId.Mercury, BodyId.Venus], [EventType.MaxElongation], ct);
        events.AddRange(monthEvents.Events);
        events.AddRange(innerEvents.Events);

        var metadata = new CalculationMetadata(
            [new DatasetRef("tzdb", "2026c")],
            [new AlgorithmRef("almanac-composer", "1.0")], []);
        return new MonthlyAlmanacResult($"{year:D4}-{month:D2}", referenceUtc, days,
            events.OrderBy(e => e.Utc).ToList(), metadata);
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
