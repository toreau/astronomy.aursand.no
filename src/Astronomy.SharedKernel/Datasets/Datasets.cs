namespace Astronomy.SharedKernel.Datasets;

public sealed record DatasetRef(string Name, string Version);

public sealed record AlgorithmRef(string Name, string Version);

public sealed record CalculationWarning(string Code, string Message);

public sealed record CalculationMetadata(
    IReadOnlyList<DatasetRef> Datasets,
    IReadOnlyList<AlgorithmRef> Algorithms,
    IReadOnlyList<CalculationWarning> Warnings)
{
    public static CalculationMetadata Empty { get; } = new([], [], []);

    public CalculationMetadata WithWarning(CalculationWarning warning) =>
        this with { Warnings = Warnings.Append(warning).ToArray() };
}

public interface IDatasetCatalog
{
    DatasetRef? ActiveVersion(string datasetName);
    string? ResolvePath(string datasetName, string version);
    IReadOnlyList<string> DatasetNames { get; }
}

public interface IDatasetRegistry
{
    DatasetRef? ActiveVersion(string datasetName);
    Task<int> StageAsync(string datasetName, string version, string checksum, CancellationToken ct = default);
    Task<int> ActivateAsync(string datasetName, string version, CancellationToken ct = default);
    Task<int> RollbackAsync(string datasetName, string version, CancellationToken ct = default);
}
