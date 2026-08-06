using Astronomy.Modules.Calendars.Application;
using static Astronomy.Api.EndpointHelpers;

namespace Astronomy.Api.Endpoints;

public static class CalendarEndpoints
{
    public static RouteGroupBuilder MapCalendarEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("calendars").WithTags("Calendars");

        g.MapGet("/convert", (string date, string? timezone, ICalendarService service) =>
        {
            var parsed = ParseDate(date);
            return Results.Ok(service.ConvertDate(parsed, timezone));
        });

        g.MapGet("/date-arithmetic", (string date, int days, string? timezone, ICalendarService service) =>
        {
            if (days is < -100000 or > 100000) throw new ArgumentException("days out of range [-100000, 100000]");
            var parsed = ParseDate(date);
            return Results.Ok(service.AddDays(parsed, days, timezone));
        });

        g.MapGet("/range", (
            string from, string to, string? timezone, ICalendarService service, HttpContext context) =>
        {
            var result = service.ConvertRange(ParseDate(from), ParseDate(to), timezone);
            context.Response.Headers.CacheControl = "public, max-age=3600";
            return Results.Ok(result);
        }).CacheOutput(p => p.Expire(TimeSpan.FromSeconds(3600)));

        return g;
    }
}
