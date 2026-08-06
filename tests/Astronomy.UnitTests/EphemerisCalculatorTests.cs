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

    [Fact]
    public void MoonPhase01_Bands_NameCorrectly()
    {
        Assert.Equal("New Moon", EphemerisCalculator.MoonPhaseName(0.0));
        Assert.Equal("New Moon", EphemerisCalculator.MoonPhaseName(0.98));
        Assert.Equal("Waxing Crescent", EphemerisCalculator.MoonPhaseName(0.1));
        Assert.Equal("First Quarter", EphemerisCalculator.MoonPhaseName(0.25));
        Assert.Equal("Waxing Gibbous", EphemerisCalculator.MoonPhaseName(0.4));
        Assert.Equal("Full Moon", EphemerisCalculator.MoonPhaseName(0.5));
        Assert.Equal("Waning Gibbous", EphemerisCalculator.MoonPhaseName(0.6));
        Assert.Equal("Last Quarter", EphemerisCalculator.MoonPhaseName(0.75));
        Assert.Equal("Waning Crescent", EphemerisCalculator.MoonPhaseName(0.9));
    }

    [Theory]
    [InlineData(2026, 8, 8, "Waning Crescent")]    // between last quarter (Aug 6) and new (Aug 12)
    [InlineData(2026, 8, 9, "Waning Crescent")]
    [InlineData(2026, 8, 13, "New Moon")]          // 18 h after new — still inside the new band
    [InlineData(2026, 8, 15, "Waxing Crescent")]   // mid-way to first quarter
    [InlineData(2026, 8, 22, "Waxing Gibbous")]    // after first quarter (Aug 20)
    [InlineData(2026, 8, 23, "Waxing Gibbous")]
    [InlineData(2026, 8, 29, "Full Moon")]         // 1.3 d after full (Aug 28) — inside the full band
    [InlineData(2026, 8, 31, "Waning Gibbous")]    // waning toward last quarter (Sep 4)
    public void MoonPhaseName_August2026_DaysMatchPhase(int year, int month, int day, string expected)
    {
        var calc = Calculator();
        var utc = new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, EphemerisCalculator.MoonPhaseName(calc.MoonPhase01(utc)));
    }

    [Fact]
    public void MoonPhaseName_QuarterInstants_2026()
    {
        var calc = Calculator();
        // Quarter instants from the live /moon/phases (UTC): LQ Aug 6 02:21, New Aug 12 17:37,
        // FQ Aug 20 02:46, Full Aug 28 04:19 — at noon UTC each day the phase is in-band.
        Assert.Equal("Last Quarter", EphemerisCalculator.MoonPhaseName(calc.MoonPhase01(new DateTimeOffset(2026, 8, 6, 12, 0, 0, TimeSpan.Zero))));
        Assert.Equal("New Moon", EphemerisCalculator.MoonPhaseName(calc.MoonPhase01(new DateTimeOffset(2026, 8, 12, 12, 0, 0, TimeSpan.Zero))));
        Assert.Equal("First Quarter", EphemerisCalculator.MoonPhaseName(calc.MoonPhase01(new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero))));
        Assert.Equal("Full Moon", EphemerisCalculator.MoonPhaseName(calc.MoonPhase01(new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero))));
    }
}
