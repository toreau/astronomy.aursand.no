using System.Globalization;
using Astronomy.SharedKernel.Datasets;
using Astronomy.SharedKernel.Time;

namespace Astronomy.Infrastructure.Time;

public static class TimeDatasetLoaders
{
    public static LeapSecondTable LoadLeapSeconds(IDatasetCatalog catalog, string dataRoot)
    {
        var active = catalog.ActiveVersion("leap-seconds");
        if (active is null) return LeapSecondTable.Default;
        var path = catalog.ResolvePath("leap-seconds", active.Version);
        if (path is null) return LeapSecondTable.Default;
        var file = Path.Combine(path, "leap-seconds.csv");
        if (!File.Exists(file)) return LeapSecondTable.Default;
        var entries = new List<LeapSecond>();
        foreach (var line in File.ReadAllLines(file).Skip(1))
        {
            var p = line.Split(',');
            if (p.Length < 2) continue;
            entries.Add(new LeapSecond(
                DateTimeOffset.Parse(p[0], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                int.Parse(p[1], CultureInfo.InvariantCulture)));
        }
        return entries.Count > 0 ? new LeapSecondTable(entries, active.Version) : LeapSecondTable.Default;
    }

    public static IReadOnlyList<EopSample> LoadEop(IDatasetCatalog catalog, string dataRoot)
    {
        var active = catalog.ActiveVersion("eop-ut1");
        if (active is null) return [];
        var path = catalog.ResolvePath("eop-ut1", active.Version);
        if (path is null) return [];
        var file = Path.Combine(path, "eop-ut1.csv");
        if (!File.Exists(file)) return [];
        var samples = new List<EopSample>();
        foreach (var line in File.ReadAllLines(file).Skip(1))
        {
            var p = line.Split(',');
            if (p.Length < 2) continue;
            var mjd = double.Parse(p[0], CultureInfo.InvariantCulture);
            var dut1 = double.Parse(p[1], CultureInfo.InvariantCulture);
            var utc = DateTimeOffset.UnixEpoch.AddDays(mjd - 40587.0);
            samples.Add(new EopSample(utc, dut1, active.Version));
        }
        return samples;
    }

    public static TimeScaleConverter CreateTimeScaleConverter(IDatasetCatalog catalog, string dataRoot) =>
        new(LoadLeapSeconds(catalog, dataRoot), LoadEop(catalog, dataRoot));
}
