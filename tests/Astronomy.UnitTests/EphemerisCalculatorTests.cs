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

    [Fact]
    public void MoonHorizontal_MatchesHorizonsValue()
    {
        // Regression: the horizontal path used the geocentric moon position (parallax
        // missing, altitude ~0.87 deg too high). Values are Horizons-verified
        // (oslo 2026-08-15T12:00Z, no refraction): alt 24.80 az 154.12.
        var calc = Calculator();
        var observer = ObserverLocation.FromDegrees(59.9, 10.7, 0);
        var (alt, az) = calc.Horizontal(BodyId.Moon,
            new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero), observer, refraction: false);
        Assert.InRange(alt, 24.70, 24.90);
        Assert.InRange(az, 154.02, 154.22);
    }

    [Fact]
    public void MoonHorizontal_AppliesParallax()
    {
        // The topocentric altitude must differ from the geocentric one by the
        // moon's parallax (0.3-1.0 deg) at this epoch.
        var calc = Calculator();
        var observer = ObserverLocation.FromDegrees(59.9, 10.7, 0);
        var utc = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var eq = calc.GeocentricEquatorial(BodyId.Moon, utc, apparent: true);
        var t = new CosineKitty.AstroTime(utc.UtcDateTime);
        var engineObserver = new CosineKitty.Observer(59.9, 10.7, 0);
        var geocentric = CosineKitty.Astronomy.Horizon(t, engineObserver, eq.RaDeg / 15.0, eq.DecDeg,
            CosineKitty.Refraction.None);
        var (alt, _) = calc.Horizontal(BodyId.Moon, utc, observer, refraction: false);
        Assert.InRange(Math.Abs(alt - geocentric.altitude), 0.3, 1.0);
    }

    [Fact]
    public void SunHorizontal_UnchangedByParallaxFix()
    {
        // The sun's parallax is ~0.002 deg; the topocentric path must not move it.
        var calc = Calculator();
        var observer = ObserverLocation.FromDegrees(59.9, 10.7, 0);
        var utc = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        var eq = calc.GeocentricEquatorial(BodyId.Sun, utc, apparent: true);
        var t = new CosineKitty.AstroTime(utc.UtcDateTime);
        var engineObserver = new CosineKitty.Observer(59.9, 10.7, 0);
        var geocentric = CosineKitty.Astronomy.Horizon(t, engineObserver, eq.RaDeg / 15.0, eq.DecDeg,
            CosineKitty.Refraction.None);
        var (alt, az) = calc.Horizontal(BodyId.Sun, utc, observer, refraction: false);
        Assert.True(Math.Abs(alt - geocentric.altitude) < 0.01, $"sun alt moved {alt - geocentric.altitude:F4} deg");
        Assert.True(Math.Abs(az - geocentric.azimuth) < 0.01, $"sun az moved {az - geocentric.azimuth:F4} deg");
    }
}
