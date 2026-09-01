namespace Astronomy.Api;

public static class CodeqlProbe
{
    public static string? Probe(HttpContext context)
    {
        var path = context.Request.Query["file"].ToString();
        return File.ReadAllText(path);
    }
}
