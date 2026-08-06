using Astronomy.Modules.Satellites.Application;
using Astronomy.SharedKernel.Coordinates;
using Astronomy.SharedKernel.Datasets;
using Astronomy.SharedKernel.Time;

namespace Astronomy.UnitTests;

public class SatelliteTests
{
    private const string IssLine1 = "1 25544U 98067A   08264.51782528 -.00002182  00000-0 -11606-4 0  2927";
    private const string IssLine2 = "2 25544  51.6416 247.4627 0006703 130.5360 325.0288 15.72125391563537";

    private static OrbitalElementRow Iss() => new(
        "ISS (ZARYA)", "25544",
        new DateTimeOffset(2026, 8, 3, 19, 6, 47, 841, TimeSpan.Zero).AddMilliseconds(984),
        15.49332738, 0.00072249, 51.6316, 64.4821, 9.2337, 350.8783,
        0.00014146099, 0.00007444, 0, 57913);

    [Fact]
    public void TleLine1_MatchesKnownIssLine()
    {
        // Compare structure and the computed checksums against the reference TLE:
        // the epoch/mean-motion fields differ (different element set), but the
        // norad, classification, and checksum conventions must match.
        var line1 = OneSgp4Propagator.BuildLine1(Iss());
        Assert.Equal('1', line1[0]);
        Assert.Equal("25544", line1[2..7]);
        Assert.Equal('U', line1[7]);
        Assert.Equal(line1[68], ChecksumOf(line1));
        Assert.Equal(69, line1.Length);
    }

    [Fact]
    public void TleLine2_ChecksumValid()
    {
        var line2 = OneSgp4Propagator.BuildLine2(Iss());
        Assert.Equal('2', line2[0]);
        Assert.Equal("25544", line2[2..7]);
        Assert.Equal(line2[68], ChecksumOf(line2));
        var incl = double.Parse(line2[8..16], System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(51.6316, incl, 1e-4);
        var mm = double.Parse(line2[52..63], System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(15.49332738, mm, 1e-6);
    }

    [Fact]
    public void Propagate_Iss_PlausibleLowEarthOrbit()
    {
        var prop = new OneSgp4Propagator();
        var t = Iss().EpochUtc.AddHours(1);
        var v = prop.Propagate(Iss(), t);
        var r = Math.Sqrt(v.XKm * v.XKm + v.YKm * v.YKm + v.ZKm * v.ZKm);
        Assert.InRange(r, 6600, 7000); // ~400-500 km altitude above the WGS-72 radius
    }

    [Fact]
    public void Gmst_J2000_IsAbout280Deg()
    {
        var jd = 2451545.0; // J2000.0 TT (UT1 ~ same)
        var gmst = SatelliteFrames.GmstDegrees(jd);
        Assert.InRange(gmst, 279.0, 282.0); // GMST at J2000.0 = 280.46 deg
    }

    [Fact]
    public void Geodetic_RoundTrips()
    {
        var (x, y, z) = SatelliteFrames.GeodeticToEcef(59.9, 10.7, 0.1);
        var (lat, lon, alt) = SatelliteFrames.GeodeticFromEcef(x, y, z);
        Assert.Equal(59.9, lat, 1e-6);
        Assert.Equal(10.7, lon, 1e-6);
        Assert.Equal(0.1, alt, 1e-4);
    }

    [Fact]
    public void Topocentric_AtZenith_Gives90DegAltitude()
    {
        var (ox, oy, oz) = SatelliteFrames.GeodeticToEcef(45.0, 10.0, 0);
        // Satellite directly above the observer at 400 km
        var (sx, sy, sz) = SatelliteFrames.GeodeticToEcef(45.0, 10.0, 400.0);
        var (alt, _, range) = SatelliteFrames.Topocentric(sx, sy, sz, ox, oy, oz, 45.0, 10.0, false);
        Assert.Equal(90.0, alt, 1e-6);
        Assert.Equal(400.0, range, 1e-3);
    }

    [Fact]
    public async Task GetPosition_Iss_ReturnsPlausibleAltAz()
    {
        var service = ServiceWithElements();
        var result = await service.GetPositionAsync("25544", Iss().EpochUtc.AddHours(1),
            ObserverLocation.FromDegrees(59.9, 10.7, 0), false, CancellationToken.None);
        Assert.Equal("25544", result.NoradId);
        Assert.InRange(result.AltitudeDeg, -90, 90);
        Assert.InRange(result.AzimuthDeg, 0, 360);
        Assert.InRange(result.RangeKm, 300, 13000); // visible ~300-3000; far side of Earth up to ~13000
        Assert.InRange(result.SubpointLatDeg, -90, 90);
        Assert.Contains(result.Metadata.Algorithms, a => a.Name == "sgp4");
    }

    [Fact]
    public async Task GetPosition_UnknownNorad_Throws()
    {
        var service = ServiceWithElements();
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetPositionAsync("99999",
            DateTimeOffset.UtcNow, ObserverLocation.FromDegrees(59.9, 10.7, 0), false, CancellationToken.None));
    }

    [Fact]
    public async Task GetPasses_IssOslo_RiseSetAtMinElevation()
    {
        var service = ServiceWithElements();
        var from = Iss().EpochUtc;
        var passes = await service.GetPassesAsync("25544", from, from.AddHours(24),
            ObserverLocation.FromDegrees(59.9, 10.7, 0), 10.0, 30.0, CancellationToken.None);
        Assert.True(passes.Passes.Count >= 2, $"expected ISS passes over Oslo, got {passes.Passes.Count}");
        foreach (var pass in passes.Passes)
        {
            Assert.True(pass.RiseUtc < pass.MaxElevationUtc);
            // A pass in progress at window end may have max elevation exactly at the
            // clamped set time.
            Assert.True(pass.MaxElevationUtc <= pass.SetUtc);
            Assert.True(pass.MaxElevationDeg >= 10.0);
        }
    }

    [Fact]
    public void Predict_PassStraddlingWindowEnd_IsIncludedWithClampedSet()
    {
        var propagator = new OneSgp4Propagator();
        var elements = Iss();
        var observer = ObserverLocation.FromDegrees(59.9, 10.7, 0);
        var full = SatellitePassPredictor.Predict(propagator, elements,
            elements.EpochUtc, elements.EpochUtc.AddHours(24), observer, 0.0, 10.0, 30.0);
        Assert.NotEmpty(full);

        // Window that ends 1 minute before the pass's real set time: the pass is
        // still in progress at the window end and must be reported with a clamped set.
        var from = full[0].RiseUtc.AddMinutes(-1);
        var to = full[0].SetUtc.AddMinutes(-1);
        var truncated = SatellitePassPredictor.Predict(propagator, elements,
            from, to, observer, 0.0, 10.0, 30.0);
        Assert.Single(truncated);
        Assert.Equal(to, truncated[0].SetUtc);
        Assert.True(Math.Abs((truncated[0].RiseUtc - full[0].RiseUtc).TotalSeconds) < 2);
        Assert.True(truncated[0].RiseUtc < truncated[0].MaxElevationUtc);
        Assert.True(truncated[0].MaxElevationUtc <= truncated[0].SetUtc);
    }

    [Fact]
    public async Task GetPasses_WindowTooLong_Throws()
    {
        var service = ServiceWithElements();
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetPassesAsync("25544",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(8),
            ObserverLocation.FromDegrees(59.9, 10.7, 0), 10.0, 30.0, CancellationToken.None));
    }

