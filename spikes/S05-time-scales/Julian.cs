namespace S05TimeScales;

public static class Julian
{
    private const double UnixEpochJd = 2440587.5;

    public static double ToJd(DateTime utc) => UnixEpochJd + (utc.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds / 86400.0;

    public static double ToMjd(DateTime utc) => ToJd(utc) - 2400000.5;

    public static DateTime FromJd(double jd) =>
        new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds((jd - UnixEpochJd) * 86400.0);
}
