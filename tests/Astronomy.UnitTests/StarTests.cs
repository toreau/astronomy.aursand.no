using Astronomy.Modules.Stars.Application;
using Astronomy.SharedKernel.Coordinates;
using Astronomy.SharedKernel.Stars;
using Astronomy.SharedKernel.Units;

namespace Astronomy.UnitTests;

public class StarTests
{
    private static readonly StarRecord Sirius = new(
        "32349", "Sirius", "9Alp CMa", "Alp", "9", "CMa",
        101.287155, -16.716117, -546.01, -1223.08, 8.6, -1.44, "A0m...");

    private static readonly StarRecord Vega = new(
        "91262", "Vega", "3Alp Lyr", "Alp", "3", "Lyr",
        279.234735, 38.783689, 200.94, 286.23, 25.0, 0.03, "A0Va");

    private static readonly StarRecord Arcturus = new(
        "69673", "Arcturus", "16Alp Boo", "Alp", "16", "Boo",
        213.915300, 19.182409, -1093.45, -1999.40, 36.7, -0.05, "K1.5III");

    private static readonly StarRecord[] Sample = [Sirius, Vega, Arcturus];

    private static StarCatalog Catalog(params StarRecord[] stars) => new(stars, "v38", "ok");

    private static StarService Service(StarCatalog catalog) =>
        new(catalog, new StubCatalog());

    [Fact]
    public void Catalog_LoadsAndIndexesByHip()
    {
        var catalog = Catalog(Sample);
        Assert.Equal(3, catalog.Stars.Count);
        Assert.True(catalog.IsAvailable);
        Assert.True(catalog.TryGetByHip("32349", out var star));
        Assert.Equal("Sirius", star.ProperName);
        Assert.False(catalog.TryGetByHip("99999", out _));
    }

    [Fact]
    public void Catalog_Unavailable_WhenEmpty()
    {
        var catalog = StarCatalog.Unavailable;
        Assert.False(catalog.IsAvailable);
        Assert.Contains("not ingested", catalog.Reason);
    }

    [Fact]
    public void StarRecord_RoundTripsThroughCsv()
    {
        var line = Sirius.ToCsvLine();
        var parsed = StarRecord.Parse(line);
        Assert.Equal(Sirius.Hip, parsed.Hip);
        Assert.Equal(Sirius.ProperName, parsed.ProperName);
        Assert.Equal(Sirius.RaDeg, parsed.RaDeg);
        Assert.Equal(Sirius.DecDeg, parsed.DecDeg);
        Assert.Equal(Sirius.PmRaMasYr, parsed.PmRaMasYr);
        Assert.Equal(Sirius.Vmag, parsed.Vmag);
    }

    [Fact]
    public async Task ConeSearch_FindsNearbyBrightStars()
    {
        var service = Service(Catalog(Sample));
        // 2 degrees around Sirius: only Sirius is within radius; Arcturus/Vega are far away
        var results = await service.ConeSearchAsync(new ConeSearchRequest(
            new Angle(Sirius.RaDeg), new Angle(Sirius.DecDeg), new Angle(2.0), 6.5, 50, null), CancellationToken.None);
        Assert.Single(results);
        Assert.Equal("32349", results[0].CatalogueId);
        Assert.Equal("hyg", results[0].Catalogue);
    }

    [Fact]
    public async Task ConeSearch_RespectsMagnitudeFilter()
    {
        var service = Service(Catalog(Sample));
        // Sirius is -1.44: brighter than -1.4 but not brighter than -2.0
        var results = await service.ConeSearchAsync(new ConeSearchRequest(
            new Angle(Sirius.RaDeg), new Angle(Sirius.DecDeg), new Angle(90.0), -1.4, 50, null), CancellationToken.None);
        Assert.Single(results);
        var results2 = await service.ConeSearchAsync(new ConeSearchRequest(
            new Angle(Sirius.RaDeg), new Angle(Sirius.DecDeg), new Angle(90.0), -2.0, 50, null), CancellationToken.None);
        Assert.Empty(results2);
    }

