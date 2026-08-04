namespace Astronomy.SharedKernel.Time;

public readonly record struct LeapSecond(DateTimeOffset EffectiveUtc, int TaiMinusUtc);

public sealed record LeapSecondTable(IReadOnlyList<LeapSecond> Entries, string DatasetVersion, string AlgorithmVersion = "iers-bulletin-c")
{
    private static readonly LeapSecond[] DefaultEntries =
    {
        new(new DateTimeOffset(1972, 1, 1, 0, 0, 0, TimeSpan.Zero), 10),
        new(new DateTimeOffset(1972, 7, 1, 0, 0, 0, TimeSpan.Zero), 11),
        new(new DateTimeOffset(1973, 1, 1, 0, 0, 0, TimeSpan.Zero), 12),
        new(new DateTimeOffset(1974, 1, 1, 0, 0, 0, TimeSpan.Zero), 13),
        new(new DateTimeOffset(1975, 1, 1, 0, 0, 0, TimeSpan.Zero), 14),
        new(new DateTimeOffset(1976, 1, 1, 0, 0, 0, TimeSpan.Zero), 15),
        new(new DateTimeOffset(1977, 1, 1, 0, 0, 0, TimeSpan.Zero), 16),
        new(new DateTimeOffset(1978, 1, 1, 0, 0, 0, TimeSpan.Zero), 17),
        new(new DateTimeOffset(1979, 1, 1, 0, 0, 0, TimeSpan.Zero), 18),
        new(new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero), 19),
        new(new DateTimeOffset(1981, 7, 1, 0, 0, 0, TimeSpan.Zero), 20),
        new(new DateTimeOffset(1982, 7, 1, 0, 0, 0, TimeSpan.Zero), 21),
        new(new DateTimeOffset(1983, 7, 1, 0, 0, 0, TimeSpan.Zero), 22),
        new(new DateTimeOffset(1985, 7, 1, 0, 0, 0, TimeSpan.Zero), 23),
        new(new DateTimeOffset(1988, 1, 1, 0, 0, 0, TimeSpan.Zero), 24),
        new(new DateTimeOffset(1990, 1, 1, 0, 0, 0, TimeSpan.Zero), 25),
        new(new DateTimeOffset(1991, 1, 1, 0, 0, 0, TimeSpan.Zero), 26),
        new(new DateTimeOffset(1992, 7, 1, 0, 0, 0, TimeSpan.Zero), 27),
        new(new DateTimeOffset(1993, 7, 1, 0, 0, 0, TimeSpan.Zero), 28),
        new(new DateTimeOffset(1994, 7, 1, 0, 0, 0, TimeSpan.Zero), 29),
        new(new DateTimeOffset(1996, 1, 1, 0, 0, 0, TimeSpan.Zero), 30),
        new(new DateTimeOffset(1997, 7, 1, 0, 0, 0, TimeSpan.Zero), 31),
        new(new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero), 32),
        new(new DateTimeOffset(2006, 1, 1, 0, 0, 0, TimeSpan.Zero), 33),
        new(new DateTimeOffset(2009, 1, 1, 0, 0, 0, TimeSpan.Zero), 34),
        new(new DateTimeOffset(2012, 7, 1, 0, 0, 0, TimeSpan.Zero), 35),
        new(new DateTimeOffset(2015, 7, 1, 0, 0, 0, TimeSpan.Zero), 36),
        new(new DateTimeOffset(2017, 1, 1, 0, 0, 0, TimeSpan.Zero), 37),
    };

    public static LeapSecondTable Default { get; } = new(DefaultEntries, "iers-2026a");

    public const double TaiMinusTtSeconds = 32.184;

    public int TaiMinusUtc(DateTimeOffset utcUtc)
    {
        var offset = 10;
        foreach (var entry in Entries)
            if (utcUtc >= entry.EffectiveUtc)
                offset = entry.TaiMinusUtc;
        return offset;
    }
}
