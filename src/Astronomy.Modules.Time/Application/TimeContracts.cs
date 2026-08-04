using Astronomy.SharedKernel;
using Astronomy.SharedKernel.Datasets;

namespace Astronomy.Modules.Time.Application;

public sealed record JulianDateResult(
    double JulianDate,
    double ModifiedJulianDate,
    string Utc,
    CalculationMetadata Metadata);

public sealed record TimeScalesResult(
    string Utc,
    double TaiJd,
    double TtJd,
    double Ut1Jd,
    double TdbJd,
    double TaiMinusUtcSeconds,
    double TtMinusUtcSeconds,
    double Ut1MinusUtcSeconds,
    double TdbMinusTtSeconds,
    string LeapSecondDatasetVersion,
    string EopDatasetVersion,
    string AlgorithmVersion);

public interface ITimeService
{
    JulianDateResult GetJulianDate(DateTimeOffset utc);
    TimeScalesResult GetTimeScales(DateTimeOffset utc);
}
