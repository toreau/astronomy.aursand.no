using Astronomy.DataIngestion;
using Astronomy.SharedKernel.Datasets;

namespace Astronomy.UnitTests;

public class JobsPipelineTests
{
    private sealed class RecordingRegistry : IDatasetRegistry
    {
        public string? ActiveVersionValue { get; set; }
        public List<string> Activated { get; } = [];
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
            Activated.Add($"{datasetName}:{version}");
            ActiveVersionValue = version;
            return Task.FromResult(0);
        }

        public Task<int> RollbackAsync(string datasetName, string version, CancellationToken ct = default) =>
            Task.FromResult(0);
    }

    [Fact]
    public async Task GateAndActivate_GatePass_Activates()
    {
        var registry = new RecordingRegistry();
        var exit = await Jobs.GateAndActivateAsync("satellite-elements", "20260806", registry,
            v => Task.FromResult(v == "20260806"));
        Assert.Equal(0, exit);
        Assert.Contains("satellite-elements:20260806", registry.Activated);
    }

    [Fact]
    public async Task GateAndActivate_GateFail_DoesNotActivate()
    {
        var registry = new RecordingRegistry();
        registry.ActiveVersionValue = "20260805"; // previous version stays active
        var exit = await Jobs.GateAndActivateAsync("satellite-elements", "20260806", registry,
            _ => Task.FromResult(false));
        Assert.Equal(1, exit);
        Assert.Empty(registry.Activated);
        Assert.Equal("20260805", registry.ActiveVersionValue);
    }

    [Fact]
    public async Task GateAndActivate_NoGate_Activates()
    {
        var registry = new RecordingRegistry();
        var exit = await Jobs.GateAndActivateAsync("satellite-elements", "20260806", registry, null);
        Assert.Equal(0, exit);
        Assert.Contains("satellite-elements:20260806", registry.Activated);
    }

    [Fact]
    public void Ut1Continuity_SmallChange_Passes()
    {
        var csv = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(csv, new[] { "mjd,ut1_minus_utc_seconds", "61000.0,0.3660000" });
            Assert.False(Jobs.Ut1ContinuityViolated(0.37, csv));
        }
        finally { File.Delete(csv); }
    }

    [Fact]
    public void Ut1Continuity_Jump_Rejected()
    {
        var csv = Path.GetTempFileName();
        try
        {
            File.WriteAllLines(csv, new[] { "mjd,ut1_minus_utc_seconds", "61000.0,0.3660000" });
            Assert.True(Jobs.Ut1ContinuityViolated(1.5, csv));
        }
        finally { File.Delete(csv); }
    }

    [Fact]
    public void Ut1Continuity_NoActiveDataset_Passes()
    {
        Assert.False(Jobs.Ut1ContinuityViolated(0.5, null));
        Assert.False(Jobs.Ut1ContinuityViolated(0.5, "/nonexistent/path.csv"));
    }
}
