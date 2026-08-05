namespace Astronomy.SharedKernel.Time;

/// <summary>
/// EOP C04 (IERS 14 C04, IAU2000A) sample: UT1-UTC plus polar motion x/y,
/// one sample per day (0h UTC). Used by the reference-tier horizontal chain
/// (ERFA eraC2t06a). Version from the registry dataset.
/// </summary>
public sealed record EopC04Sample(
    DateTimeOffset Utc,
    double Ut1MinusUtcSeconds,
    double XArcsec,
    double YArcsec,
    string DatasetVersion);

public static class EopC04Interpolator
{
    /// <summary>
    /// Linear interpolation of the daily C04 samples at the given instant
    /// (extrapolation-free: returns the nearest sample outside the covered range).
    /// </summary>
    public static (double Ut1MinusUtcSeconds, double XArcsec, double YArcsec) Interpolate(
        IReadOnlyList<EopC04Sample> samples, DateTimeOffset utc)
    {
        if (samples.Count == 0) return (0, 0, 0);
        if (utc <= samples[0].Utc) return (samples[0].Ut1MinusUtcSeconds, samples[0].XArcsec, samples[0].YArcsec);
        var last = samples[^1];
        if (utc >= last.Utc) return (last.Ut1MinusUtcSeconds, last.XArcsec, last.YArcsec);
        for (var i = 0; i < samples.Count - 1; i++)
        {
            if (utc >= samples[i].Utc && utc <= samples[i + 1].Utc)
            {
                var f = (utc - samples[i].Utc).TotalSeconds / (samples[i + 1].Utc - samples[i].Utc).TotalSeconds;
                return (
                    samples[i].Ut1MinusUtcSeconds + f * (samples[i + 1].Ut1MinusUtcSeconds - samples[i].Ut1MinusUtcSeconds),
                    samples[i].XArcsec + f * (samples[i + 1].XArcsec - samples[i].XArcsec),
                    samples[i].YArcsec + f * (samples[i + 1].YArcsec - samples[i].YArcsec));
            }
        }
        return (last.Ut1MinusUtcSeconds, last.XArcsec, last.YArcsec);
    }
}
