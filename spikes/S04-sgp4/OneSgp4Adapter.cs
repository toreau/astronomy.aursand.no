using One_Sgp4;
using One_Sgp4.omm;

namespace S04Sgp4;

public sealed class OneSgp4Adapter : IPropagator
{
    public string Name => "One_Sgp4 1.1.0";
    private Tle? _tle;
    private EpochTime? _epoch;

    public void Init(string line1, string line2)
    {
        _tle = ParserTLE.parseTle(line1, line2, "v");
        var (year, day) = Fixtures.Epoch(new VerCase("", line1, line2, []));
        _epoch = new EpochTime(year, day);
    }

    public (double X, double Y, double Z) PositionAt(double minutesSinceEpoch)
    {
        var t = new EpochTime(_epoch!);
        t.addMinutes(minutesSinceEpoch);
        var p = SatFunctions.getSatPositionAtTime(_tle!, t, Sgp4.wgsConstant.WGS_72);
        return (p.getX(), p.getY(), p.getZ());
    }
}
