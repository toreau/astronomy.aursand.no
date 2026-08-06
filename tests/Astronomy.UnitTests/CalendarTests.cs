using Astronomy.Modules.Calendars.Application;

namespace Astronomy.UnitTests;

public class CalendarTests
{
    private static ICalendarService Service() => new CalendarService();

    [Fact]
    public void ConvertDate_NoTimeZone_ReturnsGregorianAndJulian()
    {
        var result = Service().ConvertDate(new DateOnly(2026, 8, 5), null);
        Assert.Equal("2026-08-05", result.GregorianDate);
        Assert.Equal("2026-W32-3", result.IsoWeekDate);
        Assert.Equal(2461257.5, result.JulianDate, 6);
        Assert.Equal("Wednesday", result.DayOfWeek);
        Assert.Null(result.TimeZone);
        Assert.Null(result.LocalTime);
        Assert.Null(result.UtcOffsetSeconds);
    }

    [Fact]
    public void ConvertDate_OsloSummerTime_ReturnsLocalTimeWithOffset()
    {
        var result = Service().ConvertDate(new DateOnly(2026, 8, 5), "Europe/Oslo");
        Assert.Equal("Europe/Oslo", result.TimeZone);
        Assert.Equal(7200, result.UtcOffsetSeconds);
        Assert.Equal("2026-08-05T12:00:00+02:00", result.LocalTime);
    }

    [Fact]
    public void ConvertDate_OsloWinterTime_ReturnsStandardOffset()
    {
        var result = Service().ConvertDate(new DateOnly(2026, 1, 15), "Europe/Oslo");
        Assert.Equal(3600, result.UtcOffsetSeconds);
        Assert.Equal("2026-01-15T12:00:00+01:00", result.LocalTime);
    }

    [Fact]
    public void ConvertDate_UnknownTimeZone_ReturnsWarningNotError()
    {
        var result = Service().ConvertDate(new DateOnly(2026, 8, 5), "Mars/Olympus");
        Assert.Equal("Mars/Olympus", result.TimeZone);
        Assert.Null(result.LocalTime);
        Assert.Contains(result.Metadata.Warnings, w => w.Code == "AST-6001");
    }

    [Fact]
    public void AddDays_Positive_Adds()
    {
        var result = Service().AddDays(new DateOnly(2026, 8, 5), 7, null);
        Assert.Equal("2026-08-12", result.ResultDate);
    }

    [Fact]
    public void AddDays_Negative_Subtracts()
    {
        var result = Service().AddDays(new DateOnly(2026, 8, 5), -1, null);
        Assert.Equal("2026-08-04", result.ResultDate);
    }

    [Fact]
    public void ConvertRange_FullYear_Returns365Entries()
    {
        var result = Service().ConvertRange(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null);
        Assert.Equal("2026-01-01", result.From);
        Assert.Equal("2026-12-31", result.To);
        Assert.Equal(365, result.Entries.Count);
        Assert.Equal("2026-01-01", result.Entries[0].GregorianDate);
        Assert.Equal("2026-12-31", result.Entries[^1].GregorianDate);
    }

    [Fact]
    public void ConvertRange_LeapYear_Returns366Entries()
    {
        var result = Service().ConvertRange(new DateOnly(2028, 1, 1), new DateOnly(2028, 12, 31), null);
        Assert.Equal(366, result.Entries.Count);
    }

    [Fact]
    public void ConvertRange_SpanTooLong_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            Service().ConvertRange(new DateOnly(2026, 1, 1), new DateOnly(2027, 1, 2), null));
        Assert.Contains("366", ex.Message);
    }

    [Fact]
    public void ConvertRange_FromAfterTo_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            Service().ConvertRange(new DateOnly(2026, 12, 31), new DateOnly(2026, 1, 1), null));
    }

    [Fact]
    public void ConvertRange_TimezoneAppliedToEntries()
    {
        var result = Service().ConvertRange(new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 5), "Europe/Oslo");
        Assert.Single(result.Entries);
        Assert.Equal("Europe/Oslo", result.Entries[0].TimeZone);
        Assert.Equal(7200, result.Entries[0].UtcOffsetSeconds); // CEST
    }

    [Fact]
    public void ConvertRange_SingleDay_Inclusive()
    {
        var result = Service().ConvertRange(new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 5), null);
        Assert.Single(result.Entries);
        Assert.Equal("2026-W32-3", result.Entries[0].IsoWeekDate);
    }
}
