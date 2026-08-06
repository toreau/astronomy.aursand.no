using Astronomy.Infrastructure;
using Astronomy.Infrastructure.Catalog;
using Astronomy.Infrastructure.Registry;
using Astronomy.SharedKernel.Stars;

namespace Astronomy.UnitTests;

public class StarCatalogLoaderTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"star-load-{Guid.NewGuid():N}.db");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"star-root-{Guid.NewGuid():N}");

    public StarCatalogLoaderTests()
    {
        InfrastructureRegistrar.MigrateRegistry(_db);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
        foreach (var f in new[] { _db, _db + "-wal", _db + "-shm" })
            try { File.Delete(f); } catch { }
    }

    private async Task<StarCatalog> LoadAsync()
    {
        var registry = new DatasetRegistry(() => InfrastructureRegistrar.CreateRegistryContext(_db));
        var catalog = new DatasetCatalog(registry, _root);
        return Astronomy.Infrastructure.Stars.StarCatalogLoader.LoadStarCatalog(catalog, _root);
    }

    [Fact]
    public async Task Load_NoActiveDataset_ReturnsUnavailable()
    {
        var catalog = await LoadAsync();
        Assert.False(catalog.IsAvailable);
    }

    [Fact]
    public async Task Load_FromCsv_SkipsMalformedRows()
    {
        var dir = Path.Combine(_root, "datasets", "star-catalog-hyg", "v38");
        Directory.CreateDirectory(dir);
        await File.WriteAllLinesAsync(Path.Combine(dir, "star-catalog-hyg.csv"), new[]
        {
            "hip,proper,bayer_flamsteed,bayer,flam,con,ra_deg,dec_deg,pmra_mas_yr,pmdec_mas_yr,dist_ly,vmag,spect",
            "32349,Sirius,9Alp CMa,Alp,9,CMa,101.287155,-16.716117,-546.01,-1223.08,8.6,-1.44,A0m...",
            "this,is,not,a,valid,row,,,,,,,",
            "91262,Vega,3Alp Lyr,Alp,3,Lyr,279.234735,38.783689,200.94,286.23,25.0,0.03,A0Va",
        });
        var registry = new DatasetRegistry(() => InfrastructureRegistrar.CreateRegistryContext(_db));
        await registry.StageAsync("star-catalog-hyg", "v38", "x");
        await registry.ActivateAsync("star-catalog-hyg", "v38");

        var catalog = await LoadAsync();
        Assert.True(catalog.IsAvailable);
        Assert.Equal(2, catalog.Stars.Count);
        Assert.Equal("v38", catalog.Version);
        Assert.True(catalog.TryGetByHip("91262", out _));
    }
}
