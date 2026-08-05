using Astronomy.SharedKernel.Coordinates;
using Astronomy.SharedKernel.Datasets;
using Astronomy.SharedKernel.Stars;
using Astronomy.SharedKernel.Units;
using CosineKitty;
using Astr = CosineKitty.Astronomy;

namespace Astronomy.Modules.Stars.Application;

internal sealed class StarService : IStarService
{
    private static readonly DateTime J2000 = new(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Standard star rise/set altitude: geometric horizon minus 34' refraction.</summary>
    private const double RiseSetAltitudeDeg = -0.5667;

    private const string AlgorithmName = "hyg-star-catalog";

    private readonly StarCatalog _catalog;
    private readonly IDatasetCatalog _datasetCatalog;

    public StarService(StarCatalog catalog, IDatasetCatalog datasetCatalog)
    {
        _catalog = catalog;
        _datasetCatalog = datasetCatalog;
    }

    public Task<IReadOnlyList<StarSearchResult>> ConeSearchAsync(ConeSearchRequest request, CancellationToken ct)
    {
        EnsureAvailable();
        if (request.Radius.Degrees <= 0 || request.Radius.Degrees > 180)
            throw new ArgumentException("radius must be in (0, 180] degrees");
        if (request.Limit is < 1 or > 100)
            throw new ArgumentException("limit must be in [1, 100]");
        var centerRa = request.CenterRa.NormalizedTo360().Radians;
        var centerDec = request.CenterDec.Degrees;
        if (centerDec is < -90 or > 90)
            throw new ArgumentException("dec must be in [-90, 90]");
        var centerDecRad = request.CenterDec.Radians;
        var radiusRad = request.Radius.Radians;
        var results = new List<StarSearchResult>();
        foreach (var star in _catalog.Stars)
        {
            var (ra, dec) = PositionAt(star, request.Time ?? DateTimeOffset.UtcNow, precessToOfDate: false);
            var sep = SeparationRad(centerRa, centerDecRad, ra * Math.PI / 180, dec * Math.PI / 180);
            if (sep > radiusRad) continue;
            if (star.Vmag > request.MaxMagnitude) continue;
            results.Add(new StarSearchResult("hyg", star.Hip, Name(star), ra, dec, star.Vmag,
                Metadata(star, "cone-search")));
            if (results.Count >= request.Limit) break;
        }
        results.Sort((a, b) => a.Vmag.CompareTo(b.Vmag));
        return Task.FromResult<IReadOnlyList<StarSearchResult>>(results);
    }

    public Task<StarDetailResult> GetStarAsync(
        string hip, DateTimeOffset time, CoordinateFrame frame, PositionType positionType,
        ObserverLocation observer, bool refraction, CancellationToken ct)
    {
        EnsureAvailable();
        var star = Find(hip);
        if (frame == CoordinateFrame.IcrJ2000 && positionType != PositionType.Astrometric ||
            frame == CoordinateFrame.EquatorialOfDate && positionType != PositionType.Apparent)
            throw new ArgumentException($"unsupported frame/positionType combination {frame}/{positionType} (supported: ICRS-J2000+astrometric, of-date+apparent, horizontal)");

        var (ra, dec) = PositionAt(star, time, precessToOfDate: frame != CoordinateFrame.IcrJ2000);
        double? alt = null, az = null;
        if (frame == CoordinateFrame.Horizontal)
        {
            var t = new AstroTime(time.UtcDateTime);
            var engineObserver = new Observer(observer.Latitude.Degrees, observer.Longitude.Degrees, observer.ElevationMeters);
            // The engine's Horizon expects RA in HOURS (its Equatorial convention).
            var hor = Astr.Horizon(t, engineObserver, ra / 15.0, dec, refraction ? Refraction.Normal : Refraction.None);
            alt = hor.altitude;
            az = hor.azimuth;
        }
        var variant = frame == CoordinateFrame.EquatorialOfDate ? "of-date-apparent"
            : frame == CoordinateFrame.Horizontal ? "horizontal" : "j2000-astrometric";
        return Task.FromResult(new StarDetailResult(
            star.Hip, Name(star), star.BayerFlamsteed, ConstellationName(star.Constellation),
            star.Vmag, star.SpectralType, star.DistLightYears,
            new StarPosition(ra, dec, alt, az), Metadata(star, variant)));
    }

    public Task<IReadOnlyList<StarSearchResult>> SearchByNameAsync(string query, CancellationToken ct)
    {
        EnsureAvailable();
        var results = _catalog.SearchByName(query)
            .Select(star =>
            {
                var (ra, dec) = PositionAt(star, DateTimeOffset.UtcNow, precessToOfDate: false);
                return new StarSearchResult("hyg", star.Hip, Name(star), ra, dec, star.Vmag,
                    Metadata(star, "name-search"));
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<StarSearchResult>>(results);
    }

    public Task<StarEventsResult> GetRiseSetAsync(string hip, DateOnly date, ObserverLocation observer, CancellationToken ct)
    {
        EnsureAvailable();
        var star = Find(hip);
        var (rise, set, transit, circumpolar) = RiseSetTransit(star, date, observer);
        return Task.FromResult(new StarEventsResult(
            star.Hip, rise, set, transit, circumpolar, Metadata(star, "rise-set")));
    }

    public Task<StarListResult> GetBrightestAsync(int limit, double maxMagnitude, string? constellation, CancellationToken ct)
    {
        EnsureAvailable();
        if (limit is < 1 or > 50)
            throw new ArgumentException("limit must be in [1, 50]");
        var list = _catalog.Stars
            .Where(s => s.Vmag <= maxMagnitude)
            .Where(s => constellation is null || s.Constellation.Equals(constellation, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Vmag)
            .Take(limit)
            .Select(s =>
            {
                var (ra, dec) = PositionAt(s, DateTimeOffset.UtcNow, precessToOfDate: false);
                return new StarListItem(s.Hip, Name(s), ConstellationName(s.Constellation), ra, dec, s.Vmag);
            })
            .ToList();
        return Task.FromResult(new StarListResult(list, Metadata(null, "brightest")));
    }

    private void EnsureAvailable()
    {
        if (!_catalog.IsAvailable)
            throw new StarCatalogUnavailableException(_catalog.Reason);
    }

    private StarRecord Find(string hip)
    {
        if (!_catalog.TryGetByHip(hip, out var star))
            throw new ArgumentException($"unknown star '{hip}' (Hipparcos catalogue id)");
        return star;
    }

    private static string Name(StarRecord star) =>
        star.ProperName.Length > 0 ? star.ProperName
        : star.BayerFlamsteed.Length > 0 ? star.BayerFlamsteed
        : star.Hip;

    private CalculationMetadata Metadata(StarRecord? star, string variant)
    {
        var datasets = new List<DatasetRef> { new("star-catalog-hyg", _catalog.Version) };
        var algorithms = new List<AlgorithmRef>
        {
            new(AlgorithmName, _catalog.Version + ":proper-motion:" + variant),
        };
        return new CalculationMetadata(datasets, algorithms, []);
    }

    /// <summary>
    /// Position of the star at the given UTC instant: J2000 catalog coordinates
    /// propagated by proper motion (PmRaMasYr is cos(dec)-scaled per Hipparcos
    /// convention), optionally precessed to the equinox of date via the engine's
    /// EQJ->EQD rotation.
    /// </summary>
    private static (double RaDeg, double DecDeg) PositionAt(StarRecord star, DateTimeOffset utc, bool precessToOfDate)
    {
        var years = (utc.UtcDateTime - J2000).TotalDays / 365.25;
        var dec = star.DecDeg + star.PmDecMasYr * years / 3_600_000.0;
        var ra = star.RaDeg + star.PmRaMasYr * years / 3_600_000.0 / Math.Cos(dec * Math.PI / 180);
        if (!precessToOfDate) return (ra, dec);

        var raRad = ra * Math.PI / 180;
        var decRad = dec * Math.PI / 180;
        var v = new double[]
        {
            Math.Cos(decRad) * Math.Cos(raRad),
            Math.Cos(decRad) * Math.Sin(raRad),
            Math.Sin(decRad),
        };
        var t = new AstroTime(utc.UtcDateTime);
        var rot = Astr.Rotation_EQJ_EQD(t);
        var precessed = Astr.RotateVector(rot, new AstroVector(v[0], v[1], v[2], t));
        var eq = Astr.EquatorFromVector(precessed);
        return (eq.ra * 15.0, eq.dec);
    }

    /// <summary>
    /// Analytic rise/set/transit from the hour-angle equation
    /// cos(H) = (sin(alt) - sin(lat)sin(dec)) / (cos(lat)cos(dec)) with the
    /// standard -0.5667 deg refraction threshold. Circumpolar stars return null
    /// events with Circumpolar=true.
    /// </summary>
    private static (DateTimeOffset? Rise, DateTimeOffset? Set, DateTimeOffset? Transit, bool Circumpolar) RiseSetTransit(
        StarRecord star, DateOnly date, ObserverLocation observer)
    {
        var t0 = new AstroTime(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var lat = observer.Latitude.Degrees;
        var lon = observer.Longitude.Degrees;

        var (raOfDate, decOfDate) = PositionAt(star, t0.ToUtcDateTime(), precessToOfDate: true);
        var raRad = raOfDate * Math.PI / 180;
        var decRad = decOfDate * Math.PI / 180;
        var latRad = lat * Math.PI / 180;

        var cosH = (Math.Sin(RiseSetAltitudeDeg * Math.PI / 180) - Math.Sin(latRad) * Math.Sin(decRad))
                   / (Math.Cos(latRad) * Math.Cos(decRad));
        if (cosH > 1 || cosH < -1)
            return (null, null, null, true);

        var haRad = Math.Acos(Math.Clamp(cosH, -1, 1));

        // The engine's SiderealTime returns GMST in HOURS (0-24) - convert to degrees.
        var gmst0 = Astr.SiderealTime(t0) * 15.0;
        var lst0 = (gmst0 + lon + 360) % 360;
        var haNow = (lst0 - raOfDate + 360) % 360; // degrees, 0..360

        var degPerDay = 360.985647366;
        var transitOffsetDays = ((360.0 - haNow) % 360.0) / degPerDay; // time until HA wraps to 0
        var transit = t0.ToUtcDateTime().AddDays(transitOffsetDays);
        var rise = transit.AddDays(-haRad * 180 / Math.PI / degPerDay);
        var set = transit.AddDays(haRad * 180 / Math.PI / degPerDay);

        if (DateOnly.FromDateTime(rise.Date) != date || DateOnly.FromDateTime(set.Date) != date || DateOnly.FromDateTime(transit.Date) != date)
        {
            // Events landed on a neighboring date; shift by a sidereal day so the
            // transit falls on the requested calendar date.
            var target = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            while (transit < target) transit = transit.AddDays(1.0);
            while (transit >= target.AddDays(1.0)) transit = transit.AddDays(-1.0);
            rise = transit.AddDays(-haRad * 180 / Math.PI / degPerDay);
            set = transit.AddDays(haRad * 180 / Math.PI / degPerDay);
        }

        return (rise, set, transit, false);
    }

    private static double SeparationRad(double ra1, double dec1, double ra2, double dec2)
    {
        var cosSep = Math.Sin(dec1) * Math.Sin(dec2) + Math.Cos(dec1) * Math.Cos(dec2) * Math.Cos(ra1 - ra2);
        return Math.Acos(Math.Clamp(cosSep, -1, 1));
    }

    internal static string ConstellationName(string abbreviation) =>
        abbreviation.Length > 0 && ConstellationNames.TryGetValue(abbreviation, out var name)
            ? name
            : abbreviation;

    private static readonly IReadOnlyDictionary<string, string> ConstellationNames = new Dictionary<string, string>
    {
        ["And"] = "Andromeda", ["Ant"] = "Antlia", ["Aps"] = "Apus", ["Aql"] = "Aquila",
        ["Aqr"] = "Aquarius", ["Ara"] = "Ara", ["Ari"] = "Aries", ["Aur"] = "Auriga",
        ["Boo"] = "Bootes", ["Cae"] = "Caelum", ["Cam"] = "Camelopardalis", ["Cap"] = "Capricornus",
        ["Car"] = "Carina", ["Cas"] = "Cassiopeia", ["Cen"] = "Centaurus", ["Cep"] = "Cepheus",
        ["Cet"] = "Cetus", ["Cha"] = "Chamaeleon", ["Cir"] = "Circinus", ["CMa"] = "Canis Major",
        ["CMi"] = "Canis Minor", ["Cnc"] = "Cancer", ["Col"] = "Columba", ["Com"] = "Coma Berenices",
        ["CrA"] = "Corona Australis", ["CrB"] = "Corona Borealis", ["Crt"] = "Crater", ["Cru"] = "Crux",
        ["Crv"] = "Corvus", ["CVn"] = "Canes Venatici", ["Cyg"] = "Cygnus", ["Del"] = "Delphinus",
        ["Dor"] = "Dorado", ["Dra"] = "Draco", ["Equ"] = "Equuleus", ["Eri"] = "Eridanus",
        ["For"] = "Fornax", ["Gem"] = "Gemini", ["Gru"] = "Grus", ["Her"] = "Hercules",
        ["Hor"] = "Horologium", ["Hya"] = "Hydra", ["Hyi"] = "Hydrus", ["Ind"] = "Indus",
        ["Lac"] = "Lacerta", ["Leo"] = "Leo", ["Lep"] = "Lepus", ["Lib"] = "Libra",
        ["LMi"] = "Leo Minor", ["Lup"] = "Lupus", ["Lyn"] = "Lynx", ["Lyr"] = "Lyra",
        ["Men"] = "Mensa", ["Mic"] = "Microscopium", ["Mon"] = "Monoceros", ["Mus"] = "Musca",
        ["Nor"] = "Norma", ["Oct"] = "Octans", ["Oph"] = "Ophiuchus", ["Ori"] = "Orion",
        ["Pav"] = "Pavo", ["Peg"] = "Pegasus", ["Per"] = "Perseus", ["Phe"] = "Phoenix",
        ["Pic"] = "Pictor", ["Psc"] = "Pisces", ["PsA"] = "Piscis Austrinus", ["Pup"] = "Puppis",
        ["Pyx"] = "Pyxis", ["Ret"] = "Reticulum", ["Scl"] = "Sculptor", ["Sco"] = "Scorpius",
        ["Sct"] = "Scutum", ["Ser"] = "Serpens", ["Sex"] = "Sextans", ["Sge"] = "Sagitta",
        ["Sgr"] = "Sagittarius", ["Tau"] = "Taurus", ["Tel"] = "Telescopium", ["Tri"] = "Triangulum",
        ["TrA"] = "Triangulum Australe", ["Tuc"] = "Tucana", ["UMa"] = "Ursa Major", ["UMi"] = "Ursa Minor",
        ["Vel"] = "Vela", ["Vir"] = "Virgo", ["Vol"] = "Volans", ["Vul"] = "Vulpecula",
    };
}
