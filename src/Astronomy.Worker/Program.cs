using Microsoft.Data.Sqlite;
using System.Diagnostics;

var dbPath = Environment.GetEnvironmentVariable("ASTRONOMY_DB_PATH") ?? "/data/astronomy.db";
var mode = args.Length > 0 ? args[0] : "heartbeat";

switch (mode)
{
    case "migrate":
        using (var conn = Open(dbPath, readOnly: false))
        {
            Exec(conn, "PRAGMA journal_mode=WAL;");
            Exec(conn, "CREATE TABLE IF NOT EXISTS heartbeat (id INTEGER PRIMARY KEY, at_utc TEXT NOT NULL);");
        }
        Console.WriteLine("worker-migrate: schema ok");
        break;

    case "probe":
        await ProbeAsync();
        break;

    case "fixtures":
        return await Astronomy.Worker.HostGates.FetchFixturesAsync(args.Length > 1 ? args[1] : "/data/fixtures");

    case "compare":
        return Astronomy.Worker.HostGates.CompareFixtures(args.Length > 1 ? args[1] : "/data/fixtures");

    case "naif":
        return await Astronomy.Worker.HostGates.NaifAsync(args.Length > 1 ? args[1] : "/data/kernels");

    case "heartbeat":
        try
        {
            using var initConn = Open(dbPath, readOnly: false);
            Exec(initConn, "PRAGMA journal_mode=WAL;");
            Exec(initConn, "CREATE TABLE IF NOT EXISTS heartbeat (id INTEGER PRIMARY KEY, at_utc TEXT NOT NULL);");
            Console.WriteLine("worker: schema ok");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"worker: schema init FAIL {ex.Message.Split('\n')[0]}");
        }
        while (true)
        {
            try
            {
                using var conn = Open(dbPath, readOnly: false);
                Exec(conn, "INSERT INTO heartbeat (at_utc) VALUES ($now)", DateTime.UtcNow.ToString("O"));
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM heartbeat";
                var n = (long)cmd.ExecuteScalar()!;
                Console.WriteLine($"worker-heartbeat: ok, beats={n}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"worker-heartbeat: FAIL {ex.Message.Split('\n')[0]}");
            }
            await Task.Delay(TimeSpan.FromSeconds(30));
        }

    default:
        Console.WriteLine("usage: Astronomy.Worker <heartbeat|migrate|probe|fixtures|compare|naif>");
        return 1;
}
return 0;

static SqliteConnection Open(string db, bool readOnly)
{
    var conn = new SqliteConnection($"Data Source={db}{(readOnly ? ";Mode=ReadOnly" : "")}");
    conn.Open();
    return conn;
}

static void Exec(SqliteConnection conn, string sql, string? p = null)
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    if (p != null) cmd.Parameters.AddWithValue("$now", p);
    cmd.ExecuteNonQuery();
}

static async Task ProbeAsync()
{
    var targets = new (string Name, string Url)[]
    {
        ("jpl-ssd", "https://ssd.jpl.nasa.gov/"),
        ("jpl-naif", "https://naif.jpl.nasa.gov/"),
        ("usno-ser7", "https://maia.usno.navy.mil/ser7/ser7.dat"),
        ("celestrak", "https://celestrak.org/"),
        ("cds", "https://cdsarc.cds.unistra.fr/"),
    };
    using var hc = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
    foreach (var (name, url) in targets)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var resp = await hc.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            Console.WriteLine($"probe: {name,-12} {url,-50} HTTP {(int)resp.StatusCode} in {sw.Elapsed.TotalSeconds:F1}s");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"probe: {name,-12} {url,-50} FAIL {ex.Message.Split('\n')[0]} in {sw.Elapsed.TotalSeconds:F1}s");
        }
    }
}
