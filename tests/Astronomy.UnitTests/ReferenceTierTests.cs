using Astronomy.Modules.Ephemeris.Application;
using Astronomy.Modules.Ephemeris.Reference;
using Astronomy.SharedKernel.Coordinates;

namespace Astronomy.UnitTests;

public class ReferenceTierTests
{
    private sealed class FakeReferenceEphemeris(bool available = true) : IReferenceEphemeris
    {
        public bool IsAvailable { get; } = available;
        public string UnavailableReason { get; } = available ? "" : "test kernel pool unavailable";
        public IReadOnlyDictionary<string, string> KernelVersions { get; } =
            new Dictionary<string, string> { ["de440s.bsp"] = "testsha8" };
        public int CallCount { get; private set; }
        public bool? LastApparent { get; private set; }
        public ReferencePosition Position(BodyId body, DateTimeOffset utc, bool apparent)
        {
            CallCount++;
            LastApparent = apparent;
            return new ReferencePosition(123.456, -45.678, 149_000_000.0, apparent ? "LT+S" : "LT");
        }
    }

    private static PositionRequest Request(string frame = "icrs", string positionType = "astrometric", PrecisionMode precision = PrecisionMode.Reference) =>
        new("jupiter", new DateTimeOffset(2026, 8, 4, 12, 0, 0, TimeSpan.Zero),
            ObserverLocation.FromDegrees(59.9, 10.7, 0), Frame(frame), PositionTypeFrom(positionType),
            RefractionModel.None, precision);

    private static CoordinateFrame Frame(string f) => f switch
    {
        "icrs" => CoordinateFrame.IcrJ2000,
        "of-date" => CoordinateFrame.EquatorialOfDate,
        _ => CoordinateFrame.Horizontal,
    };

    private static PositionType PositionTypeFrom(string p) => p switch
    {
        "apparent" => PositionType.Apparent,
        _ => PositionType.Astrometric,
    };

    [Fact]
    public async Task ReferencePrecision_UsesSpicePositions_NoWarning()
    {
        var fake = new FakeReferenceEphemeris();
        var service = new EphemerisService(new StubCatalog(), fake);
        var result = await service.GetPositionAsync(Request(), CancellationToken.None);

        Assert.Equal(123.456, result.RightAscensionDeg);
        Assert.Equal(-45.678, result.DeclinationDeg);
        Assert.Equal(149_000_000.0, result.DistanceKm);
        Assert.Contains(result.Metadata.Algorithms, a => a.Name == "spice-de440s" && a.Version.Contains("j2000-astrometric"));
        Assert.DoesNotContain(result.Metadata.Warnings, w => w.Code == "AST-7002");
        Assert.Contains(result.Metadata.Datasets, d => d.Name == "spice:de440s.bsp" && d.Version == "testsha8");
        Assert.Equal(1, fake.CallCount);
    }

    [Fact]
    public async Task AdvancedPrecision_UsesSpicePositions()
    {
        var fake = new FakeReferenceEphemeris();
        var service = new EphemerisService(new StubCatalog(), fake);
        var result = await service.GetPositionAsync(Request(precision: PrecisionMode.Advanced), CancellationToken.None);

        Assert.Equal(123.456, result.RightAscensionDeg);
        Assert.DoesNotContain(result.Metadata.Warnings, w => w.Code == "AST-7002");
    }

    [Fact]
    public async Task ReferencePrecision_Apparent_RequestsLightTimePlusAberration()
    {
        var fake = new FakeReferenceEphemeris();
        var service = new EphemerisService(new StubCatalog(), fake);
        await service.GetPositionAsync(Request(positionType: "apparent"), CancellationToken.None);

        Assert.True(fake.LastApparent);
    }

    [Fact]
    public async Task ReferencePrecision_OfDate_Throws()
    {
        var service = new EphemerisService(new StubCatalog(), new FakeReferenceEphemeris());
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetPositionAsync(Request(frame: "of-date", positionType: "apparent"), CancellationToken.None));
        Assert.Contains("J2000", ex.Message);
    }

    [Fact]
    public async Task ReferencePrecision_Unavailable_Throws503Exception()
    {
        var service = new EphemerisService(new StubCatalog(), new FakeReferenceEphemeris(available: false));
        var ex = await Assert.ThrowsAsync<ReferenceEphemerisUnavailableException>(() =>
            service.GetPositionAsync(Request(), CancellationToken.None));
        Assert.Contains("test kernel pool unavailable", ex.Message);
    }

    [Fact]
    public async Task ReferencePrecision_NoReferenceRegistered_Throws503Exception()
    {
        var service = new EphemerisService(new StubCatalog());
        await Assert.ThrowsAsync<ReferenceEphemerisUnavailableException>(() =>
            service.GetPositionAsync(Request(), CancellationToken.None));
    }

    [Fact]
    public async Task ReferencePrecision_Horizontal_UsesEngineChain_WithWarning()
    {
        var fake = new FakeReferenceEphemeris();
        var service = new EphemerisService(new StubCatalog(), fake);
        var result = await service.GetPositionAsync(Request(frame: "horizontal"), CancellationToken.None);

        Assert.Null(result.Metadata.Algorithms.FirstOrDefault(a => a.Name == "spice-de440s"));
        Assert.Equal(0, fake.CallCount);
        Assert.Contains(result.Metadata.Warnings, w => w.Code == "AST-7003");
    }

    [Fact]
    public async Task ConsumerPrecision_DoesNotTouchReference()
    {
        var fake = new FakeReferenceEphemeris();
        var service = new EphemerisService(new StubCatalog(), fake);
        var result = await service.GetPositionAsync(Request(precision: PrecisionMode.Consumer), CancellationToken.None);

        Assert.Equal(0, fake.CallCount);
        Assert.Contains(result.Metadata.Algorithms, a => a.Name == "astronomy-engine");
        Assert.DoesNotContain(result.Metadata.Warnings, w => w.Code == "AST-7002");
    }

    [Fact]
    public async Task AdvancedPrecision_OnRiseSet_StillWarns()
    {
        var service = new EphemerisService(new StubCatalog(), new FakeReferenceEphemeris());
        var result = await service.GetRiseSetAsync(BodyId.Sun, new DateOnly(2026, 8, 4),
            ObserverLocation.FromDegrees(59.9, 10.7, 0), PrecisionMode.Advanced, CancellationToken.None);

        Assert.Contains(result.Metadata.Warnings, w => w.Code == "AST-7002");
    }
}
