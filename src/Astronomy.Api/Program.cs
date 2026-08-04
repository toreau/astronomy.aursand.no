using System.Text.Json;
using Astronomy.Infrastructure;
using Astronomy.Infrastructure.Time;
using Astronomy.Modules.Almanac.Application;
using Astronomy.Modules.Calendars.Application;
using Astronomy.Modules.Ephemeris.Application;
using Astronomy.Modules.Satellites.Application;
using Astronomy.Modules.Stars.Application;
using Astronomy.Modules.Time.Application;
using Astronomy.SharedKernel;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

var dbPath = builder.Configuration["ASTRONOMY_DB_PATH"] ?? "/data/astronomy.db";
var dataRoot = builder.Configuration["ASTRONOMY_DATA_ROOT"] ?? "/data";
Console.WriteLine($"astronomy-api: db={dbPath} dataRoot={dataRoot}");

builder.Services.AddAstronomyInfrastructure(dbPath, dataRoot);
builder.Services.AddSingleton(sp =>
    TimeDatasetLoaders.CreateTimeScaleConverter(sp.GetRequiredService<Astronomy.SharedKernel.Datasets.IDatasetCatalog>(), dataRoot));
builder.Services.AddCalendarsModule();
builder.Services.AddTimeModule();
builder.Services.AddEphemerisModule();
builder.Services.AddStarsModule();
builder.Services.AddSatellitesModule(dbPath);
builder.Services.AddAlmanacModule();
builder.Services.AddOpenApi();

var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(o => o.AddPolicy("public-site", p =>
        p.WithOrigins(corsOrigins).WithMethods("GET", "HEAD").AllowAnyHeader()));
}

var app = builder.Build();

app.Use(async (context, next) =>
{
    var requestId = context.Request.Headers["X-Request-Id"].FirstOrDefault() ?? Guid.NewGuid().ToString("N");
    context.Response.Headers["X-Request-Id"] = requestId;
    context.Items["requestId"] = requestId;
    await next();
});

app.UseExceptionHandler(err => err.Run(async context =>
{
    var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var status = ex switch
    {
        FeatureNotImplementedInPhaseException => StatusCodes.Status501NotImplemented,
        ArgumentException => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError,
    };
    var code = ex switch
    {
        FeatureNotImplementedInPhaseException n => $"AST-5010:{n.Feature}:{n.Phase}",
        ArgumentException => "AST-4001",
        _ => "AST-5000",
    };
    context.Response.StatusCode = status;
    context.Response.ContentType = "application/problem+json";
    await context.Response.WriteAsync(JsonSerializer.Serialize(new
    {
        type = "https://astronomy.aursand.no/errors/" + code.Split(':')[0],
        title = ex?.GetType().Name,
        status,
        detail = ex?.Message,
        instance = context.Request.Path.ToString(),
        code,
    }));
}));

if (corsOrigins.Length > 0) app.UseCors("public-site");

app.MapGet("/", () => Results.Text("Astronomy API"));

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapGet("/ready", () =>
{
    try
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        cmd.ExecuteScalar();
        return Results.Ok(new { status = "ready", db = "ok" });
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "not-ready", db = ex.Message.Split('\n')[0] }, statusCode: 503);
    }
});

app.MapGet("/api/v1/time/julian-date", (string? time, ITimeService service) =>
{
    var utc = ParseTime(time);
    return Results.Ok(service.GetJulianDate(utc));
});

app.MapGet("/api/v1/time/time-scales", (string? time, ITimeService service) =>
{
    var utc = ParseTime(time);
    return Results.Ok(service.GetTimeScales(utc));
});

app.MapGet("/api/v1/calendars/convert", (string date, string? timezone, ICalendarService service) =>
{
    var parsed = DateOnly.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    return Results.Ok(service.ConvertDate(parsed, timezone));
});

app.MapGet("/api/v1/calendars/date-arithmetic", (string date, int days, string? timezone, ICalendarService service) =>
{
    if (days is < -100000 or > 100000) throw new ArgumentException("days out of range [-100000, 100000]");
    var parsed = DateOnly.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    return Results.Ok(service.AddDays(parsed, days, timezone));
});

app.MapGet("/api/v1/ephemeris/sun/position", async (
    string? time, double? latitude, double? longitude, IEphemerisService service, CancellationToken ct) =>
{
    var request = new PositionRequest("sun", ParseTime(time), ObserverLocationFrom(latitude, longitude),
        Astronomy.SharedKernel.Coordinates.CoordinateFrame.EquatorialOfDate,
        Astronomy.SharedKernel.Coordinates.PositionType.Apparent,
        Astronomy.SharedKernel.Coordinates.RefractionModel.None,
        Astronomy.SharedKernel.Coordinates.PrecisionMode.Consumer);
    return Results.Ok(await service.GetPositionAsync(request, ct));
});

app.MapGet("/api/v1/almanac/daily", async (
    string date, double? latitude, double? longitude, IAlmanacService service, CancellationToken ct) =>
{
    var request = new DailyAlmanacRequest(DateOnly.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        latitude ?? throw new ArgumentException("latitude required"), longitude ?? throw new ArgumentException("longitude required"), 0, "consumer");
    return Results.Ok(await service.GetDailyAsync(request, ct));
});

app.MapOpenApi();

app.Run("http://0.0.0.0:8080");

static DateTimeOffset ParseTime(string? time) =>
    time is null
        ? DateTimeOffset.UtcNow
        : DateTimeOffset.TryParse(time, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : throw new ArgumentException($"invalid time '{time}' (expected ISO 8601)");

static Astronomy.SharedKernel.Coordinates.ObserverLocation ObserverLocationFrom(double? latitude, double? longitude) =>
    Astronomy.SharedKernel.Coordinates.ObserverLocation.FromDegrees(
        latitude ?? throw new ArgumentException("latitude required"),
        longitude ?? throw new ArgumentException("longitude required"), 0);
public partial class Program { }
