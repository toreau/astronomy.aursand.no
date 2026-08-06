using System.Collections.Concurrent;
using Astronomy.SharedKernel.Datasets;
using Microsoft.EntityFrameworkCore;

namespace Astronomy.Infrastructure.Registry;

public class DatasetRegistry : Astronomy.SharedKernel.Datasets.IDatasetRegistry
{
    private static readonly TimeSpan ActiveVersionCacheTtl = TimeSpan.FromSeconds(30);

    private readonly Func<RegistryDbContext> _contextFactory;
    private readonly ConcurrentDictionary<string, (DateTimeOffset ExpiresAt, DatasetRef? Value)> _activeCache = new(StringComparer.Ordinal);

    public DatasetRegistry(Func<RegistryDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <summary>
    /// Cached for 30s: metadata construction queries this on every request, and
    /// the parallelized almanac can issue thousands of lookups per call. Writes
    /// (stage/activate/rollback) invalidate the entry immediately.
    /// </summary>
    public DatasetRef? ActiveVersion(string datasetName)
    {
        if (_activeCache.TryGetValue(datasetName, out var cached) && DateTimeOffset.UtcNow < cached.ExpiresAt)
            return cached.Value;
        DatasetRef? value;
        using (var ctx = _contextFactory())
        {
            var active = ctx.ActiveDatasets.AsNoTracking().FirstOrDefault(a => a.Name == datasetName);
            value = active is null ? null : new DatasetRef(active.Name, active.Version);
        }
        _activeCache[datasetName] = (DateTimeOffset.UtcNow.Add(ActiveVersionCacheTtl), value);
        return value;
    }

    public async Task<int> StageAsync(string datasetName, string version, string checksum, CancellationToken ct = default)
    {
        using var ctx = _contextFactory();
        var existing = await ctx.Datasets.FirstOrDefaultAsync(d => d.Name == datasetName && d.Version == version, ct);
        if (existing is null)
        {
            ctx.Datasets.Add(new DatasetRecord
            {
                Name = datasetName,
                Version = version,
                Status = "staged",
                Checksum = checksum,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.Status = "staged";
            existing.Checksum = checksum;
        }
        ctx.AuditEntries.Add(new AuditEntry
        {
            Action = "stage",
            Detail = $"{datasetName} {version}",
            AtUtc = DateTimeOffset.UtcNow,
        });
        var result = await ctx.SaveChangesAsync(ct);
        Invalidate(datasetName);
        return result;
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
        var result = await ctx.SaveChangesAsync(ct);
        Invalidate(datasetName);
        return result;
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
        var result = await ctx.SaveChangesAsync(ct);
        Invalidate(datasetName);
        return result;
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

    private void Invalidate(string datasetName) => _activeCache.TryRemove(datasetName, out _);
}
