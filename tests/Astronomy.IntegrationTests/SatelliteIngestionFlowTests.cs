using Astronomy.Infrastructure;
using Astronomy.Infrastructure.Catalog;
using Astronomy.Infrastructure.Registry;
using Astronomy.Modules.Satellites.Application;
using Astronomy.SharedKernel.Coordinates;
using Astronomy.SharedKernel.Datasets;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.IntegrationTests;

public class SatelliteIngestionFlowTests : IDisposable
{
    private const string Header = "OBJECT_NAME,OBJECT_ID,EPOCH,MEAN_MOTION,ECCENTRICITY,INCLINATION,RA_OF_ASC_NODE,ARG_OF_PERICENTER,MEAN_ANOMALY,EPHEMERIS_TYPE,CLASSIFICATION_TYPE,NORAD_CAT_ID,ELEMENT_SET_NO,REV_AT_EPOCH,BSTAR,MEAN_MOTION_DOT,MEAN_MOTION_DDOT";

    private readonly string _db = Path.Combine(Path.GetTempPath(), $"sat-flow-{Guid.NewGuid():N}.db");
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"sat-root-{Guid.NewGuid():N}");

    public SatelliteIngestionFlowTests()
    {
        Directory.CreateDirectory(_root);
        InfrastructureRegistrar.MigrateRegistry(_db);
        SatelliteStore.EnsureSchema(_db);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
        foreach (var f in new[] { _db, _db + "-wal", _db + "-shm" })
            try { File.Delete(f); } catch { }
    }

    private ServiceProvider BuildServices() => new ServiceCollection()
        .AddAstronomyInfrastructure(_db, _root)
        .AddSingleton(sp => Astronomy.Infrastructure.Time.TimeDatasetLoaders.CreateTimeScaleConverter(
            sp.GetRequiredService<Astronomy.SharedKernel.Datasets.IDatasetCatalog>(), _root))
        .AddSatellitesModule(_db)
        .BuildServiceProvider();

    private static string Row(string norad, string name, DateTimeOffset epoch) =>
        $"{name},1998-067A,{epoch:yyyy-MM-ddTHH:mm:ss.ffffff},15.5,0.0007,51.6,64.4,9.2,350.8,0,U,{norad},999,57913,0.0001,0.00007,0";

    [Fact]
    public async Task StageFile_NotActive_ServiceRejects_ThenActivate_Resolves()
    {
        var csv = Path.Combine(Path.GetTempPath(), $"omm-{Guid.NewGuid():N}.csv");
        try
        {
            var epoch = DateTimeOffset.UtcNow.AddHours(-1);
            var lines = new List<string> { Header };
            for (var i = 0; i < 12; i++)
                lines.Add(Row($"2554{i:D2}", $"SAT {i:D2}", epoch.AddMinutes(i)));
            await File.WriteAllLinesAsync(csv, lines);

            var services = BuildServices();
            var ingestion = services.GetRequiredService<ISatelliteElementIngestionService>();
            var satellites = services.GetRequiredService<ISatelliteService>();
            var observer = ObserverLocation.FromDegrees(59.9, 10.7, 0);

            // Stage only: the service must reject lookups until activation.
            Assert.Equal(0, await ingestion.StageFileAsync("20260806", csv));
            await Assert.ThrowsAsync<SatelliteElementsUnavailableException>(() =>
                satellites.GetPositionAsync("255400", DateTimeOffset.UtcNow, observer, false, CancellationToken.None));

            // Activate: the service now resolves the elements end to end.
            await ingestion.ActivateAsync("20260806");
            var position = await satellites.GetPositionAsync("255400", DateTimeOffset.UtcNow, observer, false, CancellationToken.None);
            Assert.Equal("255400", position.NoradId);
            Assert.InRange(position.AltitudeDeg, -90, 90);

            var status = await satellites.GetStatusAsync(CancellationToken.None);
            Assert.Equal("20260806", status.ActiveVersion);
            Assert.Equal(12, status.ElementCount);
        }
        finally { File.Delete(csv); }
    }

    [Fact]
    public async Task ResolvePath_FollowsDirectoriesNotActivation()
    {
        // ResolvePath is file-backed: a staged (directory-present) version resolves
        // even before activation; a never-staged version does not.
        var dir = Path.Combine(_root, "datasets", "eop-ut1", "v1");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "eop-ut1.csv"), "mjd,ut1_minus_utc_seconds\n60814.0,0.3660000\n");

        var registry = new DatasetRegistry(() => InfrastructureRegistrar.CreateRegistryContext(_db));
        await registry.StageAsync("eop-ut1", "v1", "x");
        var catalog = new DatasetCatalog(registry, _root);

        Assert.NotNull(catalog.ResolvePath("eop-ut1", "v1"));   // staged but not active
        Assert.Null(catalog.ResolvePath("eop-ut1", "v2"));      // never staged
        Assert.Null(registry.ActiveVersion("eop-ut1"));         // activation independent
    }
}
