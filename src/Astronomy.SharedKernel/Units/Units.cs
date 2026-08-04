namespace Astronomy.SharedKernel.Units;

public readonly record struct Angle(double Degrees)
{
    public double Radians => Degrees * Math.PI / 180.0;

    public double ArcSeconds => Degrees * 3600.0;

    public static Angle FromRadians(double radians) => new(radians * 180.0 / Math.PI);

    public static Angle FromArcSeconds(double arcSeconds) => new(arcSeconds / 3600.0);

    public Angle NormalizedTo360()
    {
        var d = Degrees % 360.0;
        return new Angle(d < 0 ? d + 360.0 : d);
    }
}

public readonly record struct Distance(double Kilometers)
{
    public double AstronomicalUnits => Kilometers / 149597870.7;

    public static Distance FromAstronomicalUnits(double au) => new(au * 149597870.7);
}

public readonly record struct Velocity(double KilometersPerSecond);
