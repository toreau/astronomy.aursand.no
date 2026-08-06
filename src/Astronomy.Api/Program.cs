using System.Diagnostics;
using System.Text.Json;
using Astronomy.Api;
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
using Astronomy.SharedKernel.Coordinates;
using Astronomy.SharedKernel.Datasets;
using Astronomy.SharedKernel.Stars;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

var dbPath = builder.Configuration["ASTRONOMY_DB_PATH"] ?? "/data/astronomy.db";
var dataRoot = builder.Configuration["ASTRONOMY_DATA_ROOT"] ?? "/data";
Console.WriteLine($"astronomy-api: db={dbPath} dataRoot={dataRoot}");

try
{
    InfrastructureRegistrar.MigrateRegistry(dbPath);
    SatelliteStore.EnsureSchema(dbPath);
    Console.WriteLine("astronomy-api: schema ok (registry + satellites)");
}
catch (Exception ex)
{
    Console.WriteLine($"astronomy-api: schema init FAIL {ex.Message.Split('\n')[0]} (health/ready will report not-ready)");
}

builder.Services.AddAstronomyInfrastructure(dbPath, dataRoot);
builder.Services.AddMemoryCache();
builder.Services.AddOutputCache();
builder.Services.AddResponseCompression();
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

var app = builder.Build();
var logger = app.Logger;

app.Use(async (context, next) =>
{
    var requestId = context.Request.Headers["X-Request-Id"].FirstOrDefault() ?? Guid.NewGuid().ToString("N");
    context.Response.Headers["X-Request-Id"] = requestId;
    context.Items["requestId"] = requestId;
    var sw = Stopwatch.StartNew();
    try
    {
        await next();
    }
    finally
    {
        sw.Stop();
        logger.LogInformation("HTTP {Method} {Path} -> {Status} in {DurationMs}ms (requestId={RequestId})",
            context.Request.Method, context.Request.Path, context.Response.StatusCode, sw.ElapsedMilliseconds, requestId);
    }
});

app.UseExceptionHandler(err => err.Run(async context =>
{    var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var requestId = context.Items["requestId"]?.ToString() ?? "unknown";
    var (status, code, detail) = ex switch
    {
        FeatureNotImplementedInPhaseException n => (StatusCodes.Status501NotImplemented, $"AST-5010:{n.Feature}:{n.Phase}", ex.Message),
        ReferenceEphemerisUnavailableException => (StatusCodes.Status503ServiceUnavailable, "AST-5030", ProblemDetailSanitizer.SanitizeDetail(ex.Message)),
        Astronomy.Modules.Stars.Application.StarCatalogUnavailableException => (StatusCodes.Status503ServiceUnavailable, "AST-5031", ProblemDetailSanitizer.SanitizeDetail(ex.Message)),
        Astronomy.Modules.Satellites.Application.SatelliteElementsUnavailableException => (StatusCodes.Status503ServiceUnavailable, "AST-5032", ProblemDetailSanitizer.SanitizeDetail(ex.Message)),
        ArgumentException or FormatException or OverflowException or InvalidDataException => (StatusCodes.Status400BadRequest, "AST-4001", ex.Message),
        _ => (StatusCodes.Status500InternalServerError, "AST-5000", "internal error"),
    };
    if (status == StatusCodes.Status503ServiceUnavailable)
        logger.LogWarning(ex, "Service unavailable (requestId={RequestId}, path={Path})", requestId, context.Request.Path);
    else if (status >= 500)
        logger.LogError(ex, "Unhandled exception (requestId={RequestId}, path={Path})", requestId, context.Request.Path);
    context.Response.StatusCode = status;
    context.Response.ContentType = "application/problem+json";
    await context.Response.WriteAsync(JsonSerializer.Serialize(new
    {
        type = "https://astronomy.aursand.no/errors/" + code.Split(':')[0],
        title = ex?.GetType().Name,
        status,
        detail,
        instance = context.Request.Path.ToString(),
        code,
    }));
}));

app.UseResponseCompression();

app.UseOutputCache();

app.MapGet("/", () => Results.Text("Astronomy API"));

app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));

app.MapGet("/health/ready", async (HttpContext context, CancellationToken ct) =>
{
    var db = DatabaseCheck(dbPath);
    var sp = context.RequestServices!;
    var payload = new
    {
        status = db == "ok" ? "ready" : "not-ready",
        db,
        kernels = ReferenceStatus(sp),
        kernelHashes = KernelHashes(sp),
        starCatalog = StarCatalogStatus(sp),
        datasets = DatasetVersions(sp, db == "ok"),
        satelliteElements = await SatelliteElementsStatus(sp, db == "ok", ct),
    };
    return db == "ok"
        ? Results.Ok(payload)
        : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
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
    var parsed = ParseDate(date);
    return Results.Ok(service.ConvertDate(parsed, timezone));
});

