using Astronomy.SharedKernel.Datasets;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.Infrastructure.Registry;

public class DatasetRegistry : Astronomy.SharedKernel.Datasets.IDatasetRegistry
{
    private readonly Func<RegistryDbContext> _contextFactory;

    public DatasetRegistry(Func<RegistryDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public DatasetRef? ActiveVersion(string datasetName)
    {
        using var ctx = _contextFactory();
        var active = ctx.ActiveDatasets.AsNoTracking().FirstOrDefault(a => a.Name == datasetName);
        return active is null ? null : new DatasetRef(active.Name, active.Version);
    }

    public async Task<int> StageAsync(string datasetName, string version, string checksum, CancellationToken ct = default)
    {
        using var ctx = _contextFactory();
        ctx.Datasets.Add(new DatasetRecord
        {
            Name = datasetName,
            Version = version,
            Status = "staged",
            Checksum = checksum,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        ctx.AuditEntries.Add(new AuditEntry
        {
            Action = "stage",
            Detail = $"{datasetName} {version}",
            AtUtc = DateTimeOffset.UtcNow,
        });
        return await ctx.SaveChangesAsync(ct);
    }

    public async Task<int> ActivateAsync(string datasetName, string version, CancellationToken ct = default)
    {
        using var ctx = _contextFactory();
        var record = await ctx.Datasets.FirstOrDefaultAsync(d => d.Name == datasetName && d.Version == version, ct);
        if (record is null) throw new InvalidOperationException($"dataset {datasetName} {version} not staged");
        record.Status = "active";
        record.ActivatedAtUtc = DateTimeOffset.UtcNow;
        var active = await ctx.ActiveDatasets.FindAsync([datasetName], ct);
        if (active is null) ctx.ActiveDatasets.Add(new ActiveDataset { Name = datasetName, Version = version, ActivatedAtUtc = DateTimeOffset.UtcNow });
        else { active.Version = version; active.ActivatedAtUtc = DateTimeOffset.UtcNow; }
        ctx.AuditEntries.Add(new AuditEntry { Action = "activate", Detail = $"{datasetName} {version}", AtUtc = DateTimeOffset.UtcNow });
        return await ctx.SaveChangesAsync(ct);
    }

    public async Task<int> RollbackAsync(string datasetName, string version, CancellationToken ct = default)
    {
        using var ctx = _contextFactory();
        var record = await ctx.Datasets.FirstOrDefaultAsync(d => d.Name == datasetName && d.Version == version, ct)
            ?? throw new InvalidOperationException($"dataset {datasetName} {version} not found");
        record.Status = "active";
        record.ActivatedAtUtc = DateTimeOffset.UtcNow;
        var active = await ctx.ActiveDatasets.FindAsync([datasetName], ct);
        if (active is null) ctx.ActiveDatasets.Add(new ActiveDataset { Name = datasetName, Version = version, ActivatedAtUtc = DateTimeOffset.UtcNow });
        else { active.Version = version; active.ActivatedAtUtc = DateTimeOffset.UtcNow; }
        ctx.AuditEntries.Add(new AuditEntry { Action = "rollback", Detail = $"{datasetName} {version}", AtUtc = DateTimeOffset.UtcNow });
        return await ctx.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DatasetRecord>> ListAsync(CancellationToken ct = default)
    {
        using var ctx = _contextFactory();
        return await ctx.Datasets.AsNoTracking().OrderBy(d => d.Name).ThenBy(d => d.Version).ToListAsync(ct);
    }

    public async Task<long> CountElementsAsync(string datasetName, CancellationToken ct = default)
    {
        using var ctx = _contextFactory();
        return await ctx.Datasets.LongCountAsync(d => d.Name == datasetName, ct);
    }
}
