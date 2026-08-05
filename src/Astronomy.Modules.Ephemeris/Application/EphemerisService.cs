using Astronomy.SharedKernel;
using Astronomy.SharedKernel.Coordinates;
using Astronomy.SharedKernel.Datasets;
using Astronomy.SharedKernel.Time;

namespace Astronomy.Modules.Ephemeris.Application;

internal sealed class EphemerisService : IEphemerisService
{
    private const string EngineRef = "astronomy-engine";

    private readonly EphemerisCalculator _calculator = new();
    private readonly IDatasetCatalog _catalog;

    public EphemerisService(IDatasetCatalog catalog)
    {
        _catalog = catalog;
    }

    public Task<EphemerisPositionResult> GetPositionAsync(PositionRequest request, CancellationToken ct)
    {
        if (!BodyId.TryParse(request.Body, out var body))
            throw new ArgumentException($"unknown body '{request.Body}'");
        if (request.PositionType == PositionType.Geometric)
            throw new ArgumentException("positionType 'geometric' is not supported: the engine exposes no pure-geometric path (its no-aberration path equals astrometric within arcseconds); use 'astrometric' or 'apparent'");
        if (request.Frame == CoordinateFrame.Horizontal)
        {
            var (alt, az) = _calculator.Horizontal(body, request.Time, request.Observer,
                request.Refraction == RefractionModel.Simple);
            return Task.FromResult(new EphemerisPositionResult(
                body.Name, 0, 0, alt, az, 0,
                Metadata(request.Precision, "horizontal")));
        }
        if (request.Frame == CoordinateFrame.IcrJ2000 && request.PositionType != PositionType.Astrometric ||
            request.Frame == CoordinateFrame.EquatorialOfDate && request.PositionType != PositionType.Apparent)
            throw new ArgumentException($"unsupported frame/positionType combination {request.Frame}/{request.PositionType} (supported: ICRS-J2000+astrometric, of-date+apparent, horizontal)");

        var eq = _calculator.GeocentricEquatorial(body, request.Time, apparent: request.PositionType == PositionType.Apparent);
        return Task.FromResult(new EphemerisPositionResult(
            body.Name, eq.RaDeg, eq.DecDeg, null, null, eq.DistanceKm,
            Metadata(request.Precision, request.Frame == CoordinateFrame.IcrJ2000 ? "j2000-astrometric" : "of-date-apparent")));
    }

    public Task<RiseSetTransitResult> GetRiseSetAsync(BodyId body, DateOnly date, ObserverLocation observer, PrecisionMode precision, CancellationToken ct) =>
        Task.FromResult(new RiseSetTransitResult(
            body.Name,
            _calculator.SearchRiseSet(body, date, observer, rise: true),
            _calculator.SearchRiseSet(body, date, observer, rise: false),
            _calculator.SearchTransit(body, date, observer),
            Metadata(precision, "rise-set")));

    public Task<TwilightResult> GetTwilightAsync(DateOnly date, ObserverLocation observer, TwilightType type, PrecisionMode precision, CancellationToken ct)
    {
        var altitude = type switch
        {
            TwilightType.Civil => -6.0,
            TwilightType.Nautical => -12.0,
            _ => -18.0,
        };
        return Task.FromResult(new TwilightResult(
            type,
            _calculator.SearchAltitude(BodyId.Sun, date, observer, altitude, rising: true),
            _calculator.SearchAltitude(BodyId.Sun, date, observer, altitude, rising: false),
            Metadata(precision, "twilight")));
    }

    public Task<MoonPhasesResult> GetMoonPhasesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to - from > TimeSpan.FromDays(366))
            throw new ArgumentException("moon phase range exceeds 366 days");
        var events = _calculator.MoonQuarters(from, to)
            .Select(q => new MoonPhaseEvent(q.Utc, EphemerisCalculator.MoonPhaseName(q.Quarter), QuarterFraction(q.Quarter)))
            .ToList();
        return Task.FromResult(new MoonPhasesResult(from, to, events, Metadata(PrecisionMode.Consumer, "moon-phases")));
    }

    public Task<MoonIlluminationResult> GetMoonIlluminationAsync(DateTimeOffset time, CancellationToken ct)
    {
        var (fraction, phaseAngle) = _calculator.MoonIllumination(time);
        return Task.FromResult(new MoonIlluminationResult(
            time, fraction, EphemerisCalculator.MoonPhaseNameFromIllumination(fraction, phaseAngle),
            Metadata(PrecisionMode.Consumer, "moon-illumination")));
    }

    private CalculationMetadata Metadata(PrecisionMode precision, string variant)
    {
        var warnings = new List<CalculationWarning>();
        if (precision != PrecisionMode.Consumer)
            warnings.Add(new CalculationWarning("AST-7002",
                "advanced/reference tier not implemented until Phase 4; consumer-tier accuracy applies"));
        return new CalculationMetadata(
            [new DatasetRef("leap-seconds", _catalog.ActiveVersion("leap-seconds")?.Version ?? "none"),
             new DatasetRef("eop-ut1", _catalog.ActiveVersion("eop-ut1")?.Version ?? "none")],
            [new AlgorithmRef(EngineRef, EphemerisCalculator.EngineVersion + ":" + variant)],
            warnings);
    }

    private static double QuarterFraction(int quarter) => quarter switch
    {
        0 => 0.0,
        1 => 0.5,
        2 => 1.0,
        _ => 0.5,
    };
}
