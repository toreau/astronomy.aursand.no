using System.Diagnostics;
using System.Text.Json;
using Astronomy.Api;
using Astronomy.Api.Endpoints;
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
using Microsoft.AspNetCore.Diagnostics;

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
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(
        System.Text.Json.JsonNamingPolicy.CamelCase)));
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
{
    var ex = context.Features.Get<IExceptionHandlerFeature>()?.Error;
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

app.MapHealthEndpoints(dbPath);
var api = app.MapGroup("/api/v1");
api.MapTimeEndpoints();
api.MapCalendarEndpoints();
api.MapEphemerisEndpoints();
api.MapStarEndpoints();
api.MapSatelliteEndpoints();
api.MapAlmanacEndpoints();

app.MapOpenApi();

app.Run();

public partial class Program { }
