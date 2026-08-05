using System.Globalization;
using Astronomy.SharedKernel.Datasets;
using Astronomy.SharedKernel.Time;

namespace Astronomy.Infrastructure.Time;

public static class EopC04Loader
{
    public const string DatasetName = "eop-c04";

    /// <summary>
    /// Loads the active eop-c04 dataset (mjd, ut1_minus_utc_seconds, x_arcsec,
    /// y_arcsec) into a sorted sample list. Missing dataset returns an empty
    /// list - the reference horizontal chain then degrades with a warning.
    /// </summary>
    public static IReadOnlyList<EopC04Sample> LoadEopC04(IDatasetCatalog catalog, string dataRoot)
    {
        var active = catalog.ActiveVersion(DatasetName);
        if (active is null) return [];
        var path = catalog.ResolvePath(DatasetName, active.Version);
        if (path is null) return [];
        var file = Path.Combine(path, "eop-c04.csv");
        if (!File.Exists(file)) return [];

        var samples = new List<EopC04Sample>();
        foreach (var line in File.ReadAllLines(file).Skip(1))
        {
            var p = line.Split(',');
            if (p.Length < 4) continue;
            if (!double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var mjd)) continue;
            if (!double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var dut1)) continue;
            if (!double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) continue;
            if (!double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) continue;
            var utc = DateTimeOffset.UnixEpoch.AddDays(mjd - 40587.0);
            samples.Add(new EopC04Sample(utc, dut1, x, y, active.Version));
        }
        samples.Sort((a, b) => a.Utc.CompareTo(b.Utc));
        return samples;
    }
}
