using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Astronomy.ApiTests;

public class ApiConventionTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiConventionTests(WebApplicationFactory<Program> factory)
    {
        var db = Path.Combine(Path.GetTempPath(), $"astro-api-{Guid.NewGuid():N}.db");
        Astronomy.Infrastructure.InfrastructureRegistrar.MigrateRegistry(db);
        _factory = factory.WithWebHostBuilder(b => b.UseSetting("ASTRONOMY_DB_PATH", db));
    }

    [Fact]
    public async Task AnonymousAccess_HealthLive_Returns200()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthReady_AfterStartupMigration_Returns200_WithComponents()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ready\"", body);
        Assert.Contains("\"db\":\"ok\"", body);
        Assert.Contains("kernels", body);
        Assert.Contains("starCatalog", body);
        Assert.Contains("satelliteElements", body);
    }

    [Fact]
    public async Task HealthReady_CorruptDatabase_Returns503()
    {
        var db = Path.Combine(Path.GetTempPath(), $"astro-api-bad-{Guid.NewGuid():N}.db");
        await File.WriteAllTextAsync(db, "this is not a sqlite database");
        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => b.UseSetting("ASTRONOMY_DB_PATH", db));
        try
        {
            var client = factory.CreateClient();
            var response = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("\"status\":\"not-ready\"", body);
        }
        finally
        {
            await factory.DisposeAsync();
            File.Delete(db);
        }
    }

    [Fact]
    public async Task Healthz_And_Ready_AreGone()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/healthz")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/ready")).StatusCode);
    }

    [Fact]
    public async Task JulianDate_J2000_ReturnsExpectedJd()
    {
        var client = _factory.CreateClient();
        var result = await client.GetFromJsonAsync<JulianDateResponse>("/api/v1/time/julian-date?time=2000-01-01T12:00:00Z");
        Assert.NotNull(result);
        Assert.Equal(2451545.0, result!.JulianDate, 8);
    }

    [Fact]
    public async Task TimeScales_ReturnsAllScales()
    {
        var client = _factory.CreateClient();
        var result = await client.GetFromJsonAsync<TimeScalesResponse>("/api/v1/time/time-scales?time=2026-08-04T12:00:00Z");
        Assert.NotNull(result);
        Assert.Equal(69.184, result!.TtMinusUtcSeconds, 3);
        Assert.Equal(37.0, result.TaiMinusUtcSeconds, 6);
    }

    [Fact]
    public async Task InvalidTime_Returns400_WithAstCode()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/time/julian-date?time=not-a-time");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("AST-4001", body);
    }

    [Fact]
    public async Task SunPosition_Real_Returns200_WithSaneValues()
    {
        var client = _factory.CreateClient();
        var result = await client.GetFromJsonAsync<SunPositionResponse>(
            "/api/v1/ephemeris/sun/position?time=2026-08-04T12:00:00Z&latitude=59.9&longitude=10.7&frame=of-date&positionType=apparent&refraction=none&precision=consumer");
        Assert.NotNull(result);
        Assert.InRange(result!.RightAscensionDeg, 130, 140);
        Assert.InRange(result.DeclinationDeg, 10, 20);
    }

    [Fact]
    public async Task SunPosition_AdvancedPrecision_WithoutKernels_Returns503()
    {
        // of-date at advanced/reference is now supported by the ERFA chain; in CI
        // the kernels are absent, so the reference tier is unavailable (503).
        var client = _factory.CreateClient();
        var response = await client.GetAsync(
            "/api/v1/ephemeris/sun/position?time=2026-08-04T12:00:00Z&latitude=59.9&longitude=10.7&frame=of-date&positionType=apparent&refraction=none&precision=advanced");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("AST-5030", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SunPosition_Pre1972Reference_WithoutKernels_Returns503()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(
            "/api/v1/ephemeris/sun/position?time=1900-06-01T12:00:00Z&frame=icrs&positionType=astrometric&refraction=none&precision=reference");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("AST-5030", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SunPosition_HorizontalReference_WithoutKernels_Returns503()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(
            "/api/v1/ephemeris/sun/position?time=2026-08-04T12:00:00Z&latitude=59.9&longitude=10.7&frame=horizontal&positionType=astrometric&refraction=none&precision=reference");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("AST-5030", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SunPosition_ReferencePrecision_WithoutKernels_Returns503()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(
            "/api/v1/ephemeris/sun/position?time=2026-08-04T12:00:00Z&latitude=59.9&longitude=10.7&frame=icrs&positionType=astrometric&refraction=none&precision=reference");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("AST-5030", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StarSearch_WithoutCatalog_Returns503()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/stars/search?ra=101.2&dec=-16.7&radius=5&maxMagnitude=6.5&limit=10");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("AST-5031", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StarPosition_WithoutCatalog_Returns503()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(
            "/api/v1/stars/32349/position?time=2026-08-04T12:00:00Z&frame=icrs&positionType=astrometric&refraction=none");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("AST-5031", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StarRiseSet_WithoutCatalog_Returns503()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/stars/91262/rise-set?date=2026-08-04&latitude=59.9&longitude=10.7");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("AST-5031", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task StarSearch_MissingRaDec_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/stars/search?radius=5");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("AST-4001", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SatellitePosition_WithoutElements_Returns503()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(
            "/api/v1/satellites/25544/position?time=2026-08-05T12:00:00Z&latitude=59.9&longitude=10.7");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("AST-5032", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SatellitePasses_WithoutElements_Returns503()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(
            "/api/v1/satellites/25544/passes?date=2026-08-05&latitude=59.9&longitude=10.7&minElevation=10");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("AST-5032", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SatellitePosition_MissingObserver_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/satellites/25544/position?time=2026-08-05T12:00:00Z");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("AST-4001", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SunPosition_Geometric_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(
            "/api/v1/ephemeris/sun/position?time=2026-08-04T12:00:00Z&latitude=59.9&longitude=10.7&frame=icrs&positionType=geometric&refraction=none");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BoundsExceeded_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/calendars/date-arithmetic?date=2026-08-04&days=200000");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RequestId_IsEchoed()
    {
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/healthz");
        request.Headers.Add("X-Request-Id", "test-request-123");
        var response = await client.SendAsync(request);
        Assert.Equal("test-request-123", response.Headers.GetValues("X-Request-Id").First());
    }

    [Fact]
    public async Task OpenApiDocument_Generates()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("astronomy", body.ToLowerInvariant());
    }

    [Fact]
    public async Task InvalidDate_NonDateString_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(
            "/api/v1/ephemeris/sun/rise-set?date=not-a-date&latitude=59.9&longitude=10.7");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("AST-4001", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task InvalidDate_CalendarOverflow_Returns400()
    {
        // 2026-02-31 is not a valid calendar date; must be 400, not 500.
        var client = _factory.CreateClient();
        var response = await client.GetAsync(
            "/api/v1/ephemeris/sun/rise-set?date=2026-02-31&latitude=59.9&longitude=10.7");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("AST-4001", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RiseSet_HasPublicCacheHeader()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(
            "/api/v1/ephemeris/sun/rise-set?date=2026-08-04&latitude=59.9&longitude=10.7");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=900", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task HorizontalPosition_MissingObserver_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(
            "/api/v1/ephemeris/sun/position?time=2026-08-04T12:00:00Z&frame=horizontal&precision=consumer");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("AST-4001", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task EquatorialPosition_MissingObserver_StillWorks()
    {
        // Equatorial frames do not need an observer; missing coords must not 400.
        var client = _factory.CreateClient();
        var response = await client.GetAsync(
            "/api/v1/ephemeris/sun/position?time=2026-08-04T12:00:00Z&frame=icrs&positionType=astrometric&precision=consumer");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SatellitePasses_InvalidMinElevation_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(
            "/api/v1/satellites/25544/passes?date=2026-08-05&latitude=59.9&longitude=10.7&minElevation=200");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("AST-4001", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AlmanacDaily_MissingLatitude_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/almanac/daily?date=2026-08-05&longitude=10.7");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("AST-4001", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CachedEndpoint_SecondCall_ReturnsSameBody()
    {
        var client = _factory.CreateClient();
        var url = "/api/v1/ephemeris/sun/rise-set?date=2026-08-04&latitude=59.9&longitude=10.7";
        var first = await client.GetStringAsync(url);
        var second = await client.GetStringAsync(url);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task AlmanacYearly_Returns12Months()
    {
        var client = _factory.CreateClient();
        var body = await client.GetStringAsync(
            "/api/v1/almanac/monthly?year=2026&latitude=59.9&longitude=10.7");
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("2026", doc.RootElement.GetProperty("year").GetString());
        Assert.Equal(12, doc.RootElement.GetProperty("months").GetArrayLength());
    }

    [Fact]
    public async Task AlmanacMonthly_BothYearAndMonth_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(
            "/api/v1/almanac/monthly?month=2026-08&year=2026&latitude=59.9&longitude=10.7");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("AST-4001", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AlmanacMonthly_NeitherYearNorMonth_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/almanac/monthly?latitude=59.9&longitude=10.7");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("AST-4001", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AlmanacDaily_MoonPhaseName_WaxingSideIsCorrect()
    {
        // Regression: the illumination-based naming used to label the post-new-moon
        // waxing crescent as "Waning Crescent" (phase angle ~180 at new, not 0).
        var client = _factory.CreateClient();
        var body = await client.GetStringAsync(
            "/api/v1/almanac/daily?date=2026-08-15&latitude=59.9&longitude=10.7");
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("Waxing Crescent", doc.RootElement.GetProperty("moon").GetProperty("phaseName").GetString());
    }

    [Fact]
    public async Task CalendarRange_FullYear_Returns365Entries()
    {
        var client = _factory.CreateClient();
        var body = await client.GetStringAsync("/api/v1/calendars/range?from=2026-01-01&to=2026-12-31");
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(365, doc.RootElement.GetProperty("entries").GetArrayLength());
    }

    [Fact]
    public async Task CalendarRange_SpanTooLong_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/calendars/range?from=2026-01-01&to=2027-01-02");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("AST-4001", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CalendarRange_FromAfterTo_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/calendars/range?from=2026-12-31&to=2026-01-01");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CalendarRange_InvalidDate_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/v1/calendars/range?from=not-a-date&to=2026-12-31");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("AST-4001", await response.Content.ReadAsStringAsync());
    }

    public sealed record SunPositionResponse(string Body, double RightAscensionDeg, double DeclinationDeg, double? AltitudeDeg, double? AzimuthDeg, double DistanceKm);

    public sealed record JulianDateResponse(double JulianDate, double ModifiedJulianDate, string Utc);
    public sealed record TimeScalesResponse(
        string Utc, double TaiJd, double TtJd, double Ut1Jd, double TdbJd,
        double TaiMinusUtcSeconds, double TtMinusUtcSeconds, double Ut1MinusUtcSeconds,
        double TdbMinusTtSeconds, string LeapSecondDatasetVersion, string EopDatasetVersion, string AlgorithmVersion);
}
