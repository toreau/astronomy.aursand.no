namespace Astronomy.SharedKernel.Time;

public readonly record struct JulianDate(double Value)
{
    public const double UnixEpochJd = 2440587.5;

    public static JulianDate FromUnixSeconds(long unixSeconds) => new(UnixEpochJd + unixSeconds / 86400.0);

    public static JulianDate FromUnixSeconds(double unixSeconds) => new(UnixEpochJd + unixSeconds / 86400.0);

    public static JulianDate FromDateTimeUtc(DateTimeOffset utc) =>
        new(UnixEpochJd + (utc.ToUniversalTime() - DateTimeOffset.UnixEpoch).TotalSeconds / 86400.0);

    public DateTimeOffset ToDateTimeUtc() =>
        DateTimeOffset.UnixEpoch.AddSeconds((Value - UnixEpochJd) * 86400.0);

    public ModifiedJulianDate ToMjd() => new(Value - 2400000.5);
}

public readonly record struct ModifiedJulianDate(double Value)
{
    public JulianDate ToJd() => new(Value + 2400000.5);
}
