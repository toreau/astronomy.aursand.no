using Astronomy.Modules.Time.Application;
using static Astronomy.Api.EndpointHelpers;

namespace Astronomy.Api.Endpoints;

public static class TimeEndpoints
{
    public static RouteGroupBuilder MapTimeEndpoints(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("time").WithTags("Time");

        g.MapGet("/julian-date", (string? time, ITimeService service) =>
        {
            var utc = ParseTime(time);
            return Results.Ok(service.GetJulianDate(utc));
        });

        g.MapGet("/time-scales", (string? time, ITimeService service) =>
        {
            var utc = ParseTime(time);
            return Results.Ok(service.GetTimeScales(utc));
        });

        return g;
    }
}
