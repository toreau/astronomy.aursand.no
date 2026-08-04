using System.Diagnostics;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace S09Sqlite;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: S09Sqlite <init|writer|reader|migrate|backup|restore|enforce|test> <db> [...]"); return 1; }
        var mode = args[0];
        var db = args[1];
        return mode switch
        {
            "init" => Init(db),
            "writer" => Writer(db, int.Parse(args[2])),
            "reader" => Reader(db, int.Parse(args[2])),
            "migrate" => Migrate(db),
            "backup" => Backup(db, args[2]),
            "restore" => Restore(db, args[2]),
            "enforce" => Enforce(db),
            "test" => Test(db),
            _ => 1,
        };
    }

    private static string Cs(string db, bool readOnly = false) =>
        $"Data Source={db}{(readOnly ? ";Mode=ReadOnly" : "")}";

    private static SqliteConnection OpenRaw(string db, bool readOnly, bool wal)
    {
        var conn = new SqliteConnection(Cs(db, readOnly));
        conn.Open();
        if (wal && !readOnly)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
            cmd.ExecuteNonQuery();
        }
        return conn;
    }

    private static DrillDbContext Context(string db)
    {
        var conn = OpenRaw(db, readOnly: false, wal: true);
        var options = new DbContextOptionsBuilder<DrillDbContext>().UseSqlite(conn).Options;
        return new DrillDbContext(options);
    }

    private static int Init(string db)
    {
        if (File.Exists(db)) File.Delete(db);
        using var ctx = Context(db);
        ctx.Database.Migrate();
        ctx.Datasets.Add(new Dataset { Name = "eop-ut1", Version = "2026-08-04", Status = "active", ActivatedAtUtc = DateTime.UtcNow, Checksum = "abc123" });
        ctx.SaveChanges();
        Console.WriteLine($"init: migrations applied, {ctx.Datasets.Count()} datasets");
        return 0;
    }

    private static int Writer(string db, int durationSeconds)
    {
        using var ctx = Context(db);
        var sw = Stopwatch.StartNew();
        var batch = 0;
        while (sw.Elapsed.TotalSeconds < durationSeconds)
        {
            for (var i = 0; i < 1000; i++)
            {
                ctx.SatelliteElements.Add(new SatelliteElement
                {
                    NoradId = (25544 + batch).ToString(),
                    EpochUtc = DateTime.UtcNow,
                    DatasetVersion = "v" + batch,
                    ElementsJson = $"{{\"batch\":{batch},\"i\":{i}}}",
                });
            }
            ctx.SaveChanges();
            batch++;
            Thread.Sleep(80);
        }
        Console.WriteLine($"writer: inserted {batch * 1000} rows in {sw.Elapsed.TotalSeconds:F1}s ({batch} batches)");
        return 0;
    }

    private static int Reader(string db, int durationSeconds)
    {
        var latencies = new List<double>();
        long lastCount = -1;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < durationSeconds)
        {
            using var conn = OpenRaw(db, readOnly: true, wal: false);
            var t = Stopwatch.StartNew();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM SatelliteElements";
                lastCount = (long)cmd.ExecuteScalar()!;
            }
            t.Stop();
            latencies.Add(t.Elapsed.TotalMilliseconds * 1000);
            Thread.Sleep(10);
        }
        latencies.Sort();
        Console.WriteLine($"reader: N={latencies.Count} rows_seen={lastCount} mean={latencies.Average() / 1000:F3} ms p95={latencies[(int)(0.95 * latencies.Count)] / 1000:F3} ms max={latencies[^1] / 1000:F3} ms");
        return 0;
    }

    private static int Migrate(string db)
    {
        using var ctx = Context(db);
        ctx.Database.Migrate();
        var migrations = ctx.Database.GetAppliedMigrations().ToArray();
        Console.WriteLine($"migrate: applied {migrations.Length} migrations: {string.Join(", ", migrations)}");
        return 0;
    }

    private static int Backup(string db, string dest)
    {
        using var source = OpenRaw(db, readOnly: true, wal: false);
        using var target = new SqliteConnection(Cs(dest));
        source.BackupDatabase(target);
        Console.WriteLine($"backup: {db} -> {dest} ({new FileInfo(dest).Length} bytes)");
        return 0;
    }

    private static int Restore(string db, string src)
    {
        if (File.Exists(db)) File.Delete(db);
        File.Copy(src, db);
        using var conn = OpenRaw(db, readOnly: false, wal: true);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM SatelliteElements";
        var n = (long)cmd.ExecuteScalar()!;
        Console.WriteLine($"restore: {src} -> {db}, {n} satellite elements present");
        return 0;
    }

    private static int Enforce(string db)
    {
        try
        {
            using var conn = OpenRaw(db, readOnly: true, wal: false);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO SatelliteElements (NoradId, EpochUtc, DatasetVersion, ElementsJson) VALUES ('1', '2026-01-01', 'v0', '{}')";
            cmd.ExecuteNonQuery();
            Console.WriteLine("enforce: FAIL - write succeeded on read-only connection");
            return 1;
        }
        catch (SqliteException ex)
        {
            Console.WriteLine($"enforce: PASS - write rejected: {ex.Message.Split('\n')[0]}");
            return 0;
        }
    }

    private static int Test(string db)
    {
        var rc = 0;
        rc |= Init(db);
        rc |= Migrate(db);
        rc |= Enforce(db);
        rc |= Writer(db, 3);
        rc |= Reader(db, 2);
        var backup = db + ".backup";
        rc |= Backup(db, backup);
        File.WriteAllText(db, "corrupted garbage".PadRight(4096, 'x'));
        rc |= Restore(db, backup);
        rc |= Reader(db, 1);
        rc |= Enforce(db);
        Console.WriteLine(rc == 0 ? "test: ALL GREEN" : $"test: failures rc={rc}");
        return rc;
    }
}
