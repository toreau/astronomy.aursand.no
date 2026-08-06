using Astronomy.Modules.Almanac.Application;
using Astronomy.Modules.Ephemeris.Application;
using Astronomy.SharedKernel;
using Astronomy.SharedKernel.Coordinates;
using Astronomy.SharedKernel.Datasets;

namespace Astronomy.UnitTests;

public class AlmanacServiceTests
{
    private static readonly ObserverLocation Oslo = ObserverLocation.FromDegrees(59.9, 10.7, 0);

    private static AlmanacService Service(FakeEphemeris ephemeris) => new(ephemeris);

    [Fact]
    public async Task GetYearlyAsync_Returns12MonthsInOrder()
    {
        var ephemeris = new FakeEphemeris();
        var result = await Service(ephemeris).GetYearlyAsync(2026, Oslo, CancellationToken.None);

        Assert.Equal("2026", result.Year);
        Assert.Equal(12, result.Months.Count);
        for (var m = 1; m <= 12; m++)
        {
            Assert.Equal($"2026-{m:D2}", result.Months[m - 1].Month);
            Assert.Equal(DateTime.DaysInMonth(2026, m), result.Months[m - 1].Days.Count);
        }
    }

    [Fact]
    public async Task GetYearlyAsync_LeapYear_Covers366Days()
    {
        var ephemeris = new FakeEphemeris();
        var result = await Service(ephemeris).GetYearlyAsync(2028, Oslo, CancellationToken.None);

        Assert.Equal(29, result.Months[1].Days.Count); // February 2028
        Assert.Equal(366, ephemeris.RiseSetDates.Count);
        Assert.Equal(366, result.Months.Sum(m => m.Days.Count));
    }

    [Fact]
    public async Task GetYearlyAsync_CoversEveryDayOfYear()
    {
        var ephemeris = new FakeEphemeris();
        await Service(ephemeris).GetYearlyAsync(2026, Oslo, CancellationToken.None);

        Assert.Equal(365, ephemeris.RiseSetDates.Count);
        Assert.Equal(2026, ephemeris.RiseSetDates.Min(d => d.Year));
        Assert.Equal(2026, ephemeris.RiseSetDates.Max(d => d.Year));
    }

    [Fact]
    public async Task GetYearlyAsync_MonthlyEventsAreIncluded()
    {
        var ephemeris = new FakeEphemeris();
        var result = await Service(ephemeris).GetYearlyAsync(2026, Oslo, CancellationToken.None);

        Assert.Equal(24, ephemeris.EventsCalls); // 2 event searches per month (outer + inner planets)
        Assert.All(result.Months, m => Assert.NotNull(m.Events));
    }

    /// <summary>Deterministic stand-in for IEphemerisService (engine-free, fast).
    /// Thread-safe: the almanac computes months/days concurrently.</summary>
    private sealed class FakeEphemeris : IEphemerisService
    {
        private readonly System.Collections.Concurrent.ConcurrentBag<DateOnly> _dates = [];
        private int _eventsCalls;

        public HashSet<DateOnly> RiseSetDates => [.. _dates];
        public int EventsCalls => _eventsCalls;

        public Task<EphemerisPositionResult> GetPositionAsync(PositionRequest request, CancellationToken ct) =>
            Task.FromResult(new EphemerisPositionResult(request.Body, 0, 0, null, null, 0, CalculationMetadata.Empty));

        public Task<RiseSetTransitResult> GetRiseSetAsync(BodyId body, DateOnly date, ObserverLocation observer, PrecisionMode precision, CancellationToken ct)
        {
            _dates.Add(date);
            return Task.FromResult(new RiseSetTransitResult(body.Name, null, null, null, CalculationMetadata.Empty));
        }

        public Task<TwilightResult> GetTwilightAsync(DateOnly date, ObserverLocation observer, TwilightType type, PrecisionMode precision, CancellationToken ct) =>
            Task.FromResult(new TwilightResult(type, null, null, CalculationMetadata.Empty));

        public Task<MoonPhasesResult> GetMoonPhasesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
            Task.FromResult(new MoonPhasesResult(from, to, [], CalculationMetadata.Empty));

        public Task<MoonIlluminationResult> GetMoonIlluminationAsync(DateTimeOffset time, CancellationToken ct) =>
            Task.FromResult(new MoonIlluminationResult(time, 0.5, "Full Moon", CalculationMetadata.Empty));

        public Task<VisibilityResult> GetVisibilityAsync(BodyId body, DateTimeOffset time, ObserverLocation observer, PrecisionMode precision, CancellationToken ct) =>
            Task.FromResult(new VisibilityResult(body.Name, 1.0, 60, "east", "Leo", 30, 180, true, true, CalculationMetadata.Empty));

        public Task<EventsResult> GetEventsAsync(DateTimeOffset from, DateTimeOffset to, IReadOnlyList<BodyId> bodies, IReadOnlyList<EventType> types, CancellationToken ct)
        {
            Interlocked.Increment(ref _eventsCalls);
            return Task.FromResult(new EventsResult(from, to, [], CalculationMetadata.Empty));
        }
    }
}
