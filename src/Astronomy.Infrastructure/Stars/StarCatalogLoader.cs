using System.Globalization;
using Astronomy.SharedKernel.Datasets;
using Astronomy.SharedKernel.Stars;

namespace Astronomy.Infrastructure.Stars;

public static class StarCatalogLoader
{
    public const string DatasetName = "star-catalog-hyg";

    public static StarCatalog LoadStarCatalog(IDatasetCatalog catalog, string dataRoot)
    {
        var active = catalog.ActiveVersion(DatasetName);
        if (active is null) return StarCatalog.Unavailable;
        var path = catalog.ResolvePath(DatasetName, active.Version);
        if (path is null) return StarCatalog.Unavailable;
        var file = Path.Combine(path, "star-catalog-hyg.csv");
        if (!File.Exists(file)) return StarCatalog.Unavailable;

        var stars = new List<StarRecord>(120_000);
        foreach (var line in File.ReadLines(file).Skip(1))
        {
            if (line.Length == 0) continue;
            try
            {
                stars.Add(StarRecord.Parse(line));
            }
            catch (FormatException)
            {
                // skip malformed rows defensively
            }
        }
        return stars.Count == 0
            ? StarCatalog.Unavailable
            : new StarCatalog(stars.ToArray(), active.Version, "ok");
    }
}
