using System.Net;
using System.Net.Http.Json;
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
    public async Task AnonymousAccess_Healthz_Returns200()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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

    public sealed record SunPositionResponse(string Body, double RightAscensionDeg, double DeclinationDeg, double? AltitudeDeg, double? AzimuthDeg, double DistanceKm);

    public sealed record JulianDateResponse(double JulianDate, double ModifiedJulianDate, string Utc);
    public sealed record TimeScalesResponse(
        string Utc, double TaiJd, double TtJd, double Ut1Jd, double TdbJd,
        double TaiMinusUtcSeconds, double TtMinusUtcSeconds, double Ut1MinusUtcSeconds,
        double TdbMinusTtSeconds, string LeapSecondDatasetVersion, string EopDatasetVersion, string AlgorithmVersion);
}
