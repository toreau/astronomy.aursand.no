using Astronomy.Modules.Ephemeris.Application;
using Astronomy.SharedKernel.Coordinates;
using static Astronomy.Api.EndpointHelpers;

namespace Astronomy.Api.Endpoints;

public static class EphemerisEndpoints
{
    public static RouteGroupBuilder MapEphemerisEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("ephemeris").WithTags("Ephemeris");

        g.MapGet("/{body}/position", async (
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

        g.MapGet("/{body}/rise-set", async (
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

        g.MapGet("/twilight", async (
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

        g.MapGet("/moon/phases", async (
            string? from, string? to, IEphemerisService service, CancellationToken ct, HttpContext context) =>
        {
            var result = await service.GetMoonPhasesAsync(ParseTime(from), ParseTime(to), ct);
            context.Response.Headers.CacheControl = "public, max-age=3600";
            return Results.Ok(result);
        }).CacheOutput(p => p.Expire(TimeSpan.FromSeconds(3600)));

        g.MapGet("/{body}/visibility", async (
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

        g.MapGet("/events", async (
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

        return g;
    }
}
