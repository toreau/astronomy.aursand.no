using Astronomy.SharedKernel.Time;

namespace Astronomy.UnitTests;

public class TimeScaleConverterTests
{
    private static TimeScaleConverter Converter() =>
        new(LeapSecondTable.Default, []);

    [Fact]
    public void UnixEpoch_Jd_Is2440587_5()
    {
        var jd = JulianDate.FromUnixSeconds(0);
        Assert.Equal(2440587.5, jd.Value, 9);
    }

    [Fact]
    public void J2000_Tt_Jd_Is2451545()
    {
        var utc = new DateTimeOffset(2000, 1, 1, 11, 58, 55, TimeSpan.Zero).AddTicks(8160000);
        var r = Converter().Convert(utc);
        Assert.Equal(2451545.0, r.TtJd.Value, 6);
    }

    [Fact]
    public void TtJd_At2000Noon_IsUtcJdPlus64_184s()
    {
        var utc = new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var r = Converter().Convert(utc);
        Assert.Equal(2451545.0 + 64.184 / 86400.0, r.TtJd.Value, 8);
    }

    [Fact]
    public void TtMinusUtc_At2000_Is64_184()
    {
        var utc = new DateTimeOffset(2000, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var r = Converter().Convert(utc);
        Assert.Equal(64.184, r.TtMinusUtcSeconds, 3);
    }

    [Fact]
    public void TtMinusUtc_2026_Is69_184()
    {
        var utc = new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
        var r = Converter().Convert(utc);
        Assert.Equal(69.184, r.TtMinusUtcSeconds, 3);
    }

    [Fact]
    public void LeapSecondBoundary_2016_12_31_Is36()
    {
        var before = new DateTimeOffset(2016, 12, 31, 23, 59, 59, TimeSpan.Zero);
        var after = new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(36, Converter().Convert(before).TaiMinusUtcSeconds);
        Assert.Equal(37, Converter().Convert(after).TaiMinusUtcSeconds);
    }

    [Fact]
    public void TdbMinusTt_Band_Under1_7ms()
    {
        var r = Converter().Convert(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));
        Assert.True(Math.Abs(r.TdbMinusTtSeconds) < 0.0017, $"TDB-TT {r.TdbMinusTtSeconds * 1000:F3} ms");
    }

    [Fact]
    public void Ut1_Applied_FromSamples()
    {
        var samples = new List<EopSample>
        {
            new(new DateTimeOffset(2026, 8, 3, 0, 0, 0, TimeSpan.Zero), 0.25, "test"),
            new(new DateTimeOffset(2026, 8, 4, 0, 0, 0, TimeSpan.Zero), 0.36, "test"),
        };
        var converter = new TimeScaleConverter(LeapSecondTable.Default, samples);
        var r = converter.Convert(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(0.36, r.Ut1MinusUtcSeconds, 6);
    }

    [Fact]
    public void Mjd_Conversion_RoundTrips()
    {
        var jd = JulianDate.FromUnixSeconds(0);
        var mjd = jd.ToMjd();
        Assert.Equal(40587.0, mjd.Value, 9);
        Assert.Equal(jd.Value, mjd.ToJd().Value, 9);
    }
}
