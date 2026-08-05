using Astronomy.SharedKernel.Coordinates;
using Astronomy.SharedKernel.Datasets;
using Astronomy.SharedKernel.Time;
using Microsoft.Extensions.Caching.Memory;

namespace Astronomy.Modules.Satellites.Application;

internal sealed class SatelliteService : ISatelliteService
{
    public const string DatasetName = "satellite-elements";
    public const string AlgorithmName = "sgp4";
    public const string AlgorithmVersion = "onesgp4-1.1.0";
    public const double StaleTleHours = 72.0;
    public const double MaxPassWindowDays = 7.0;

    private static readonly TimeSpan ElementsCacheTtl = TimeSpan.FromSeconds(60);

    private readonly string _dbPath;
    private readonly IDatasetRegistry _registry;
    private readonly TimeScaleConverter _timeScale;
    private readonly IOrbitalPropagator _propagator;
    private readonly IMemoryCache? _cache;

    public SatelliteService(string dbPath, IDatasetRegistry registry, TimeScaleConverter timeScale, IOrbitalPropagator? propagator = null, IMemoryCache? cache = null)
    {
        _dbPath = dbPath;
        _registry = registry;
        _timeScale = timeScale;
        _propagator = propagator ?? new OneSgp4Propagator();
        _cache = cache;
    }

    public Task<SatellitePositionResult> GetPositionAsync(string noradId, DateTimeOffset time, ObserverLocation observer, bool refraction, CancellationToken ct)
    {
        var (elements, activeVersion) = Load(noradId, out _);
        var ut1 = Ut1MinusUtc(time);
        var teme = _propagator.Propagate(elements, time);
        var pef = SatelliteFrames.TemeToPef(teme, SatelliteFrames.GmstDegrees(Jd(time)) + ut1 * 360.0 / 86164.0905);
        var (lat, lon, altKm) = SatelliteFrames.GeodeticFromEcef(pef.X, pef.Y, pef.Z);
        var obsEcef = SatelliteFrames.GeodeticToEcef(observer.Latitude.Degrees, observer.Longitude.Degrees,
            observer.ElevationMeters / 1000.0);
        var (alt, az, range) = SatelliteFrames.Topocentric(pef.X, pef.Y, pef.Z, obsEcef.X, obsEcef.Y, obsEcef.Z,
            observer.Latitude.Degrees, observer.Longitude.Degrees, refraction);
        return Task.FromResult(new SatellitePositionResult(
            elements.NoradId, elements.Name, alt, az, range, lat, lon, altKm,
            (time - elements.EpochUtc).TotalHours, Metadata(elements, activeVersion, "position")));
    }

    public Task<SatellitePassesResult> GetPassesAsync(string noradId, DateTimeOffset from, DateTimeOffset to, ObserverLocation observer, double minElevationDeg, double stepSeconds, CancellationToken ct)
    {
        if (to - from > TimeSpan.FromDays(MaxPassWindowDays))
            throw new ArgumentException($"pass window exceeds {MaxPassWindowDays} days (SGP4 validity)");
        if (minElevationDeg is < 0 or > 90)
            throw new ArgumentException("minElevation must be in [0, 90]");
        if (stepSeconds is < 10 or > 300)
            throw new ArgumentException("stepSeconds must be in [10, 300]");
        var (elements, activeVersion) = Load(noradId, out _);
        var ut1 = Ut1MinusUtc(from);
        var passes = _propagator is OneSgp4Propagator sgp4
            ? SatellitePassPredictor.Predict(sgp4.PreparedPropagator(elements), from, to, observer, ut1, minElevationDeg, stepSeconds)
            : SatellitePassPredictor.Predict(_propagator, elements, from, to, observer, ut1, minElevationDeg, stepSeconds);
        return Task.FromResult(new SatellitePassesResult(
            elements.NoradId, elements.Name, from, to, passes, Metadata(elements, activeVersion, "passes")));
    }

    public Task<IReadOnlyList<SatelliteSearchResult>> SearchAsync(string name, CancellationToken ct)
    {
        var (activeVersion, rows) = LoadAll();
        var q = name.Trim().ToLowerInvariant();
        var results = rows
            .Where(r => q.Length == 0 || r.Name.ToLowerInvariant().Contains(q) || r.NoradId.Contains(q))
            .OrderBy(r => r.Name)
            .Take(20)
            .Select(r => new SatelliteSearchResult(r.NoradId, r.Name, r.EpochUtc, (DateTimeOffset.UtcNow - r.EpochUtc).TotalHours))
            .ToList();
        return Task.FromResult<IReadOnlyList<SatelliteSearchResult>>(results);
    }

    public Task<IngestionStatus> GetStatusAsync(CancellationToken ct)
    {
        var active = _registry.ActiveVersion(DatasetName);
        var rows = active is null ? [] : SatelliteStore.ReadElements(_dbPath, active.Version);
        var (fresh, warn, degraded, refuse) = SatelliteStore.Freshness(rows, DateTimeOffset.UtcNow);
        return Task.FromResult(new IngestionStatus(active?.Version, rows.Count, fresh, warn, degraded, refuse));
    }

    private (OrbitalElementRow Elements, string ActiveVersion) Load(string noradId, out IReadOnlyList<OrbitalElementRow> rows)
    {
        var (activeVersion, all) = LoadAll();
        var match = all.FirstOrDefault(r => r.NoradId == noradId);
        if (match is null)
            throw new ArgumentException($"unknown satellite '{noradId}'");
        rows = all;
        return (match, activeVersion);
    }

    private (string ActiveVersion, IReadOnlyList<OrbitalElementRow> Rows) LoadAll()
    {
        var active = _registry.ActiveVersion(DatasetName);
        if (active is null)
            throw new SatelliteElementsUnavailableException("satellite-elements dataset not ingested");
        var cacheKey = $"{DatasetName}:{_dbPath}:{active.Version}";
        if (_cache is not null && _cache.TryGetValue(cacheKey, out IReadOnlyList<OrbitalElementRow>? cached) && cached is not null)
            return (active.Version, cached);
        var rows = SatelliteStore.ReadElements(_dbPath, active.Version);
        if (rows.Count == 0)
            throw new SatelliteElementsUnavailableException($"satellite-elements dataset {active.Version} has no elements");
        _cache?.Set(cacheKey, rows, ElementsCacheTtl);
        return (active.Version, rows);
    }

    private double Ut1MinusUtc(DateTimeOffset utc)
    {
        try
        {
            return _timeScale.Convert(utc).Ut1MinusUtcSeconds;
        }
        catch
        {
            return 0.0; // eop-ut1 absent: UTC ~ UT1 (sub-arcsecond effect for alt/az)
        }
    }

    private CalculationMetadata Metadata(OrbitalElementRow elements, string activeVersion, string variant)
    {
        var warnings = new List<CalculationWarning>();
        var ageHours = (DateTimeOffset.UtcNow - elements.EpochUtc).TotalHours;
        if (ageHours > StaleTleHours)
            warnings.Add(new CalculationWarning("AST-7004",
                $"TLE age {ageHours:F0}h exceeds {StaleTleHours:F0}h; SGP4 accuracy degrades with element age"));
        return new CalculationMetadata(
            [new DatasetRef(DatasetName, activeVersion)],
            [new AlgorithmRef(AlgorithmName, AlgorithmVersion + ":" + variant)],
            warnings);
    }

    private static double Jd(DateTimeOffset utc) =>
        2451545.0 + (utc - new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero)).TotalDays;
}
