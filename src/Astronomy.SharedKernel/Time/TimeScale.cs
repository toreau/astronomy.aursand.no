namespace Astronomy.SharedKernel.Time;

public enum TimeScale
{
    Utc,
    Tai,
    Tt,
    Ut1,
    Tdb,
}

public readonly record struct AstronomicalTime(JulianDate JulianDate, TimeScale Scale);
