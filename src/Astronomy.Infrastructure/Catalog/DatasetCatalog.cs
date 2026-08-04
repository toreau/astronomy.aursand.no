using Astronomy.SharedKernel.Datasets;
using Astronomy.Infrastructure.Registry;

namespace Astronomy.Infrastructure.Catalog;

public class DatasetCatalog : IDatasetCatalog
{
    private readonly IDatasetRegistry _registry;
    private readonly string _dataRoot;

    public DatasetCatalog(IDatasetRegistry registry, string dataRoot)
    {
        _registry = registry;
        _dataRoot = dataRoot;
    }

    public IReadOnlyList<string> DatasetNames { get; } = ["leap-seconds", "eop-ut1"];

    public DatasetRef? ActiveVersion(string datasetName) => _registry.ActiveVersion(datasetName);

    public string? ResolvePath(string datasetName, string version)
    {
        var path = Path.Combine(_dataRoot, "datasets", datasetName, version);
        return Directory.Exists(path) ? path : null;
    }
}
