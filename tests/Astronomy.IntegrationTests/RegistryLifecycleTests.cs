using Astronomy.Infrastructure;
using Astronomy.Infrastructure.Catalog;
using Astronomy.Infrastructure.Registry;
using Astronomy.Infrastructure.Time;
using Astronomy.SharedKernel.Datasets;
using Astronomy.SharedKernel.Persistence;
using Astronomy.SharedKernel.Time;

namespace Astronomy.IntegrationTests;

public class RegistryLifecycleTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"astro-reg-{Guid.NewGuid():N}.db");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"astro-data-{Guid.NewGuid():N}");

    private static AstronomyDbConfig Config(string path) => AstronomyDbConfig.FromValues("sqlite", null, path);

    public RegistryLifecycleTests()
    {
        Directory.CreateDirectory(_root);
        InfrastructureRegistrar.EnsureSchema(Config(_db));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
        foreach (var f in new[] { _db, _db + "-wal", _db + "-shm" })
            try { File.Delete(f); } catch { }
    }

    private DatasetRegistry Registry() => new(() => InfrastructureRegistrar.CreateRegistryContext(Config(_db)));

    [Fact]
    public async Task Stage_Activate_Rollback_Lifecycle()
    {
        var registry = Registry();
        Assert.Null(registry.ActiveVersion("eop-ut1"));

        await registry.StageAsync("eop-ut1", "v1", "abc");
        await registry.ActivateAsync("eop-ut1", "v1");
        Assert.Equal("v1", registry.ActiveVersion("eop-ut1")!.Version);

        await registry.StageAsync("eop-ut1", "v2", "def");
        await registry.ActivateAsync("eop-ut1", "v2");
        Assert.Equal("v2", registry.ActiveVersion("eop-ut1")!.Version);

        await registry.RollbackAsync("eop-ut1", "v1");
        Assert.Equal("v1", registry.ActiveVersion("eop-ut1")!.Version);
    }

    [Fact]
    public async Task Activate_UnknownVersion_Throws()
    {
        var registry = Registry();
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.ActivateAsync("eop-ut1", "nope"));
    }

    [Fact]
    public async Task TimeScaleConverter_UsesActivatedDatasets()
    {
        var leapDir = Path.Combine(_root, "datasets", "leap-seconds", "iers-2026a");
        Directory.CreateDirectory(leapDir);
        var lines = new List<string> { "effective_utc,tai_minus_utc" };
        lines.AddRange(LeapSecondTable.Default.Entries.Select(e => $"{e.EffectiveUtc:yyyy-MM-ddTHH:mm:ssZ},{e.TaiMinusUtc}"));
        await File.WriteAllLinesAsync(Path.Combine(leapDir, "leap-seconds.csv"), lines);
        var eopDir = Path.Combine(_root, "datasets", "eop-ut1", "20260804");
        Directory.CreateDirectory(eopDir);
        await File.WriteAllLinesAsync(Path.Combine(eopDir, "eop-ut1.csv"), new[] { "mjd,ut1_minus_utc_seconds", "61035.0,0.3600000" });

        var registry = Registry();
        await registry.StageAsync("leap-seconds", "iers-2026a", "x");
        await registry.ActivateAsync("leap-seconds", "iers-2026a");
        await registry.StageAsync("eop-ut1", "20260804", "y");
        await registry.ActivateAsync("eop-ut1", "20260804");

        var catalog = new DatasetCatalog(registry, _root);
        var converter = TimeDatasetLoaders.CreateTimeScaleConverter(catalog, _root);
        var r = converter.Convert(new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(69.184, r.TtMinusUtcSeconds, 3);
        Assert.Equal("20260804", r.EopDatasetVersion);
        Assert.Equal(0.36, r.Ut1MinusUtcSeconds, 6);
    }

    [Fact]
    public async Task Backup_Restore_RoundTrip()
    {
        var registry = Registry();
        await registry.StageAsync("eop-ut1", "v1", "abc");
        await registry.ActivateAsync("eop-ut1", "v1");

        var backup = _db + ".backup";
        InfrastructureRegistrar.BackupDatabase(Config(_db), backup);
        Assert.True(File.Exists(backup));

        File.WriteAllText(_db, "corrupted garbage");
        File.Copy(backup, _db, overwrite: true);
        var after = Registry();
        Assert.Equal("v1", after.ActiveVersion("eop-ut1")!.Version);
    }
}
