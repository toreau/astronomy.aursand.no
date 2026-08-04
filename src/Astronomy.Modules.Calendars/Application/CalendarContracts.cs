using Astronomy.SharedKernel.Datasets;
using NodaTime;
using NodaTime.TimeZones;

namespace Astronomy.Modules.Calendars.Application;

public sealed record DateConversionResult(
    string GregorianDate,
    string IsoWeekDate,
    double JulianDate,
    string? DayOfWeek,
    string? TimeZone,
    string? LocalTime,
    int? UtcOffsetSeconds,
    CalculationMetadata Metadata);

public sealed record DateArithmeticResult(
    string StartDate,
    int DaysAdded,
    string ResultDate,
    string? TimeZone,
    CalculationMetadata Metadata);

public interface ICalendarService
{
    DateConversionResult ConvertDate(DateOnly date, string? timeZone);
    DateArithmeticResult AddDays(DateOnly date, int days, string? timeZone);
}
