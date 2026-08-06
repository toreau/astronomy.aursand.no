using Astronomy.SharedKernel.Datasets;
using Microsoft.Data.Sqlite;

namespace Astronomy.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app, string dbPath)
    {
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

        return app;
    }

    private static string DatabaseCheck(string dbPath)
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

    private static string ReferenceStatus(IServiceProvider sp)
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

    private static Dictionary<string, string> KernelHashes(IServiceProvider sp)
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

    private static string StarCatalogStatus(IServiceProvider sp)
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

    private static Dictionary<string, string> DatasetVersions(IServiceProvider sp, bool dbOk)
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

    private static async Task<string> SatelliteElementsStatus(IServiceProvider sp, bool dbOk, CancellationToken ct)
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
}
