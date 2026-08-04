using Astronomy.SharedKernel.Units;

namespace Astronomy.SharedKernel.Coordinates;

public enum CoordinateFrame
{
    IcrJ2000,
    EquatorialOfDate,
    Horizontal,
    EciTeme,
    EcefItrf,
}

public enum PositionType
{
    Geometric,
    Astrometric,
    Apparent,
}

public enum RefractionModel
{
    None,
    Simple,
}

public enum PrecisionMode
{
    Consumer,
    Advanced,
    Reference,
}

public sealed record ObserverLocation(
    Angle Latitude,
    Angle Longitude,
    double ElevationMeters,
    string Datum = "WGS84")
{
    public static ObserverLocation FromDegrees(double latitudeDeg, double longitudeDeg, double elevationMeters) =>
        new(new Angle(latitudeDeg), new Angle(longitudeDeg), elevationMeters);
}
