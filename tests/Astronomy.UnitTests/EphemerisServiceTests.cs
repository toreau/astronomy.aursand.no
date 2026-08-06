using Astronomy.Modules.Ephemeris.Application;
using Astronomy.SharedKernel.Coordinates;
using Astronomy.SharedKernel.Datasets;

namespace Astronomy.UnitTests;

public class EphemerisServiceTests
{
    private static EphemerisService Service() => new(new StubCatalog());

    [Fact]
    public async Task GetPositionAsync_Geometric_Throws()
    {
        var service = Service();
        var request = new PositionRequest("jupiter", DateTimeOffset.UtcNow,
            ObserverLocation.FromDegrees(0, 0, 0), CoordinateFrame.IcrJ2000, PositionType.Geometric,
            RefractionModel.None, PrecisionMode.Consumer);
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetPositionAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task GetVisibilityAsync_VenusEveningSky_VisibleTonight()
    {
        // Venus at max elongation Aug 2026: an evening star from Oslo.
        var result = await Service().GetVisibilityAsync(BodyId.Venus,
            new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero),
            ObserverLocation.FromDegrees(59.9, 10.7, 0), PrecisionMode.Consumer, CancellationToken.None);
        Assert.True(result.VisibleTonight);
        Assert.True(result.NakedEyeVisible);
        Assert.InRange(result.Magnitude, -5.0, -3.0);
        Assert.InRange(result.ElongationDeg, 35.0, 55.0);
        Assert.False(string.IsNullOrEmpty(result.Constellation));
    }

    [Fact]
    public async Task GetVisibilityAsync_MarsNearSolarConjunction_NotVisibleTonight()
    {
        // Mars solar conjunction ~2026-01-09: rises/sets with the sun -> no night visibility.
        var result = await Service().GetVisibilityAsync(BodyId.Mars,
            new DateTimeOffset(2026, 1, 9, 12, 0, 0, TimeSpan.Zero),
            ObserverLocation.FromDegrees(59.9, 10.7, 0), PrecisionMode.Consumer, CancellationToken.None);
        Assert.False(result.VisibleTonight);
    }

    [Fact]
    public async Task GetEventsAsync_RangeTooLong_Throws()
    {
        var service = Service();
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetEventsAsync(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 15, 0, 0, 0, TimeSpan.Zero),
            [BodyId.Jupiter], [EventType.Opposition], CancellationToken.None));
    }

    [Fact]
    public async Task GetEventsAsync_MaxElongation_OnlyInnerPlanets()
    {
        var service = Service();
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetEventsAsync(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            [BodyId.Jupiter], [EventType.MaxElongation], CancellationToken.None));
    }

    [Fact]
    public async Task GetEventsAsync_JupiterOpposition2026()
    {
        var result = await Service().GetEventsAsync(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            [BodyId.Jupiter], [EventType.Opposition], CancellationToken.None);
        var opposition = result.Events.FirstOrDefault(e => e.Type == "opposition");
        Assert.NotNull(opposition);
        Assert.True(opposition!.ElongationDeg > 150, $"elongation {opposition.ElongationDeg:F1}");
        Assert.InRange(opposition.Utc, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 1, 31, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task GetMoonPhasesAsync_RangeTooLong_Throws()
    {
        var service = Service();
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetMoonPhasesAsync(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 15, 0, 0, 0, TimeSpan.Zero), CancellationToken.None));
    }

    [Fact]
    public async Task GetMoonPhasesAsync_ReturnsQuarters()
    {
        var result = await Service().GetMoonPhasesAsync(
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);
        Assert.InRange(result.Events.Count, 3, 5);
        Assert.All(result.Events, e =>
        {
            Assert.False(string.IsNullOrEmpty(e.Phase));
            Assert.InRange(e.IlluminationFraction, 0.0, 1.0);
        });
    }

    [Fact]
    public async Task GetMoonIlluminationAsync_ReturnsPlausible()
    {
        var result = await Service().GetMoonIlluminationAsync(
            new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero), CancellationToken.None);
        Assert.InRange(result.IlluminationFraction, 0.0, 1.0);
        Assert.False(string.IsNullOrEmpty(result.PhaseName));
    }

    [Fact]
    public async Task GetTwilightAsync_OrderingCivilNauticalAstronomical()
    {
        // Equator on the prime meridian: local time == UTC (no date-line wrap),
        // and all three twilight types occur year-round (Oslo has no astronomical
        // twilight in August).
        var service = Service();
        var observer = ObserverLocation.FromDegrees(0, 0, 0);
        var date = new DateOnly(2026, 8, 15);
        var civil = await service.GetTwilightAsync(date, observer, TwilightType.Civil, PrecisionMode.Consumer, CancellationToken.None);
        var nautical = await service.GetTwilightAsync(date, observer, TwilightType.Nautical, PrecisionMode.Consumer, CancellationToken.None);
        var astro = await service.GetTwilightAsync(date, observer, TwilightType.Astronomical, PrecisionMode.Consumer, CancellationToken.None);
        Assert.NotNull(civil.BeginUtc);
        Assert.NotNull(nautical.BeginUtc);
        Assert.NotNull(astro.BeginUtc);
        // Deeper twilight begins earlier in the morning (-18° before -12° before -6°).
        Assert.True(astro.BeginUtc < nautical.BeginUtc);
        Assert.True(nautical.BeginUtc < civil.BeginUtc);
        Assert.True(civil.BeginUtc < civil.EndUtc);
    }

    private sealed class StubCatalog : IDatasetCatalog
    {
        public IReadOnlyList<string> DatasetNames { get; } = ["leap-seconds", "eop-ut1"];
        public DatasetRef? ActiveVersion(string datasetName) => null;
        public string? ResolvePath(string datasetName, string version) => null;
    }
}