app.MapGet("/api/v1/calendars/date-arithmetic", (string date, int days, string? timezone, ICalendarService service) =>
{
    if (days is < -100000 or > 100000) throw new ArgumentException("days out of range [-100000, 100000]");
    var parsed = ParseDate(date);
    return Results.Ok(service.AddDays(parsed, days, timezone));
});

app.MapGet("/api/v1/calendars/range", (
    string from, string to, string? timezone, ICalendarService service, HttpContext context) =>
{
    var result = service.ConvertRange(ParseDate(from), ParseDate(to), timezone);
    context.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.Ok(result);
}).CacheOutput(p => p.Expire(TimeSpan.FromSeconds(3600)));

app.MapGet("/api/v1/ephemeris/{body}/position", async (
    string body, string? time, double? latitude, double? longitude, double? elevationMeters,
    string? frame, string? positionType, string? refraction, string? precision,
    IEphemerisService service, CancellationToken ct, HttpContext context) =>
{
    if (!BodyId.TryParse(body, out var bodyId))
        throw new ArgumentException($"unknown body '{body}'");
    var parsedFrame = ParseFrame(frame);
    if (parsedFrame == CoordinateFrame.Horizontal && (latitude is null || longitude is null))
        throw new ArgumentException("latitude and longitude are required when frame=horizontal");
    var observer = latitude is null || longitude is null
        ? ObserverLocation.FromDegrees(0, 0, 0)
        : ObserverLocationFrom(latitude, longitude, elevationMeters);
    var request = new PositionRequest(
        bodyId.Name,
        ParseTime(time),
        observer,
        parsedFrame,
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
        ParseDate(date),
        ObserverLocationFrom(latitude, longitude, elevationMeters), ParsePrecision(precision), ct);
    context.Response.Headers.CacheControl = "public, max-age=900";
    return Results.Ok(result);
}).CacheOutput(p => p.Expire(TimeSpan.FromSeconds(900)));

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
        ParseDate(date),
        ObserverLocationFrom(latitude, longitude, elevationMeters), twilightType, ParsePrecision(precision), ct);
    context.Response.Headers.CacheControl = "public, max-age=900";
    return Results.Ok(result);
}).CacheOutput(p => p.Expire(TimeSpan.FromSeconds(900)));

app.MapGet("/api/v1/ephemeris/moon/phases", async (
    string? from, string? to, IEphemerisService service, CancellationToken ct, HttpContext context) =>
{
    var result = await service.GetMoonPhasesAsync(ParseTime(from), ParseTime(to), ct);
    context.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.Ok(result);
}).CacheOutput(p => p.Expire(TimeSpan.FromSeconds(3600)));

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
}).CacheOutput(p => p.Expire(TimeSpan.FromSeconds(3600)));

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
}).CacheOutput(p => p.Expire(TimeSpan.FromSeconds(900)));

app.MapGet("/api/v1/stars/name", async (
    string name, Astronomy.Modules.Stars.Application.IStarService service, CancellationToken ct, HttpContext context) =>
{
    var result = await service.SearchByNameAsync(name, ct);
    context.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.Ok(result);
}).CacheOutput(p => p.Expire(TimeSpan.FromSeconds(3600)));

app.MapGet("/api/v1/stars/brightest", async (
    int? limit, double? maxMagnitude, string? constellation,
    Astronomy.Modules.Stars.Application.IStarService service, CancellationToken ct, HttpContext context) =>
{
    var result = await service.GetBrightestAsync(limit ?? 10, maxMagnitude ?? 6.5, constellation, ct);
    context.Response.Headers.CacheControl = "public, max-age=3600";
    return Results.Ok(result);
}).CacheOutput(p => p.Expire(TimeSpan.FromSeconds(3600)));

app.MapGet("/api/v1/stars/{hip}/position", async (
    string hip, string? time, double? latitude, double? longitude, double? elevationMeters,
    string? frame, string? positionType, string? refraction,
    Astronomy.Modules.Stars.Application.IStarService service, CancellationToken ct, HttpContext context) =>
{
    var parsedFrame = ParseFrame(frame);
    if (parsedFrame == CoordinateFrame.Horizontal && (latitude is null || longitude is null))
        throw new ArgumentException("latitude and longitude are required when frame=horizontal");
    var observer = latitude is null || longitude is null
        ? ObserverLocation.FromDegrees(0, 0, 0)
        : ObserverLocationFrom(latitude, longitude, elevationMeters);
    var result = await service.GetStarAsync(hip, ParseTime(time), parsedFrame,
        ParsePositionType(positionType), observer, ParseRefraction(refraction) == RefractionModel.Simple, ct);
    context.Response.Headers.CacheControl = "no-cache";
    return Results.Ok(result);
});

