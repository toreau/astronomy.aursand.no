using System.Text.Json;
using Astronomy.Infrastructure;
using Astronomy.Infrastructure.Time;
using Astronomy.Modules.Almanac.Application;
using Astronomy.Modules.Calendars.Application;
using Astronomy.Modules.Ephemeris.Application;
using Astronomy.Modules.Ephemeris.Reference;
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
builder.Services.AddSingleton(sp =>
    Astronomy.Infrastructure.Stars.StarCatalogLoader.LoadStarCatalog(sp.GetRequiredService<Astronomy.SharedKernel.Datasets.IDatasetCatalog>(), dataRoot));
builder.Services.AddSingleton<Astronomy.Modules.Ephemeris.Reference.IReferenceEphemeris>(sp =>
{
    var eopC04 = Astronomy.Infrastructure.Time.EopC04Loader.LoadEopC04(
        sp.GetRequiredService<Astronomy.SharedKernel.Datasets.IDatasetCatalog>(), dataRoot);
    return new Astronomy.Modules.Ephemeris.Reference.SpiceReferenceEphemeris(
        Environment.GetEnvironmentVariable("ASTRONOMY_KERNEL_PATH") ?? "/data/kernels", eopC04);
});
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
        ReferenceEphemerisUnavailableException => StatusCodes.Status503ServiceUnavailable,
        Astronomy.Modules.Stars.Application.StarCatalogUnavailableException => StatusCodes.Status503ServiceUnavailable,
        ArgumentException => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError,
    };
    var code = ex switch
    {
        FeatureNotImplementedInPhaseException n => $"AST-5010:{n.Feature}:{n.Phase}",
        ReferenceEphemerisUnavailableException => "AST-5030",
        Astronomy.Modules.Stars.Application.StarCatalogUnavailableException => "AST-5031",
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

app.MapGet("/ready", (Astronomy.Modules.Ephemeris.Reference.IReferenceEphemeris reference) =>
{
    try
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        cmd.ExecuteScalar();
        return Results.Ok(new
        {
            status = "ready",
            db = "ok",
            kernels = reference.IsAvailable ? "ok" : "unavailable",
        });
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

app.MapGet("/api/v1/ephemeris/{body}/position", async (
    string body, string? time, double? latitude, double? longitude, double? elevationMeters,
    string? frame, string? positionType, string? refraction, string? precision,
    IEphemerisService service, CancellationToken ct, HttpContext context) =>
{
    if (!BodyId.TryParse(body, out var bodyId))
        throw new ArgumentException($"unknown body '{body}'");
    var observer = latitude is null || longitude is null
        ? Astronomy.SharedKernel.Coordinates.ObserverLocation.FromDegrees(0, 0, 0)
        : ObserverLocationFrom(latitude, longitude, elevationMeters);
    var request = new PositionRequest(
        bodyId.Name,
        ParseTime(time),
        observer,
        ParseFrame(frame),
        ParsePositionType(positionType),
        ParseRefraction(refraction),
        ParsePrecision(precision));
    var result = await service.GetPositionAsync(request, ct);
    context.Response.Headers.CacheControl = "no-cache";
    return Results.Ok(result);
});

app.MapGet("/api/v1/ephemeris/{body}/rise-set", async (
    string body, string date, double? latitude, double? longitude, double? elevationMeters, string? precision,
    IEphemerisService service, CancellationToken ct, HttpContext context) =>
{
    if (!BodyId.TryParse(body, out var bodyId))
        throw new ArgumentException($"unknown body '{body}'");
    var result = await service.GetRiseSetAsync(bodyId,
        DateOnly.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        ObserverLocationFrom(latitude, longitude, elevationMeters), ParsePrecision(precision), ct);
    context.Response.Headers.CacheControl = "public, max-age=900";
    return Results.Ok(result);
});

app.MapGet("/api/v1/ephemeris/twilight", async (
    string date, double? latitude, double? longitude, double? elevationMeters, string? type, string? precision,
    IEphemerisService service, CancellationToken ct, HttpContext context) =>
{
    var twilightType = type?.ToLowerInvariant() switch
    {
        "civil" => TwilightType.Civil,
        "nautical" => TwilightType.Nautical,
        "astronomical" => TwilightType.Astronomical,
        _ => throw new ArgumentException($"unknown twilight type '{type}'"),
    };
    var result = await service.GetTwilightAsync(
        DateOnly.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        ObserverLocationFrom(latitude, longitude, elevationMeters), twilightType, ParsePrecision(precision), ct);
    context.Response.Headers.CacheControl = "public, max-age=900";
    return Results.Ok(result);
});

app.MapGet("/api/v1/ephemeris/moon/phases", async (
    string? from, string? to, IEphemerisService service, CancellationToken ct, HttpContext context) =>
{
    var result = await service.GetMoonPhasesAsync(ParseTime(from), ParseTime(to), ct);
    context.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.Ok(result);
});

app.MapGet("/api/v1/ephemeris/{body}/visibility", async (
    string body, string? time, double? latitude, double? longitude, double? elevationMeters, string? precision,
    IEphemerisService service, CancellationToken ct, HttpContext context) =>
{
    if (!BodyId.TryParse(body, out var bodyId))
        throw new ArgumentException($"unknown body '{body}'");
    var result = await service.GetVisibilityAsync(bodyId, ParseTime(time),
        ObserverLocationFrom(latitude, longitude, elevationMeters), ParsePrecision(precision), ct);
    context.Response.Headers.CacheControl = "no-cache";
    return Results.Ok(result);
});

app.MapGet("/api/v1/ephemeris/events", async (
    string? from, string? to, string? bodies, string? types,
    IEphemerisService service, CancellationToken ct, HttpContext context) =>
{
    if (!BodyId.TryParseList(bodies ?? "jupiter", out var bodyList))
        throw new ArgumentException($"unknown body in '{bodies}'");
    var typeList = new List<EventType>();
    foreach (var part in (types ?? "opposition").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        typeList.Add(part.ToLowerInvariant() switch
        {
            "opposition" => EventType.Opposition,
            "conjunction" => EventType.Conjunction,
            "max-elongation" or "maxelongation" => EventType.MaxElongation,
            _ => throw new ArgumentException($"unknown event type '{part}'"),
        });
    }
    var result = await service.GetEventsAsync(ParseTime(from), ParseTime(to), bodyList, typeList, ct);
    context.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.Ok(result);
});

app.MapGet("/api/v1/stars/search", async (
    double? ra, double? dec, double? radius, double? maxMagnitude, int? limit, string? time,
    Astronomy.Modules.Stars.Application.IStarService service, CancellationToken ct, HttpContext context) =>
{
    if (ra is null || dec is null)
        throw new ArgumentException("ra and dec are required (degrees, ICRS J2000)");
    var request = new Astronomy.Modules.Stars.Application.ConeSearchRequest(
        new Astronomy.SharedKernel.Units.Angle(ra.Value),
        new Astronomy.SharedKernel.Units.Angle(dec.Value),
        new Astronomy.SharedKernel.Units.Angle(radius ?? 10.0),
        maxMagnitude ?? 6.5,
        limit ?? 50,
        time is null ? null : ParseTime(time));
    var result = await service.ConeSearchAsync(request, ct);
    context.Response.Headers.CacheControl = "public, max-age=900";
    return Results.Ok(result);
});

app.MapGet("/api/v1/stars/name", async (
    string name, Astronomy.Modules.Stars.Application.IStarService service, CancellationToken ct, HttpContext context) =>
{
    var result = await service.SearchByNameAsync(name, ct);
    context.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.Ok(result);
});

app.MapGet("/api/v1/stars/brightest", async (
    int? limit, double? maxMagnitude, string? constellation,
    Astronomy.Modules.Stars.Application.IStarService service, CancellationToken ct, HttpContext context) =>
{
    var result = await service.GetBrightestAsync(limit ?? 10, maxMagnitude ?? 6.5, constellation, ct);
    context.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.Ok(result);
});

app.MapGet("/api/v1/stars/{hip}/position", async (
    string hip, string? time, double? latitude, double? longitude, double? elevationMeters,
    string? frame, string? positionType, string? refraction,
    Astronomy.Modules.Stars.Application.IStarService service, CancellationToken ct, HttpContext context) =>
{
    var observer = latitude is null || longitude is null
        ? Astronomy.SharedKernel.Coordinates.ObserverLocation.FromDegrees(0, 0, 0)
        : ObserverLocationFrom(latitude, longitude, elevationMeters);
    var result = await service.GetStarAsync(hip, ParseTime(time), ParseFrame(frame),
        ParsePositionType(positionType), observer, ParseRefraction(refraction) == Astronomy.SharedKernel.Coordinates.RefractionModel.Simple, ct);
    context.Response.Headers.CacheControl = "no-cache";
    return Results.Ok(result);
});

app.MapGet("/api/v1/stars/{hip}/rise-set", async (
    string hip, string date, double? latitude, double? longitude, double? elevationMeters,
    Astronomy.Modules.Stars.Application.IStarService service, CancellationToken ct, HttpContext context) =>
{
    var result = await service.GetRiseSetAsync(hip,
        DateOnly.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        ObserverLocationFrom(latitude, longitude, elevationMeters), ct);
    context.Response.Headers.CacheControl = "public, max-age=900";
    return Results.Ok(result);
});

app.MapGet("/api/v1/almanac/daily", async (
    string date, double? latitude, double? longitude, double? elevationMeters, string? precision,
    IAlmanacService service, CancellationToken ct) =>
{
    var request = new DailyAlmanacRequest(
        DateOnly.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        latitude ?? throw new ArgumentException("latitude required"),
        longitude ?? throw new ArgumentException("longitude required"),
        elevationMeters ?? 0,
        precision ?? "consumer");
    return Results.Ok(await service.GetDailyAsync(request, ct));
});

app.MapGet("/api/v1/almanac/monthly", async (
    string month, double? latitude, double? longitude, double? elevationMeters,
    IAlmanacService service, CancellationToken ct, HttpContext context) =>
{
    if (!DateTime.TryParseExact(month, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var parsed))
        throw new ArgumentException($"invalid month '{month}' (expected yyyy-MM)");
    var result = await service.GetMonthlyAsync(parsed.Year, parsed.Month,
        ObserverLocationFrom(latitude, longitude, elevationMeters), ct);
    context.Response.Headers.CacheControl = "public, max-age=900";
    return Results.Ok(result);
});

app.MapOpenApi();

app.Run("http://0.0.0.0:8080");

static Astronomy.SharedKernel.Coordinates.CoordinateFrame ParseFrame(string? frame) => frame?.ToLowerInvariant() switch
{
    null => Astronomy.SharedKernel.Coordinates.CoordinateFrame.EquatorialOfDate,
    "icrs" or "j2000" => Astronomy.SharedKernel.Coordinates.CoordinateFrame.IcrJ2000,
    "of-date" or "equatorial-of-date" => Astronomy.SharedKernel.Coordinates.CoordinateFrame.EquatorialOfDate,
    "horizontal" or "alt-az" => Astronomy.SharedKernel.Coordinates.CoordinateFrame.Horizontal,
    _ => throw new ArgumentException($"unknown frame '{frame}' (supported: icrs, of-date, horizontal)"),
};

static Astronomy.SharedKernel.Coordinates.PositionType ParsePositionType(string? positionType) => positionType?.ToLowerInvariant() switch
{
    null => Astronomy.SharedKernel.Coordinates.PositionType.Apparent,
    "astrometric" => Astronomy.SharedKernel.Coordinates.PositionType.Astrometric,
    "apparent" => Astronomy.SharedKernel.Coordinates.PositionType.Apparent,
    "geometric" => Astronomy.SharedKernel.Coordinates.PositionType.Geometric,
    _ => throw new ArgumentException($"unknown positionType '{positionType}'"),
};

static Astronomy.SharedKernel.Coordinates.RefractionModel ParseRefraction(string? refraction) => refraction?.ToLowerInvariant() switch
{
    null or "none" => Astronomy.SharedKernel.Coordinates.RefractionModel.None,
    "simple" or "standard" => Astronomy.SharedKernel.Coordinates.RefractionModel.Simple,
    _ => throw new ArgumentException($"unknown refraction '{refraction}'"),
};

static Astronomy.SharedKernel.Coordinates.PrecisionMode ParsePrecision(string? precision) => precision?.ToLowerInvariant() switch
{
    null or "consumer" => Astronomy.SharedKernel.Coordinates.PrecisionMode.Consumer,
    "advanced" => Astronomy.SharedKernel.Coordinates.PrecisionMode.Advanced,
    "reference" => Astronomy.SharedKernel.Coordinates.PrecisionMode.Reference,
    _ => throw new ArgumentException($"unknown precision '{precision}'"),
};

static DateTimeOffset ParseTime(string? time) =>
    time is null
        ? DateTimeOffset.UtcNow
        : DateTimeOffset.TryParse(time, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : throw new ArgumentException($"invalid time '{time}' (expected ISO 8601)");

static Astronomy.SharedKernel.Coordinates.ObserverLocation ObserverLocationFrom(double? latitude, double? longitude, double? elevationMeters) =>
    Astronomy.SharedKernel.Coordinates.ObserverLocation.FromDegrees(
        latitude ?? throw new ArgumentException("latitude required"),
        longitude ?? throw new ArgumentException("longitude required"),
        elevationMeters ?? 0);

public partial class Program { }
