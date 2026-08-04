namespace Astronomy.SharedKernel.Time;

public sealed record EopSample(DateTimeOffset Ut1Date, double Ut1MinusUtcSeconds, string DatasetVersion);

public sealed record TimeScaleConversionResult(
    JulianDate UtcJd,
    JulianDate TaiJd,
    JulianDate TtJd,
    JulianDate Ut1Jd,
    JulianDate TdbJd,
    double TaiMinusUtcSeconds,
    double TtMinusUtcSeconds,
    double Ut1MinusUtcSeconds,
    double TdbMinusTtSeconds,
    string LeapSecondDatasetVersion,
    string EopDatasetVersion,
    string AlgorithmVersion);

public sealed class TimeScaleConverter
{
    public const double TaiMinusTtSeconds = 32.184;
    public const string AlgorithmVersion = "leap-chain-1.0";

    private readonly LeapSecondTable _leapSeconds;
    private readonly IReadOnlyList<EopSample> _eopSamples;

    public TimeScaleConverter(LeapSecondTable leapSeconds, IReadOnlyList<EopSample> eopSamples)
    {
        _leapSeconds = leapSeconds;
        _eopSamples = eopSamples.OrderBy(s => s.Ut1Date).ToArray();
    }

    public TimeScaleConversionResult Convert(DateTimeOffset utcUtc)
    {
        var taiMinusUtc = _leapSeconds.TaiMinusUtc(utcUtc);
        var ttMinusUtc = taiMinusUtc + TaiMinusTtSeconds;

        var taiJd = JulianDate.FromDateTimeUtc(utcUtc.AddSeconds(taiMinusUtc));
        var ttJd = JulianDate.FromDateTimeUtc(utcUtc.AddSeconds(ttMinusUtc));

        var ut1MinusUtc = Ut1MinusUtc(utcUtc);
        var ut1Jd = JulianDate.FromDateTimeUtc(utcUtc.AddSeconds(ut1MinusUtc));

        var tdbMinusTt = TdbMinusTtSeconds(ttJd.Value);
        var tdbJd = new JulianDate(ttJd.Value + tdbMinusTt / 86400.0);

        return new TimeScaleConversionResult(
            JulianDate.FromDateTimeUtc(utcUtc),
            taiJd,
            ttJd,
            ut1Jd,
            tdbJd,
            taiMinusUtc,
            ttMinusUtc,
            ut1MinusUtc,
            tdbMinusTt,
            _leapSeconds.DatasetVersion,
            EopDatasetVersion,
            AlgorithmVersion);
    }

    private double Ut1MinusUtc(DateTimeOffset utcUtc)
    {
        if (_eopSamples.Count == 0) return 0;
        var target = utcUtc.ToUniversalTime();
        var last = _eopSamples[0];
        foreach (var sample in _eopSamples)
        {
            if (sample.Ut1Date > target) break;
            last = sample;
        }
        return last.Ut1MinusUtcSeconds;
    }

    private static double TdbMinusTtSeconds(double ttJd)
    {
        var g = 357.53 + 0.9856003 * (ttJd - 2451545.0);
        var l = 246.11 + 0.90251792 * (ttJd - 2451545.0);
        var gRad = g * Math.PI / 180.0;
        var lRad = l * Math.PI / 180.0;
        return 0.001657 * Math.Sin(gRad + 0.01671 * Math.Sin(gRad)) + 0.000022 * Math.Sin(lRad);
    }

    private string EopDatasetVersion => _eopSamples.Count > 0 ? _eopSamples[^1].DatasetVersion : "none";
}
