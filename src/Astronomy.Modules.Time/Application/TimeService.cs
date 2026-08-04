using Astronomy.SharedKernel.Datasets;
using Astronomy.SharedKernel.Time;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.Modules.Time.Application;

internal sealed class TimeService : ITimeService
{
    private readonly TimeScaleConverter _converter;
    private readonly IDatasetCatalog _catalog;

    public TimeService(TimeScaleConverter converter, IDatasetCatalog catalog)
    {
        _converter = converter;
        _catalog = catalog;
    }

    public JulianDateResult GetJulianDate(DateTimeOffset utc)
    {
        var result = _converter.Convert(utc);
        return new JulianDateResult(
            result.UtcJd.Value,
            result.UtcJd.ToMjd().Value,
            utc.ToString("O"),
            MetadataFrom(result));
    }

    public TimeScalesResult GetTimeScales(DateTimeOffset utc)
    {
        var r = _converter.Convert(utc);
        return new TimeScalesResult(
            utc.ToString("O"),
            r.TaiJd.Value,
            r.TtJd.Value,
            r.Ut1Jd.Value,
            r.TdbJd.Value,
            r.TaiMinusUtcSeconds,
            r.TtMinusUtcSeconds,
            r.Ut1MinusUtcSeconds,
            r.TdbMinusTtSeconds,
            r.LeapSecondDatasetVersion,
            r.EopDatasetVersion,
            r.AlgorithmVersion);
    }

    private static CalculationMetadata MetadataFrom(TimeScaleConversionResult r) =>
        new(
            [new DatasetRef("leap-seconds", r.LeapSecondDatasetVersion), new DatasetRef("eop-ut1", r.EopDatasetVersion)],
            [new AlgorithmRef("time-scale-converter", r.AlgorithmVersion)],
            []);
}

public static class TimeModuleRegistrar
{
    public static IServiceCollection AddTimeModule(this IServiceCollection services, TimeScaleConverter converter)
    {
        services.AddSingleton(converter);
        services.AddSingleton<ITimeService, TimeService>();
        return services;
    }
}