app.MapGet("/api/v1/stars/{hip}/rise-set", async (
    string hip, string date, double? latitude, double? longitude, double? elevationMeters,
    Astronomy.Modules.Stars.Application.IStarService service, CancellationToken ct, HttpContext context) =>
{
    var result = await service.GetRiseSetAsync(hip,
        ParseDate(date),
        ObserverLocationFrom(latitude, longitude, elevationMeters), ct);
    context.Response.Headers.CacheControl = "public, max-age=900";
    return Results.Ok(result);
}).CacheOutput(p => p.Expire(TimeSpan.FromSeconds(900)));

app.MapGet("/api/v1/satellites/{norad}/position", async (
    string norad, string? time, double? latitude, double? longitude, double? elevationMeters, string? refraction,
    Astronomy.Modules.Satellites.Application.ISatelliteService service, CancellationToken ct, HttpContext context) =>
{
    var observer = ObserverLocationFrom(latitude, longitude, elevationMeters);
    var result = await service.GetPositionAsync(norad, ParseTime(time), observer,
        ParseRefraction(refraction) == RefractionModel.Simple, ct);
    context.Response.Headers.CacheControl = "no-cache";
    return Results.Ok(result);
});

app.MapGet("/api/v1/satellites/{norad}/passes", async (
    string norad, string? date, string? from, string? to,
    double? latitude, double? longitude, double? elevationMeters,
    double? minElevation, double? stepSeconds,
    Astronomy.Modules.Satellites.Application.ISatelliteService service, CancellationToken ct, HttpContext context) =>
{
    var observer = ObserverLocationFrom(latitude, longitude, elevationMeters);
    DateTimeOffset fromUtc, toUtc;
    if (date is not null)
    {
        var day = ParseDate(date);
        fromUtc = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        toUtc = fromUtc.AddDays(1);
    }
    else
    {
        fromUtc = ParseTime(from);
        toUtc = ParseTime(to);
    }
    var result = await service.GetPassesAsync(norad, fromUtc, toUtc, observer,
        minElevation ?? 10.0, stepSeconds ?? 30.0, ct);
    context.Response.Headers.CacheControl = "public, max-age=300";
    return Results.Ok(result);
}).CacheOutput(p => p.Expire(TimeSpan.FromSeconds(300)));

app.MapGet("/api/v1/satellites/search", async (
    string name, Astronomy.Modules.Satellites.Application.ISatelliteService service, CancellationToken ct, HttpContext context) =>
{
    var result = await service.SearchAsync(name, ct);
    context.Response.Headers.CacheControl = "public, max-age=300";
    return Results.Ok(result);
}).CacheOutput(p => p.Expire(TimeSpan.FromSeconds(300)));

app.MapGet("/api/v1/satellites/status", async (
    Astronomy.Modules.Satellites.Application.ISatelliteService service, CancellationToken ct, HttpContext context) =>
{
    var result = await service.GetStatusAsync(ct);
    context.Response.Headers.CacheControl = "public, max-age=60";
    return Results.Ok(result);
}).CacheOutput(p => p.Expire(TimeSpan.FromSeconds(60)));

app.MapGet("/api/v1/almanac/daily", async (
    string date, double? latitude, double? longitude, double? elevationMeters, string? precision,
    IAlmanacService service, CancellationToken ct, HttpContext context) =>
{
    var request = new DailyAlmanacRequest(
        ParseDate(date),
        latitude ?? throw new ArgumentException("latitude required"),
        longitude ?? throw new ArgumentException("longitude required"),
        elevationMeters ?? 0,
        precision ?? "consumer");
    var result = await service.GetDailyAsync(request, ct);
    context.Response.Headers.CacheControl = "public, max-age=900";
    return Results.Ok(result);
}).CacheOutput(p => p.Expire(TimeSpan.FromSeconds(900)));

