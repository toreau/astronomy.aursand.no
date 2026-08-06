using Astronomy.Modules.Satellites.Application;
using Astronomy.SharedKernel.Coordinates;
using static Astronomy.Api.EndpointHelpers;

namespace Astronomy.Api.Endpoints;

public static class SatelliteEndpoints
{
    public static RouteGroupBuilder MapSatelliteEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("satellites").WithTags("Satellites");

        g.MapGet("/{norad}/position", async (
            string norad, string? time, double? latitude, double? longitude, double? elevationMeters, string? refraction,
            Astronomy.Modules.Satellites.Application.ISatelliteService service, CancellationToken ct, HttpContext context) =>
        {
            var observer = ObserverLocationFrom(latitude, longitude, elevationMeters);
            var result = await service.GetPositionAsync(norad, ParseTime(time), observer,
                ParseRefraction(refraction) == RefractionModel.Simple, ct);
            context.Response.Headers.CacheControl = "no-cache";
            return Results.Ok(result);
        });

        g.MapGet("/{norad}/passes", async (
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

        g.MapGet("/search", async (
            string name, Astronomy.Modules.Satellites.Application.ISatelliteService service, CancellationToken ct, HttpContext context) =>
        {
            var result = await service.SearchAsync(name, ct);
            context.Response.Headers.CacheControl = "public, max-age=300";
            return Results.Ok(result);
        }).CacheOutput(p => p.Expire(TimeSpan.FromSeconds(300)));

        g.MapGet("/status", async (
            Astronomy.Modules.Satellites.Application.ISatelliteService service, CancellationToken ct, HttpContext context) =>
        {
            var result = await service.GetStatusAsync(ct);
            context.Response.Headers.CacheControl = "public, max-age=60";
            return Results.Ok(result);
        }).CacheOutput(p => p.Expire(TimeSpan.FromSeconds(60)));

        return g;
    }
}
