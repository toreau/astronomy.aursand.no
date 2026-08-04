using SGPdotNET.Propagation;
using SGPdotNET.TLE;

namespace S04Sgp4;

public interface IPropagator
{
    string Name { get; }
    void Init(string line1, string line2);
    (double X, double Y, double Z) PositionAt(double minutesSinceEpoch);
}

public sealed class SgpNetAdapter : IPropagator
{
    public string Name => "SGP.NET 1.5.0";
    private Sgp4? _sgp4;

    public void Init(string line1, string line2) => _sgp4 = new Sgp4(new Tle(line1, line2));

    public (double X, double Y, double Z) PositionAt(double minutesSinceEpoch)
    {
        var eci = _sgp4!.FindPosition(minutesSinceEpoch);
        var v = eci.Position;
        return (v.X, v.Y, v.Z);
    }
}
