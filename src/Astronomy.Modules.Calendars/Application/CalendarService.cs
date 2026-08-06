using Astronomy.SharedKernel.Datasets;
using Astronomy.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using NodaTime.TimeZones;

namespace Astronomy.Modules.Calendars.Application;

internal sealed class CalendarService : ICalendarService
{
    public DateConversionResult ConvertDate(DateOnly date, string? timeZone)
    {
        var jd = JulianDate.FromDateTimeUtc(new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        var isoYear = System.Globalization.ISOWeek.GetYear(date);
        var isoWeek = System.Globalization.ISOWeek.GetWeekOfYear(date);
        var isoDay = ((int)date.DayOfWeek + 6) % 7 + 1;
        var isoWeekDate = $"{isoYear}-W{isoWeek:D2}-{isoDay}";
        var metadata = Metadata();
        if (timeZone is null)
        {
            return new DateConversionResult(date.ToString("yyyy-MM-dd"), isoWeekDate, jd.Value,
                date.DayOfWeek.ToString(), null, null, null, metadata);
        }

        var zone = DateTimeZoneProviders.Tzdb.GetZoneOrNull(timeZone);
        if (zone is null)
            return new DateConversionResult(date.ToString("yyyy-MM-dd"), isoWeekDate, jd.Value,
                date.DayOfWeek.ToString(), timeZone, null, null,
                metadata.WithWarning(new CalculationWarning("AST-6001", $"unknown timezone '{timeZone}'; returned without local time")));

        var local = new LocalDateTime(date.Year, date.Month, date.Day, 12, 0);
        var zoned = zone.AtStrictly(local);
        var offset = zoned.Offset.ToTimeSpan().TotalSeconds;
        return new DateConversionResult(date.ToString("yyyy-MM-dd"), isoWeekDate, jd.Value,
            date.DayOfWeek.ToString(), timeZone, zoned.ToDateTimeOffset().ToString("yyyy-MM-ddTHH:mm:sszzz"), (int)offset, metadata);
    }

    public DateArithmeticResult AddDays(DateOnly date, int days, string? timeZone)
    {
        var result = date.AddDays(days);
        return new DateArithmeticResult(date.ToString("yyyy-MM-dd"), days, result.ToString("yyyy-MM-dd"), timeZone, Metadata());
    }

    /// <summary>Bulk conversion for an inclusive date range (capped at 366 days).</summary>
    public CalendarRangeResult ConvertRange(DateOnly from, DateOnly to, string? timeZone)
    {
        if (to < from)
            throw new ArgumentException($"to ({to:yyyy-MM-dd}) precedes from ({from:yyyy-MM-dd})");
        var days = to.DayNumber - from.DayNumber + 1;
        if (days > 366)
            throw new ArgumentException($"range exceeds 366 days (got {days})");
        var entries = new List<DateConversionResult>(days);
        for (var d = from; d <= to; d = d.AddDays(1))
            entries.Add(ConvertDate(d, timeZone));
        return new CalendarRangeResult(from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"), timeZone, entries, Metadata());
    }

    private static CalculationMetadata Metadata() =>
        new([new DatasetRef("tzdb", TzdbDateTimeZoneSource.Default.VersionId ?? "unknown")],
            [new AlgorithmRef("gregorian-conversion", "1.0")], []);
}

public static class CalendarsModuleRegistrar
{
    public static IServiceCollection AddCalendarsModule(this IServiceCollection services)
    {
        services.AddSingleton<ICalendarService, CalendarService>();
        return services;
    }
}
