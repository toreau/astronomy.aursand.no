using Astronomy.Modules.Satellites.Application;
using Astronomy.SharedKernel.Datasets;
using Astronomy.SharedKernel.Persistence;

namespace Astronomy.UnitTests;

public class OmmIngestionTests
{
    private const string Header = "OBJECT_NAME,OBJECT_ID,EPOCH,MEAN_MOTION,ECCENTRICITY,INCLINATION,RA_OF_ASC_NODE,ARG_OF_PERICENTER,MEAN_ANOMALY,EPHEMERIS_TYPE,CLASSIFICATION_TYPE,NORAD_CAT_ID,ELEMENT_SET_NO,REV_AT_EPOCH,BSTAR,MEAN_MOTION_DOT,MEAN_MOTION_DDOT";

    private static AstronomyDbConfig Config(string path) => AstronomyDbConfig.FromValues("sqlite", null, path);

    private static string Line(DateTimeOffset epoch, string norad = "25544", string mm = "15.49332738") =>
        $"ISS (ZARYA),1998-067A,{epoch:yyyy-MM-ddTHH:mm:ss.ffffff}, {mm},0.00072249,51.6316,64.4821,9.2337,350.8783,0,U,{norad},999,57913,0.00014146099,0.00007444,0";

    private static readonly DateTimeOffset Now = new(2026, 8, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ParseCsv_ValidPayload_ReturnsRows()
    {
        var csv = string.Join('\n', Header, Line(Now.AddHours(-10)), Line(Now.AddHours(-9), "25413", "15.1"));
        var rows = SatelliteElementIngestionService.ParseCsv(csv);
        Assert.Equal(2, rows.Count);
        Assert.Equal("25544", rows[0].NoradId);
        Assert.Equal("ISS (ZARYA)", rows[0].Name);
        Assert.Equal(15.49332738, rows[0].MeanMotion, 8);
        Assert.Equal(0.00072249, rows[0].Eccentricity, 8);
        Assert.Equal(57913, rows[0].RevAtEpoch);
    }

    [Fact]
    public void ParseCsv_HeaderOnly_Throws()
    {
        Assert.Throws<FormatException>(() => SatelliteElementIngestionService.ParseCsv(Header));
    }

    [Fact]
    public void Validate_AcceptsFreshWellFormedRows()
    {
        var rows = new List<OrbitalElementRow> { Row(Now.AddHours(-10)) };
        Assert.Empty(SatelliteElementIngestionService.Validate(rows, Now));
    }

    [Fact]
    public void Validate_RejectsBadNorad()
    {
        var rows = new List<OrbitalElementRow> { Row(Now.AddHours(-10), norad: "abc") };
        var errors = SatelliteElementIngestionService.Validate(rows, Now);
        Assert.Contains(errors, e => e.Field == "norad");
    }

    [Fact]
    public void Validate_RejectsStaleEpoch()
    {
        var rows = new List<OrbitalElementRow> { Row(Now.AddHours(-100)) };
        var errors = SatelliteElementIngestionService.Validate(rows, Now);
        Assert.Contains(errors, e => e.Field == "epoch");
    }

    [Fact]
    public void Validate_RejectsImplausibleMeanMotion()
    {
        var rows = new List<OrbitalElementRow> { Row(Now.AddHours(-10), mm: "999.0") };
        var errors = SatelliteElementIngestionService.Validate(rows, Now);
        Assert.Contains(errors, e => e.Field == "mean_motion");
    }

    [Fact]
    public void Validate_RejectsAngleOutOfRange()
    {
        var rows = new List<OrbitalElementRow> { Row(Now.AddHours(-10), raan: 361.0) };
        var errors = SatelliteElementIngestionService.Validate(rows, Now);
        Assert.Contains(errors, e => e.Field == "angles");
    }

    [Fact]
    public async Task StageFileAsync_TooFewRows_Rejected()
    {
        var db = TempDb();
        SatelliteStore.EnsureSchema(Config(db));
        try
        {
            var csv = Path.GetTempFileName();
            try
            {
                var lines = new List<string> { Header };
                for (var i = 0; i < 3; i++)
                    lines.Add(Line(DateTimeOffset.UtcNow.AddHours(-1), norad: $"2554{i}"));
                await File.WriteAllLinesAsync(csv, lines);

                var registry = new RecordingRegistry();
                var service = new SatelliteElementIngestionService(Config(db), registry);
                var exit = await service.StageFileAsync("20260805", csv);

                Assert.Equal(1, exit);
                Assert.Empty(registry.Staged);
            }
            finally { File.Delete(csv); }
        }
        finally { CleanupDb(db); }
    }

    [Fact]
    public async Task StageFileAsync_ValidRows_StagesAndActivates()
    {
        var db = TempDb();
        SatelliteStore.EnsureSchema(Config(db));
        try
        {
            var csv = Path.GetTempFileName();
            try
            {
                var lines = new List<string> { Header };
                for (var i = 0; i < 12; i++)
                    lines.Add(Line(DateTimeOffset.UtcNow.AddHours(-2 + i * 0.1), norad: $"2554{i:D2}"));
                await File.WriteAllLinesAsync(csv, lines);

                var registry = new RecordingRegistry();
                var service = new SatelliteElementIngestionService(Config(db), registry);
                var exit = await service.StageFileAsync("20260805", csv);

                Assert.Equal(0, exit);
                Assert.Contains("satellite-elements:20260805", registry.Staged);

                await service.ActivateAsync("20260805");
                var status = await service.GetStatusAsync();
                Assert.Equal("20260805", status.ActiveVersion);
                Assert.Equal(12, status.ElementCount);
                Assert.Equal(12, SatelliteStore.ReadElements(Config(db), "20260805").Count);
            }
            finally { File.Delete(csv); }
        }
        finally { CleanupDb(db); }
    }

    private static string TempDb() => Path.Combine(Path.GetTempPath(), $"omm-{Guid.NewGuid():N}.db");

    private static void CleanupDb(string db)
    {
        foreach (var f in new[] { db, db + "-wal", db + "-shm" })
            try { File.Delete(f); } catch { }
    }

    private sealed class RecordingRegistry : IDatasetRegistry
    {
        public string? ActiveVersionValue { get; private set; }
        public List<string> Staged { get; } = [];

        public DatasetRef? ActiveVersion(string datasetName) =>
            ActiveVersionValue is null ? null : new DatasetRef(datasetName, ActiveVersionValue);

        public Task<int> StageAsync(string datasetName, string version, string checksum, CancellationToken ct = default)
        {
            Staged.Add($"{datasetName}:{version}");
            return Task.FromResult(0);
        }

        public Task<int> ActivateAsync(string datasetName, string version, CancellationToken ct = default)
        {
            ActiveVersionValue = version;
            return Task.FromResult(0);
        }

        public Task<int> RollbackAsync(string datasetName, string version, CancellationToken ct = default) =>
            Task.FromResult(0);
    }

    private static OrbitalElementRow Row(DateTimeOffset epoch, string norad = "25544", string mm = "15.5", double raan = 64.4) => new(
        "ISS (ZARYA)", norad, epoch,
        double.Parse(mm, System.Globalization.CultureInfo.InvariantCulture),
        0.0007, 51.6, raan, 9.2, 350.8,
        0.0001, 0.00007, 0, 57913);
}