    [Fact]
    public async Task ConeSearch_InvalidRadius_Throws()
    {
        var service = Service(Catalog(Sample));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ConeSearchAsync(
            new ConeSearchRequest(new Angle(0), new Angle(0), new Angle(0), 6.5, 50, null), CancellationToken.None));
    }

    [Fact]
    public async Task SearchByName_MatchesProperNameCaseInsensitively()
    {
        var service = Service(Catalog(Sample));
        var results = await service.SearchByNameAsync("sirius", CancellationToken.None);
        Assert.Single(results);
        Assert.Equal("32349", results[0].CatalogueId);

        var byBayer = await service.SearchByNameAsync("alp ly", CancellationToken.None);
        Assert.Contains(byBayer, r => r.CatalogueId == "91262");
    }

    [Fact]
    public async Task GetStar_UnknownHip_Throws()
    {
        var service = Service(Catalog(Sample));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetStarAsync(
            "99999", DateTimeOffset.UtcNow, CoordinateFrame.IcrJ2000, PositionType.Astrometric,
            ObserverLocation.FromDegrees(0, 0, 0), false, CancellationToken.None));
    }

    [Fact]
    public async Task GetStar_UnavailableCatalog_Throws503()
    {
        var service = Service(Catalog());
        await Assert.ThrowsAsync<StarCatalogUnavailableException>(() => service.GetStarAsync(
            "32349", DateTimeOffset.UtcNow, CoordinateFrame.IcrJ2000, PositionType.Astrometric,
            ObserverLocation.FromDegrees(0, 0, 0), false, CancellationToken.None));
    }

    [Fact]
    public async Task GetStar_UnsupportedFramePositionType_Throws()
    {
        var service = Service(Catalog(Sample));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetStarAsync(
            "32349", DateTimeOffset.UtcNow, CoordinateFrame.IcrJ2000, PositionType.Apparent,
            ObserverLocation.FromDegrees(0, 0, 0), false, CancellationToken.None));
    }

    [Fact]
    public async Task GetStar_ProperMotionMovesPosition()
    {
        var service = Service(Catalog(Sample));
        // Sirius at J2000: pm -546 mas/yr RA, -1223 mas/yr dec
        var atJ2000 = await service.GetStarAsync("32349", new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero),
            CoordinateFrame.IcrJ2000, PositionType.Astrometric, ObserverLocation.FromDegrees(0, 0, 0), false, CancellationToken.None);
        Assert.Equal(Sirius.RaDeg, atJ2000.Position.RaDeg, 3);
        Assert.Equal(Sirius.DecDeg, atJ2000.Position.DecDeg, 3);

        var at2030 = await service.GetStarAsync("32349", new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero),
            CoordinateFrame.IcrJ2000, PositionType.Astrometric, ObserverLocation.FromDegrees(0, 0, 0), false, CancellationToken.None);
        // 30 years: RA shift ~ -546*30 mas / cos(dec) ~ -17.1 arcsec -> -0.00475 deg; dec shift -1223*30 mas = -36.7 arcsec = -0.0102 deg
        Assert.True(at2030.Position.RaDeg < atJ2000.Position.RaDeg - 0.004);
        Assert.True(at2030.Position.DecDeg < atJ2000.Position.DecDeg - 0.009);
        Assert.Equal("Sirius", at2030.Name);
        Assert.Equal("Canis Major", at2030.Constellation);
    }

    [Fact]
    public async Task GetStar_Horizontal_ReturnsAltAz()
    {
        var service = Service(Catalog(Sample));
        var result = await service.GetStarAsync("91262", new DateTimeOffset(2026, 8, 4, 22, 0, 0, TimeSpan.Zero),
            CoordinateFrame.Horizontal, PositionType.Astrometric,
            ObserverLocation.FromDegrees(59.9, 10.7, 0), false, CancellationToken.None);
        Assert.NotNull(result.Position.AltDeg);
        Assert.NotNull(result.Position.AzDeg);
        Assert.InRange(result.Position.AltDeg!.Value, -90, 90);
        Assert.InRange(result.Position.AzDeg!.Value, 0, 360);
    }

    [Fact]
    public async Task GetStar_Horizontal_StarAtZenithIsNear90()
    {
        // Pin the RA-in-hours convention: at the J2000 epoch (precession ~ 0), an
        // observer at the star's latitude with LST == RA sees the star at the zenith.
        var lat = 45.0;
        var lon = 10.0;
        var utc = new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var gmstHours = CosineKitty.Astronomy.SiderealTime(new CosineKitty.AstroTime(utc.UtcDateTime));
        var lstDeg = (gmstHours * 15.0 + lon + 360) % 360;
        var zenithStar = new StarRecord("999001", "ZenithStar", "", "", "", "And",
            lstDeg, lat, 0, 0, 100, 5.0, "G2V");
        var catalog = new StarCatalog([zenithStar], "v38", "ok");
        var service = new StarService(catalog, new StubCatalog());
        var result = await service.GetStarAsync("999001", utc, CoordinateFrame.Horizontal, PositionType.Astrometric,
            ObserverLocation.FromDegrees(lat, lon, 0), false, CancellationToken.None);
        Assert.InRange(result.Position.AltDeg!.Value, 89.9, 90.1);
    }

    [Fact]
    public async Task GetRiseSet_SiriusOslo_EventsMatchHorizonCrossing()
    {
        var service = Service(Catalog(Sample));
        var observer = ObserverLocation.FromDegrees(59.9, 10.7, 0);
        var result = await service.GetRiseSetAsync("32349", new DateOnly(2026, 8, 4), observer, CancellationToken.None);
        Assert.NotNull(result.TransitUtc);
        Assert.NotNull(result.RiseUtc);
        Assert.NotNull(result.SetUtc);
        Assert.False(result.Circumpolar);
        Assert.True(result.RiseUtc < result.TransitUtc);
        Assert.True(result.TransitUtc < result.SetUtc);
        Assert.Equal("32349", result.Hip);
    }

    [Fact]
    public async Task GetRiseSet_CircumpolarStar_ReturnsNoEvents()
    {
        var service = Service(Catalog(Sample));
        // Vega (dec 38.8) is circumpolar from Oslo (lat 59.9: threshold dec > 30.1)
        var result = await service.GetRiseSetAsync("91262", new DateOnly(2026, 8, 4),
            ObserverLocation.FromDegrees(59.9, 10.7, 0), CancellationToken.None);
        Assert.True(result.Circumpolar);
        Assert.Null(result.RiseUtc);
        Assert.Null(result.TransitUtc);
    }

    [Fact]
    public async Task GetBrightest_OrdersByMagnitude()
    {
        var service = Service(Catalog(Sample));
        var result = await service.GetBrightestAsync(10, 6.5, null, CancellationToken.None);
        Assert.Equal(3, result.Stars.Count);
        Assert.Equal("Sirius", result.Stars[0].Name);
        Assert.Equal("Arcturus", result.Stars[1].Name);
        Assert.Equal("Vega", result.Stars[2].Name);
    }

    [Fact]
    public void ConstellationAbbreviation_ExpandsToFullName()
    {
        Assert.Equal("Canis Major", StarService.ConstellationName("CMa"));
        Assert.Equal("Ursa Minor", StarService.ConstellationName("UMi"));
        Assert.Equal("UnknownX", StarService.ConstellationName("UnknownX"));
    }
}
