using Astronomy.SharedKernel.Coordinates;

namespace Astronomy.Api;

/// <summary>Shared query-parameter parsing used across the endpoint domains.</summary>
internal static class EndpointHelpers
{
    public static CoordinateFrame ParseFrame(string? frame) => frame?.ToLowerInvariant() switch
    {
        null => CoordinateFrame.EquatorialOfDate,
        "icrs" or "j2000" => CoordinateFrame.IcrJ2000,
        "of-date" or "equatorial-of-date" => CoordinateFrame.EquatorialOfDate,
        "horizontal" or "alt-az" => CoordinateFrame.Horizontal,
        _ => throw new ArgumentException($"unknown frame '{frame}' (supported: icrs, of-date, horizontal)"),
    };

    public static PositionType ParsePositionType(string? positionType) => positionType?.ToLowerInvariant() switch
    {
        null => PositionType.Apparent,
        "astrometric" => PositionType.Astrometric,
        "apparent" => PositionType.Apparent,
        "geometric" => PositionType.Geometric,
        _ => throw new ArgumentException($"unknown positionType '{positionType}'"),
    };

    public static RefractionModel ParseRefraction(string? refraction) => refraction?.ToLowerInvariant() switch
    {
        null or "none" => RefractionModel.None,
        "simple" or "standard" => RefractionModel.Simple,
        _ => throw new ArgumentException($"unknown refraction '{refraction}'"),
    };

    public static PrecisionMode ParsePrecision(string? precision) => precision?.ToLowerInvariant() switch
    {
        null or "consumer" => PrecisionMode.Consumer,
        "advanced" => PrecisionMode.Advanced,
        "reference" => PrecisionMode.Reference,
        _ => throw new ArgumentException($"unknown precision '{precision}'"),
    };

    public static DateTimeOffset ParseTime(string? time) =>
        time is null
            ? DateTimeOffset.UtcNow
            : DateTimeOffset.TryParse(time, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed
                : throw new ArgumentException($"invalid time '{time}' (expected ISO 8601)");

    public static DateOnly ParseDate(string date) =>
        DateOnly.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed)
            ? parsed
            : throw new ArgumentException($"invalid date '{date}' (expected yyyy-MM-dd)");

    public static ObserverLocation ObserverLocationFrom(double? latitude, double? longitude, double? elevationMeters) =>
        ObserverLocation.FromDegrees(
            latitude ?? throw new ArgumentException("latitude required"),
            longitude ?? throw new ArgumentException("longitude required"),
            elevationMeters ?? 0);
}
