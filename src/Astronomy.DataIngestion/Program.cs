using Astronomy.Infrastructure;
using Astronomy.Infrastructure.Registry;
using Astronomy.Modules.Satellites.Application;
using Microsoft.Extensions.DependencyInjection;
using Astronomy.DataIngestion;
using Astronomy.SharedKernel.Datasets;
using Microsoft.Data.Sqlite;

var dbPath = Environment.GetEnvironmentVariable("ASTRONOMY_DB_PATH") ?? "/data/astronomy.db";
var dataRoot = Environment.GetEnvironmentVariable("ASTRONOMY_DATA_ROOT") ?? "/data";
var mode = args.Length > 0 ? args[0] : "heartbeat";

switch (mode)
{
    case "migrate":
        InfrastructureRegistrar.MigrateRegistry(dbPath);
        Console.WriteLine("migrate: registry schema ok");
        break;

    case "backup":
        InfrastructureRegistrar.BackupDatabase(dbPath, args.Length > 1 ? args[1] : dbPath + ".backup");
        Console.WriteLine("backup: done");
        break;

    case "dataset":
        await DatasetCommandAsync(args[1..]);
        break;

    case "ingest":
        await IngestCommandAsync(args[1..]);
        break;

    case "omm":
        await OmmCommandAsync(args[1..]);
        break;

    case "probe":
        await HostGates.ProbeAsync();
        break;

    case "fixtures":
        return await HostGates.FetchFixturesAsync(args.Length > 1 ? args[1] : "/data/fixtures");

    case "compare":
        return HostGates.CompareFixtures(args.Length > 1 ? args[1] : "/data/fixtures");

    case "sample":
        return HostGates.SampleFixtures(args.Length > 1 ? args[1] : "/data/fixtures", args[2], int.Parse(args[3]));

    case "naif":
        return await HostGates.NaifAsync(args.Length > 1 ? args[1] : "/data/kernels");

    case "heartbeat":
        await HeartbeatAsync();
        break;

    default:
        Console.WriteLine("usage: Astronomy.DataIngestion <heartbeat|migrate|backup|dataset|ingest|omm|probe|fixtures|compare|sample|naif>");
        return 1;
}
return 0;

IDatasetRegistry Registry() => new DatasetRegistry(() => InfrastructureRegistrar.CreateRegistryContext(dbPath));

async Task<int> DatasetCommandAsync(string[] args)
{
    if (args.Length < 1) { Console.WriteLine("usage: dataset <status|activate|rollback> [version]"); return 1; }
    var registry = Registry();
    switch (args[0])
    {
        case "status":
            foreach (var name in new[] { "leap-seconds", "eop-ut1", "satellite-elements" })
            {
                var active = registry.ActiveVersion(name);
                Console.WriteLine($"dataset: {name,-18} active={active?.Version ?? "(none)"}");
            }
            return 0;
        case "activate":
            await registry.ActivateAsync(args[1], args[2]);
            Console.WriteLine($"dataset: activated {args[1]} {args[2]}");
            return 0;
        case "rollback":
            await registry.RollbackAsync(args[1], args[2]);
            Console.WriteLine($"dataset: rolled back {args[1]} to {args[2]}");
            return 0;
        default:
            Console.WriteLine($"unknown dataset op '{args[0]}'");
            return 1;
    }
}

async Task<int> IngestCommandAsync(string[] args)
{
    if (args.Length < 1) { Console.WriteLine("usage: ingest <eop|leap-seconds>"); return 1; }
    return args[0] switch
    {
        "eop" => await Jobs.RunEopJobAsync(dbPath, dataRoot),
        "leap-seconds" => await Jobs.RunLeapSecondsJobAsync(dbPath, dataRoot),
        _ => 1,
    };
}

async Task<int> OmmCommandAsync(string[] args)
{
    if (args.Length < 1) { Console.WriteLine("usage: omm <fetch|stage-file|activate|rollback|status> [version] [file]"); return 1; }
    var service = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
        .AddAstronomyInfrastructure(dbPath, dataRoot)
        .AddSatellitesModule(dbPath)
        .BuildServiceProvider()
        .GetRequiredService<ISatelliteElementIngestionService>();
    switch (args[0])
    {
        case "fetch":
            return await service.FetchAndStageAsync(args[1]);
        case "stage-file":
            return await service.StageFileAsync(args[1], args[2]);
        case "activate":
            return await service.ActivateAsync(args[1]);
        case "rollback":
            return await service.RollbackAsync(args[1]);
        case "status":
            var s = await service.GetStatusAsync();
            Console.WriteLine($"omm: active={s.ActiveVersion ?? "(none)"} elements={s.ElementCount} fresh={s.Fresh} warn={s.Warn} degraded={s.Degraded} refuse={s.Refuse}");
            return 0;
        default:
            Console.WriteLine($"unknown omm op '{args[0]}'");
            return 1;
    }
}

async Task HeartbeatAsync()
{
    try
    {
        InfrastructureRegistrar.MigrateRegistry(dbPath);
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
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE IF NOT EXISTS heartbeat (id INTEGER PRIMARY KEY, at_utc TEXT NOT NULL);";
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO heartbeat (at_utc) VALUES ($now)";
                cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O"));
                cmd.ExecuteNonQuery();
            }
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM heartbeat";
                Console.WriteLine($"worker-heartbeat: ok, beats={(long)cmd.ExecuteScalar()!}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"worker-heartbeat: FAIL {ex.Message.Split('\n')[0]}");
        }
        await Task.Delay(TimeSpan.FromSeconds(30));
    }
}