    [Fact]
    public async Task GetPasses_MinElevationOutOfRange_Throws()
    {
        var service = ServiceWithElements();
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetPassesAsync("25544",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            ObserverLocation.FromDegrees(59.9, 10.7, 0), 200.0, 30.0, CancellationToken.None));
    }

    [Fact]
    public async Task GetPasses_StepSecondsOutOfRange_Throws()
    {
        var service = ServiceWithElements();
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetPassesAsync("25544",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1),
            ObserverLocation.FromDegrees(59.9, 10.7, 0), 10.0, 500.0, CancellationToken.None));
    }

    [Fact]
    public async Task GetStatus_ReturnsActiveVersionAndCount()
    {
        var service = ServiceWithElements();
        var status = await service.GetStatusAsync(CancellationToken.None);
        Assert.Equal("test", status.ActiveVersion);
        Assert.Equal(22, status.ElementCount); // iss-stations-omm.csv fixture
    }

    [Fact]
    public async Task GetPosition_StaleTle_WarnsAst7004()
    {
        var db = Path.Combine(Path.GetTempPath(), $"sat-stale-{Guid.NewGuid():N}.db");
        try
        {
            SatelliteStore.EnsureSchema(db);
            var stale = Iss() with { EpochUtc = DateTimeOffset.UtcNow.AddDays(-5) };
            SatelliteStore.WriteElements(db, "test", [stale]);
            var service = new SatelliteService(db, new StubSatelliteRegistry(),
                new TimeScaleConverter(LeapSecondTable.Default, []));
            var result = await service.GetPositionAsync("25544", DateTimeOffset.UtcNow,
                ObserverLocation.FromDegrees(59.9, 10.7, 0), false, CancellationToken.None);
            Assert.Contains(result.Metadata.Warnings, w => w.Code == "AST-7004");
        }
        finally
        {
            foreach (var f in new[] { db, db + "-wal", db + "-shm" })
                try { File.Delete(f); } catch { }
        }
    }

    [Fact]
    public async Task Search_EmptyQuery_ReturnsAll()
    {
        var service = ServiceWithElements();
        var results = await service.SearchAsync("", CancellationToken.None);
        Assert.NotEmpty(results);
    }

    [Fact]
    public async Task GetPosition_WithoutEopData_StillWorks()
    {
        // No eop-ut1 dataset: UT1 falls back to UTC (~0.366 s effect is sub-arcsecond).
        var db = Path.Combine(Path.GetTempPath(), $"sat-noeop-{Guid.NewGuid():N}.db");
        try
        {
            SatelliteStore.EnsureSchema(db);
            SatelliteStore.WriteElements(db, "test", [Iss()]);
            var service = new SatelliteService(db, new StubSatelliteRegistry(),
                new TimeScaleConverter(LeapSecondTable.Default, []));
            var result = await service.GetPositionAsync("25544", DateTimeOffset.UtcNow,
                ObserverLocation.FromDegrees(59.9, 10.7, 0), false, CancellationToken.None);
            Assert.InRange(result.AltitudeDeg, -90, 90);
        }
        finally
        {
            foreach (var f in new[] { db, db + "-wal", db + "-shm" })
                try { File.Delete(f); } catch { }
        }
    }

    [Fact]
    public void Predict_EmptyWindow_ReturnsEmpty()
    {
        var propagator = new OneSgp4Propagator();
        var t = Iss().EpochUtc;
        var passes = SatellitePassPredictor.Predict(propagator, Iss(), t, t,
            ObserverLocation.FromDegrees(59.9, 10.7, 0), 0.0, 10.0, 30.0);
        Assert.Empty(passes);
    }

    [Fact]
    public void Predict_ConstantBelowHorizon_NoPasses()
    {
        // A propagator that never leaves the ground: no crossings, no passes.
        var passes = SatellitePassPredictor.Predict(
            _ => new TemeVector(0, 0, 0),
            Iss().EpochUtc, Iss().EpochUtc.AddHours(24),
            ObserverLocation.FromDegrees(59.9, 10.7, 0), 0.0, 10.0, 30.0);
        Assert.Empty(passes);
    }

    [Fact]
    public void Predict_WindowStartingMidPass_IsIncludedWithClampedRise()
    {
        // Regression for the start-of-window clamp (mirror of the end clamp):
        // a pass already in progress when the window starts is reported with
        // riseUtc = from.
        var propagator = new OneSgp4Propagator();
        var elements = Iss();
        var observer = ObserverLocation.FromDegrees(59.9, 10.7, 0);
        var full = SatellitePassPredictor.Predict(propagator, elements,
            elements.EpochUtc, elements.EpochUtc.AddHours(24), observer, 0.0, 10.0, 30.0);
        Assert.NotEmpty(full);

        var from = full[0].RiseUtc.AddMinutes(2);
        var to = full[0].SetUtc.AddMinutes(2);
        var mid = SatellitePassPredictor.Predict(propagator, elements,
            from, to, observer, 0.0, 10.0, 30.0);
        Assert.Single(mid);
        Assert.Equal(from, mid[0].RiseUtc);
        Assert.Equal(full[0].SetUtc, mid[0].SetUtc);
        Assert.True(mid[0].RiseUtc <= mid[0].MaxElevationUtc);
        Assert.True(mid[0].MaxElevationUtc <= mid[0].SetUtc);
    }

    [Fact]
    public async Task Search_FindsIssByName()
    {
        var service = ServiceWithElements();
        var results = await service.SearchAsync("iss", CancellationToken.None);
        Assert.Contains(results, r => r.NoradId == "25544");
    }

    private static SatelliteService ServiceWithElements()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "iss-stations-omm.csv");
        var rows = ParseOmm(fixture);
        var db = Path.Combine(Path.GetTempPath(), $"sat-{Guid.NewGuid():N}.db");
        SatelliteStore.EnsureSchema(db);
        SatelliteStore.WriteElements(db, "test", rows);
        return new SatelliteService(db, new StubSatelliteRegistry(), new TimeScaleConverter(LeapSecondTable.Default, []));
    }

    private static List<OrbitalElementRow> ParseOmm(string path)
    {
        var rows = new List<OrbitalElementRow>();
        foreach (var line in File.ReadAllLines(path).Skip(1))
        {
            var p = line.Split(',');
            if (p.Length < 17) continue;
            rows.Add(new OrbitalElementRow(
                p[0].Trim(), p[11].Trim(),
                DateTimeOffset.Parse(p[2].Trim(), System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal),
                double.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(p[4], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(p[5], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(p[6], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(p[7], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(p[8], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(p[14], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(p[15], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(p[16], System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(p[13], System.Globalization.CultureInfo.InvariantCulture)));
        }
        return rows;
    }

    private sealed class StubSatelliteRegistry : Astronomy.SharedKernel.Datasets.IDatasetRegistry
    {
        public DatasetRef? ActiveVersion(string datasetName) =>
            datasetName == SatelliteService.DatasetName ? new DatasetRef(datasetName, "test") : null;

        public Task<int> StageAsync(string datasetName, string version, string checksum, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> ActivateAsync(string datasetName, string version, CancellationToken ct = default) => Task.FromResult(0);
        public Task<int> RollbackAsync(string datasetName, string version, CancellationToken ct = default) => Task.FromResult(0);
    }

    private static char ChecksumOf(string line)
    {
        var sum = 0;
        foreach (var ch in line[..68])
        {
            if (ch == ' ') continue;
            if (ch == '-') { sum++; continue; }
            if (char.IsDigit(ch)) sum += ch - '0';
        }
        return (char)('0' + sum % 10);
    }
}