app.MapGet("/api/v1/almanac/monthly", async (
    string? month, int? year, double? latitude, double? longitude, double? elevationMeters,
    IAlmanacService service, CancellationToken ct, HttpContext context) =>
{
    var observer = ObserverLocationFrom(latitude, longitude, elevationMeters);
    object result;
    if (year is not null)
    {
        if (month is not null)
            throw new ArgumentException("year and month are mutually exclusive; provide exactly one");
        result = await service.GetYearlyAsync(year.Value, observer, ct);
    }
    else
    {
        if (month is null)
            throw new ArgumentException("month (yyyy-MM) or year (yyyy) is required");
        if (!DateTime.TryParseExact(month, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed))
            throw new ArgumentException($"invalid month '{month}' (expected yyyy-MM)");
        result = await service.GetMonthlyAsync(parsed.Year, parsed.Month, observer, ct);
    }
    context.Response.Headers.CacheControl = "public, max-age=900";
    return Results.Ok(result);
}).CacheOutput(p => p.Expire(TimeSpan.FromSeconds(900)));

app.MapOpenApi();

app.Run();

static string DatabaseCheck(string dbPath)
{
    try
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Datasets','ActiveDatasets')";
        var tables = (long)cmd.ExecuteScalar()!;
        return tables == 2 ? "ok" : $"schema incomplete ({tables}/2 registry tables)";
    }
    catch (Exception ex)
    {
        return ex.Message.Split('\n')[0];
    }
}

static string ReferenceStatus(IServiceProvider sp)
{
    try
    {
        var reference = sp.GetRequiredService<Astronomy.Modules.Ephemeris.Reference.IReferenceEphemeris>();
        return reference.IsAvailable ? "ok" : $"unavailable ({ProblemDetailSanitizer.SanitizeDetail(reference.UnavailableReason)})";
    }
    catch (Exception ex)
    {
        return $"error ({ProblemDetailSanitizer.SanitizeDetail(ex.Message.Split('\n')[0])})";
    }
}

static Dictionary<string, string> KernelHashes(IServiceProvider sp)
{
    try
    {
        var reference = sp.GetRequiredService<Astronomy.Modules.Ephemeris.Reference.IReferenceEphemeris>();
        return reference.IsAvailable
            ? reference.KernelVersions.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
    }
    catch
    {
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }
}

static string StarCatalogStatus(IServiceProvider sp)
{
    try
    {
        var catalog = sp.GetRequiredService<Astronomy.SharedKernel.Stars.StarCatalog>();
        return catalog.IsAvailable ? "ok" : $"unavailable ({ProblemDetailSanitizer.SanitizeDetail(catalog.Reason)})";
    }
    catch (Exception ex)
    {
        return $"error ({ProblemDetailSanitizer.SanitizeDetail(ex.Message.Split('\n')[0])})";
    }
}

static Dictionary<string, string> DatasetVersions(IServiceProvider sp, bool dbOk)
{
    var versions = new Dictionary<string, string>(StringComparer.Ordinal);
    if (!dbOk) return versions;
    try
    {
        var catalog = sp.GetRequiredService<Astronomy.SharedKernel.Datasets.IDatasetCatalog>();
        foreach (var name in catalog.DatasetNames)
            versions[name] = catalog.ActiveVersion(name)?.Version ?? "(none)";
        versions["satellite-elements"] = catalog.ActiveVersion("satellite-elements")?.Version ?? "(none)";
    }
    catch (Exception ex)
    {
        versions["error"] = ex.Message.Split('\n')[0];
    }
    return versions;
}

static async Task<string> SatelliteElementsStatus(IServiceProvider sp, bool dbOk, CancellationToken ct)
{
    if (!dbOk) return "(db unavailable)";
    try
    {
        var satellites = sp.GetRequiredService<Astronomy.Modules.Satellites.Application.ISatelliteService>();
        var status = await satellites.GetStatusAsync(ct);
        return status.ActiveVersion is null ? "unavailable (not ingested)" : $"ok ({status.ActiveVersion}, {status.ElementCount} elements)";
    }
    catch (Exception ex)
    {
        return $"error ({ProblemDetailSanitizer.SanitizeDetail(ex.Message.Split('\n')[0])})";
    }
}

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

static DateOnly ParseDate(string date) =>
    DateOnly.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.None, out var parsed)
        ? parsed
        : throw new ArgumentException($"invalid date '{date}' (expected yyyy-MM-dd)");

static Astronomy.SharedKernel.Coordinates.ObserverLocation ObserverLocationFrom(double? latitude, double? longitude, double? elevationMeters) =>
    Astronomy.SharedKernel.Coordinates.ObserverLocation.FromDegrees(
        latitude ?? throw new ArgumentException("latitude required"),
        longitude ?? throw new ArgumentException("longitude required"),
        elevationMeters ?? 0);

public partial class Program { }
