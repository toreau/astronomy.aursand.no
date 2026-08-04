using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();

var dbPath = Environment.GetEnvironmentVariable("ASTRONOMY_DB_PATH") ?? "/data/astronomy.db";
Console.WriteLine($"astronomy-api: db={dbPath}");

var app = builder.Build();

app.MapGet("/", () => Results.Text("Astronomy API skeleton"));

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapGet("/ready", () =>
{
    try
    {
        using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        cmd.ExecuteScalar();
        return Results.Ok(new { status = "ready", db = "ok" });
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "not-ready", db = ex.Message.Split('\n')[0] }, statusCode: 503);
    }
});

app.Run("http://0.0.0.0:8080");
