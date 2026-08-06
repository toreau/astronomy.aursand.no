using Astronomy.Modules.Almanac.Application;
using static Astronomy.Api.EndpointHelpers;

namespace Astronomy.Api.Endpoints;

public static class AlmanacEndpoints
{
    public static RouteGroupBuilder MapAlmanacEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("almanac").WithTags("Almanac");

        g.MapGet("/daily", async (
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

        g.MapGet("/monthly", async (
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

        return g;
    }
}
