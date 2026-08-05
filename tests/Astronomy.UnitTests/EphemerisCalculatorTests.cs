using Astronomy.Modules.Ephemeris.Application;
using Astronomy.SharedKernel.Coordinates;

namespace Astronomy.UnitTests;

public class EphemerisCalculatorTests
{
    private static EphemerisCalculator Calculator() => new();

    [Fact]
    public void Ra_IsReturnedInDegrees()
    {
        var calc = Calculator();
        var eq = calc.GeocentricEquatorial(BodyId.Sun, new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero), apparent: false);
        Assert.InRange(eq.RaDeg, 0, 360);
        Assert.Equal(282.559, eq.RaDeg, 2);
        Assert.Equal(-22.950, eq.DecDeg, 2);
    }

    [Fact]
    public void SunDistance_At2000_IsAboutOneAu()
    {
        var calc = Calculator();
        var eq = calc.GeocentricEquatorial(BodyId.Sun, new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero), apparent: false);
        Assert.InRange(eq.DistanceKm, 146000000, 153000000);
    }

    [Fact]
    public void MoonQuarters_2026_CountMatchesUsno()
    {
        var calc = Calculator();
        var quarters = calc.MoonQuarters(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.InRange(quarters.Count, 49, 51);
    }

    [Fact]
    public void OsloSunrise_2026_08_04_MatchesUsno()
    {
        var calc = Calculator();
        var observer = ObserverLocation.FromDegrees(59.9139, 10.7522, 25);
        var rise = calc.SearchRiseSet(BodyId.Sun, new DateOnly(2026, 8, 4), observer, rise: true);
        Assert.NotNull(rise);
        var expected = new DateTimeOffset(2026, 8, 4, 3, 5, 12, TimeSpan.Zero);
        Assert.True(Math.Abs((rise!.Value - expected).TotalSeconds) < 30, $"rise {rise.Value:O}");
    }

    [Fact]
    public void MoonPhaseNames_MapCorrectly()
    {
        Assert.Equal("New Moon", EphemerisCalculator.MoonPhaseName(0));
        Assert.Equal("First Quarter", EphemerisCalculator.MoonPhaseName(1));
        Assert.Equal("Full Moon", EphemerisCalculator.MoonPhaseName(2));
        Assert.Equal("Last Quarter", EphemerisCalculator.MoonPhaseName(3));
    }
}
