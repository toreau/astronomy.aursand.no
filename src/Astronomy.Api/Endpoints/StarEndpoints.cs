using Astronomy.Modules.Stars.Application;
using Astronomy.SharedKernel.Coordinates;
using Astronomy.SharedKernel.Units;
using static Astronomy.Api.EndpointHelpers;

namespace Astronomy.Api.Endpoints;

public static class StarEndpoints
{
    public static RouteGroupBuilder MapStarEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("stars").WithTags("Stars");

        g.MapGet("/search", async (
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

        g.MapGet("/name", async (
            string name, Astronomy.Modules.Stars.Application.IStarService service, CancellationToken ct, HttpContext context) =>
        {
            var result = await service.SearchByNameAsync(name, ct);
            context.Response.Headers.CacheControl = "public, max-age=3600";
            return Results.Ok(result);
        }).CacheOutput(p => p.Expire(TimeSpan.FromSeconds(3600)));

        g.MapGet("/brightest", async (
            int? limit, double? maxMagnitude, string? constellation,
            Astronomy.Modules.Stars.Application.IStarService service, CancellationToken ct, HttpContext context) =>
        {
            var result = await service.GetBrightestAsync(limit ?? 10, maxMagnitude ?? 6.5, constellation, ct);
            context.Response.Headers.CacheControl = "public, max-age=3600";
            return Results.Ok(result);
        }).CacheOutput(p => p.Expire(TimeSpan.FromSeconds(3600)));

        g.MapGet("/{hip}/position", async (
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

        g.MapGet("/{hip}/rise-set", async (
            string hip, string date, double? latitude, double? longitude, double? elevationMeters,
            Astronomy.Modules.Stars.Application.IStarService service, CancellationToken ct, HttpContext context) =>
        {
            var result = await service.GetRiseSetAsync(hip,
                ParseDate(date),
                ObserverLocationFrom(latitude, longitude, elevationMeters), ct);
            context.Response.Headers.CacheControl = "public, max-age=900";
            return Results.Ok(result);
        }).CacheOutput(p => p.Expire(TimeSpan.FromSeconds(900)));

        return g;
    }
}
