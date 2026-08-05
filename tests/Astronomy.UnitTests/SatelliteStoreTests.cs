using Astronomy.Modules.Satellites.Application;

namespace Astronomy.UnitTests;

public class SatelliteStoreTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"sat-store-{Guid.NewGuid():N}.db");

    public SatelliteStoreTests()
    {
        SatelliteStore.EnsureSchema(_db);
    }

    public void Dispose()
    {
        foreach (var f in new[] { _db, _db + "-wal", _db + "-shm" })
            try { File.Delete(f); } catch { }
    }

    private static OrbitalElementRow Row(string name, string norad) => new(
        name, norad,
        new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero),
        15.5, 0.0007, 51.6, 64.4, 9.2, 350.8,
        0.0001, 0.00007, 0, 57913);

    [Fact]
    public void ElementsJson_RoundTrips_SpecialCharactersInName()
    {
        var row = Row("ISS \"ZARYA\" \\ TEST", "25544");
        SatelliteStore.WriteElements(_db, "v1", [row]);
        var read = SatelliteStore.ReadElements(_db, "v1");
        Assert.Single(read);
        Assert.Equal(row.Name, read[0].Name);
        Assert.Equal(row.NoradId, read[0].NoradId);
        Assert.Equal(row.MeanMotion, read[0].MeanMotion, 9);
        Assert.Equal(row.EpochUtc, read[0].EpochUtc);
    }

    [Fact]
    public void WriteElements_SameVersionTwice_UpsertsNoDuplicates()
    {
        SatelliteStore.WriteElements(_db, "v1", [Row("ISS (ZARYA)", "25544"), Row("ISS (NAUKA)", "25413")]);
        SatelliteStore.WriteElements(_db, "v1", [Row("ISS (ZARYA)", "25544")]);
        var read = SatelliteStore.ReadElements(_db, "v1");
        Assert.Single(read);
        Assert.Equal("25544", read[0].NoradId);
    }

    [Fact]
    public void WriteElements_DifferentVersions_Coexist()
    {
        SatelliteStore.WriteElements(_db, "v1", [Row("ISS (ZARYA)", "25544")]);
        SatelliteStore.WriteElements(_db, "v2", [Row("ISS (NAUKA)", "25413")]);
        Assert.Single(SatelliteStore.ReadElements(_db, "v1"));
        Assert.Single(SatelliteStore.ReadElements(_db, "v2"));
    }

    [Fact]
    public void LegacyJson_StillParses()
    {
        // Rows staged before the STJ format used short keys and unescaped names.
        const string legacy = "{\"name\":\"ISS (ZARYA)\",\"norad\":\"25544\",\"epoch\":\"2026-08-03T12:00:00.0000000+00:00\",\"mm\":15.5,\"ecc\":0.0007,\"incl\":51.6,\"raan\":64.4,\"argp\":9.2,\"ma\":350.8,\"bstar\":0.0001,\"mmdot\":0.00007,\"mmddot\":0,\"rev\":57913}";
        using (var ctx = SatelliteStore.CreateContext(_db))
        {
            ctx.Elements.Add(new SatelliteElementRecord
            {
                DatasetVersion = "legacy",
                NoradId = "25544",
                ObjectName = "ISS (ZARYA)",
                EpochUtc = "2026-08-03T12:00:00.0000000+00:00",
                ElementsJson = legacy,
            });
            ctx.SaveChanges();
        }
        var read = SatelliteStore.ReadElements(_db, "legacy");
        Assert.Single(read);
        Assert.Equal("ISS (ZARYA)", read[0].Name);
        Assert.Equal(15.5, read[0].MeanMotion, 9);
        Assert.Equal(0.0007, read[0].Eccentricity, 9);
    }
}
