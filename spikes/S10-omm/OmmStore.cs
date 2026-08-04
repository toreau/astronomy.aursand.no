using Microsoft.Data.Sqlite;

namespace S10Omm;

public static class OmmStore
{
    public static string Cs(string db) => $"Data Source={db}";

    public static int Init(string db)
    {
        if (File.Exists(db)) File.Delete(db);
        using var conn = new SqliteConnection(Cs(db));
        conn.Open();
        Exec(conn, "PRAGMA journal_mode=WAL;");
        Exec(conn, """
            CREATE TABLE datasets (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                version TEXT NOT NULL,
                status TEXT NOT NULL,
                activated_at_utc TEXT,
                checksum TEXT,
                created_at_utc TEXT NOT NULL,
                UNIQUE(name, version));
            CREATE TABLE active_datasets (
                name TEXT PRIMARY KEY,
                version TEXT NOT NULL,
                activated_at_utc TEXT NOT NULL);
            CREATE TABLE satellite_elements (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                dataset_version TEXT NOT NULL,
                norad_id TEXT NOT NULL,
                object_name TEXT,
                epoch_utc TEXT NOT NULL,
                elements_json TEXT NOT NULL);
            CREATE INDEX ix_elements_version ON satellite_elements(dataset_version);
            CREATE TABLE audit (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                action TEXT NOT NULL,
                detail TEXT NOT NULL,
                at_utc TEXT NOT NULL);
            """);
        Console.WriteLine($"init: schema created at {db}");
        return 0;
    }

    public static void Stage(string db, string version, string sourceLabel, string payload, List<OmmRow> rows)
    {
        using var conn = new SqliteConnection(Cs(db));
        conn.Open();
        using var tx = conn.BeginTransaction();
        var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO datasets (name, version, status, checksum, created_at_utc)
            VALUES ('satellite-elements', $v, 'staged', $chk, $now)
            """;
        cmd.Parameters.AddWithValue("$v", version);
        cmd.Parameters.AddWithValue("$chk", Sha256(payload));
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();

        var ins = conn.CreateCommand();
        ins.Transaction = tx;
        ins.CommandText = """
            INSERT INTO satellite_elements (dataset_version, norad_id, object_name, epoch_utc, elements_json)
            VALUES ($v, $n, $o, $e, $j)
            """;
        foreach (var r in rows)
        {
            ins.Parameters.Clear();
            ins.Parameters.AddWithValue("$v", version);
            ins.Parameters.AddWithValue("$n", r.NoradId);
            ins.Parameters.AddWithValue("$o", r.Name);
            ins.Parameters.AddWithValue("$e", r.EpochUtc.ToString("O"));
            ins.Parameters.AddWithValue("$j", r.ToJson());
            ins.ExecuteNonQuery();
        }
        Exec(conn, $"INSERT INTO audit (action, detail, at_utc) VALUES ('stage', '{version}: {rows.Count} rows from {sourceLabel}', '{DateTime.UtcNow:O}')", tx);
        tx.Commit();
        Console.WriteLine($"stage: version {version}, {rows.Count} rows ({sourceLabel})");
    }

    public static void Activate(string db, string version)
    {
        using var conn = new SqliteConnection(Cs(db));
        conn.Open();
        using var tx = conn.BeginTransaction();
        Exec(conn, "UPDATE datasets SET status='active', activated_at_utc=$now WHERE name='satellite-elements' AND version=$v",
            tx, ("$v", version), ("$now", DateTime.UtcNow.ToString("O")));
        Exec(conn, """
            INSERT INTO active_datasets (name, version, activated_at_utc)
            VALUES ('satellite-elements', $v, $now)
            ON CONFLICT(name) DO UPDATE SET version=$v, activated_at_utc=$now
            """, tx, ("$v", version), ("$now", DateTime.UtcNow.ToString("O")));
        Exec(conn, $"INSERT INTO audit (action, detail, at_utc) VALUES ('activate', '{version}', '{DateTime.UtcNow:O}')", tx);
        tx.Commit();
        Console.WriteLine($"activate: {version} now active (older versions purged)");
    }

    public static string? ActiveVersion(string db)
    {
        using var conn = new SqliteConnection(Cs(db));
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT version FROM active_datasets WHERE name='satellite-elements'";
        return cmd.ExecuteScalar() as string;
    }

    public static int Rollback(string db, string version)
    {
        using var conn = new SqliteConnection(Cs(db));
        conn.Open();
        using var tx = conn.BeginTransaction();
        Exec(conn, "UPDATE datasets SET status='active', activated_at_utc=$now WHERE name='satellite-elements' AND version=$v",
            tx, ("$v", version), ("$now", DateTime.UtcNow.ToString("O")));
        Exec(conn, """
            INSERT INTO active_datasets (name, version, activated_at_utc)
            VALUES ('satellite-elements', $v, $now)
            ON CONFLICT(name) DO UPDATE SET version=$v, activated_at_utc=$now
            """, tx, ("$v", version), ("$now", DateTime.UtcNow.ToString("O")));
        Exec(conn, $"INSERT INTO audit (action, detail, at_utc) VALUES ('rollback', '{version}', '{DateTime.UtcNow:O}')", tx);
        tx.Commit();
        Console.WriteLine($"rollback: {version} restored as active (stale rows remain in table)");
        return 0;
    }

    public static List<OmmRow> ReadElements(string db)
    {
        using var conn = new SqliteConnection(Cs(db));
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT e.norad_id, e.object_name, e.epoch_utc, e.elements_json FROM satellite_elements e
            JOIN active_datasets a ON a.name='satellite-elements' AND a.version = e.dataset_version
            """;
        using var r = cmd.ExecuteReader();
        var list = new List<OmmRow>();
        while (r.Read())
            list.Add(OmmIngest.FromJson(r.GetString(3), r.GetString(1)));
        return list;
    }

    public static List<(string NoradId, DateTime EpochUtc, string Name)> Elements(string db)
    {
        using var conn = new SqliteConnection(Cs(db));
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT e.norad_id, e.epoch_utc, e.object_name FROM satellite_elements e
            JOIN active_datasets a ON a.name='satellite-elements' AND a.version = e.dataset_version
            """;
        using var r = cmd.ExecuteReader();
        var list = new List<(string, DateTime, string)>();
        while (r.Read())
            list.Add((r.GetString(0), DateTime.Parse(r.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind), r.GetString(2)));
        return list;
    }

    private static void Exec(SqliteConnection conn, string sql, SqliteTransaction? tx = null, params (string, string)[] parms)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (k, v) in parms) cmd.Parameters.AddWithValue(k, v);
        cmd.ExecuteNonQuery();
    }

    public static string Sha256(string s)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
