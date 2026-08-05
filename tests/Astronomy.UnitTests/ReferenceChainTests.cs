using Astronomy.Modules.Ephemeris.Reference;
using Astronomy.SharedKernel.Time;

namespace Astronomy.UnitTests;

public class ReferenceChainTests
{
    [Theory]
    [InlineData(1900, -2.79)]
    [InlineData(1920, 21.20)]
    [InlineData(1950, 29.07)]
    [InlineData(1972, 42.25)]
    public void HistoricalDeltaT_MatchesPublishedAnchors(int year, double expected)
    {
        var utc = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, HistoricalDeltaT.Seconds(utc), 0.5);
    }

    [Fact]
    public void HistoricalDeltaT_EraBounds()
    {
        Assert.Equal(1900, HistoricalDeltaT.EraStartUtc.Year);
        Assert.Equal(1972, HistoricalDeltaT.LeapSecondEraStartUtc.Year);
    }

    [Fact]
    public void EopC04Interpolator_MidpointInterpolation()
    {
        var samples = new List<EopC04Sample>
        {
            new(new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero), 0.10, 0.2, -0.1, "v"),
            new(new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero), 0.20, 0.4, 0.1, "v"),
        };
        var (dut1, x, y) = EopC04Interpolator.Interpolate(samples,
            new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(0.15, dut1, 3);
        Assert.Equal(0.3, x, 3);
        Assert.Equal(0.0, y, 3);
    }

    [Fact]
    public void EopC04Interpolator_OutsideRange_UsesNearest()
    {
        var samples = new List<EopC04Sample>
        {
            new(new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero), 0.10, 0.2, -0.1, "v"),
            new(new DateTimeOffset(2026, 8, 5, 0, 0, 0, TimeSpan.Zero), 0.20, 0.4, 0.1, "v"),
        };
        var (dut1, _, _) = EopC04Interpolator.Interpolate(samples, new DateTimeOffset(2026, 8, 6, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(0.20, dut1, 3);
        var empty = EopC04Interpolator.Interpolate([], DateTimeOffset.UtcNow);
        Assert.Equal(0, empty.Ut1MinusUtcSeconds);
    }
}
