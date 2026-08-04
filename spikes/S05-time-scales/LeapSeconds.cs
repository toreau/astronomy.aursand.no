using System.Globalization;

namespace S05TimeScales;

public static class LeapSeconds
{
    public static readonly (DateTime Utc, int TaiMinusUtc)[] Table =
    {
        (new DateTime(1972, 1, 1, 0, 0, 0, DateTimeKind.Utc), 10),
        (new DateTime(1972, 7, 1, 0, 0, 0, DateTimeKind.Utc), 11),
        (new DateTime(1973, 1, 1, 0, 0, 0, DateTimeKind.Utc), 12),
        (new DateTime(1974, 1, 1, 0, 0, 0, DateTimeKind.Utc), 13),
        (new DateTime(1975, 1, 1, 0, 0, 0, DateTimeKind.Utc), 14),
        (new DateTime(1976, 1, 1, 0, 0, 0, DateTimeKind.Utc), 15),
        (new DateTime(1977, 1, 1, 0, 0, 0, DateTimeKind.Utc), 16),
        (new DateTime(1978, 1, 1, 0, 0, 0, DateTimeKind.Utc), 17),
        (new DateTime(1979, 1, 1, 0, 0, 0, DateTimeKind.Utc), 18),
        (new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc), 19),
        (new DateTime(1981, 7, 1, 0, 0, 0, DateTimeKind.Utc), 20),
        (new DateTime(1982, 7, 1, 0, 0, 0, DateTimeKind.Utc), 21),
        (new DateTime(1983, 7, 1, 0, 0, 0, DateTimeKind.Utc), 22),
        (new DateTime(1985, 7, 1, 0, 0, 0, DateTimeKind.Utc), 23),
        (new DateTime(1988, 1, 1, 0, 0, 0, DateTimeKind.Utc), 24),
        (new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc), 25),
        (new DateTime(1991, 1, 1, 0, 0, 0, DateTimeKind.Utc), 26),
        (new DateTime(1992, 7, 1, 0, 0, 0, DateTimeKind.Utc), 27),
        (new DateTime(1993, 7, 1, 0, 0, 0, DateTimeKind.Utc), 28),
        (new DateTime(1994, 7, 1, 0, 0, 0, DateTimeKind.Utc), 29),
        (new DateTime(1996, 1, 1, 0, 0, 0, DateTimeKind.Utc), 30),
        (new DateTime(1997, 7, 1, 0, 0, 0, DateTimeKind.Utc), 31),
        (new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc), 32),
        (new DateTime(2006, 1, 1, 0, 0, 0, DateTimeKind.Utc), 33),
        (new DateTime(2009, 1, 1, 0, 0, 0, DateTimeKind.Utc), 34),
        (new DateTime(2012, 7, 1, 0, 0, 0, DateTimeKind.Utc), 35),
        (new DateTime(2015, 7, 1, 0, 0, 0, DateTimeKind.Utc), 36),
        (new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc), 37),
    };

    public static int TaiMinusUtc(DateTime utc) => TaiMinusUtc(Julian.ToMjd(utc));

    public static int TaiMinusUtc(double mjd)
    {
        var offset = 10;
        foreach (var (date, taiMinusUtc) in Table)
            if (mjd >= Julian.ToMjd(date)) offset = taiMinusUtc;
        return offset;
    }

    public const double TaiMinusTt = 32.184;
}
