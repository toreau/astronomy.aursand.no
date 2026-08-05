using Astronomy.Modules.Ephemeris.Application;
using Astronomy.SharedKernel.Coordinates;

namespace Astronomy.UnitTests;

public class PlanetTests
{
    private static readonly EphemerisCalculator Calculator = new();

    [Fact]
    public void BodyId_ParsesAllPlanets()
    {
        foreach (var name in new[] { "sun", "moon", "mercury", "venus", "mars", "jupiter", "saturn", "uranus", "neptune" })
            Assert.True(BodyId.TryParse(name, out _), name);
        Assert.False(BodyId.TryParse("pluto", out _));
    }

    [Fact]
    public void Constellation_Regulus_RaInHours_IsLeo()
    {
        var info = CosineKitty.Astronomy.Constellation(10.139, 11.967);
        Assert.Equal("Leo", info.Name);
    }

    [Fact]
    public void Venus_Elongation_2026_08_04_IsPlausible()
    {
        var (elongationDeg, visibility, _) = Calculator.Elongation(BodyId.Venus, new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));
        Assert.InRange(elongationDeg, 0, 180);
        Assert.False(string.IsNullOrEmpty(visibility));
    }

    [Fact]
    public async Task Jupiter_Opposition_Search_FindsOppositionWithElongationNear180()
    {
        var from = new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero);
        var service = new EphemerisService(new StubCatalog());
        var result = await service.GetEventsAsync(from, from.AddYears(1),
            [BodyId.Jupiter], [EventType.Opposition], CancellationToken.None);
        var opposition = Assert.Single(result.Events, e => e.Type == "opposition");
        Assert.True(opposition.ElongationDeg > 175.0, $"elongation {opposition.ElongationDeg:F2}");
        Assert.InRange(opposition.Utc, from, from.AddYears(1));
    }

    [Fact]
    public void Mercury_MaxElongation_Search_FindsEvent()
    {
        var from = new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero);
        var found = Calculator.NextMaxElongation(BodyId.Mercury, from);
        Assert.NotNull(found);
        Assert.InRange(found!.Value, from, from.AddMonths(6));
    }

    [Fact]
    public async Task Visibility_RejectsSunAndMoon()
    {
        var service = new EphemerisService(new StubCatalog());
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetVisibilityAsync(BodyId.Sun,
            new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            ObserverLocation.FromDegrees(59.9, 10.7, 0), PrecisionMode.Consumer, CancellationToken.None));
    }

    [Fact]
    public async Task Visibility_Venus_OsloEvening_IsVisibleTonight()
    {
        var service = new EphemerisService(new StubCatalog());
        var result = await service.GetVisibilityAsync(BodyId.Venus,
            new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            ObserverLocation.FromDegrees(59.9139, 10.7522, 25), PrecisionMode.Consumer, CancellationToken.None);
        Assert.True(result.VisibleTonight, $"venus visibleTonight=false (elongation {result.ElongationDeg:F1})");
    }
}

internal sealed class StubCatalog : Astronomy.SharedKernel.Datasets.IDatasetCatalog
{
    public Astronomy.SharedKernel.Datasets.DatasetRef? ActiveVersion(string datasetName) => null;
    public string? ResolvePath(string datasetName, string version) => null;
    public IReadOnlyList<string> DatasetNames { get; } = [];
}
