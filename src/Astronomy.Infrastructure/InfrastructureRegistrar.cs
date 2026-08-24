using Astronomy.Infrastructure.Catalog;
using Astronomy.Infrastructure.Registry;
using Astronomy.SharedKernel.Datasets;
using Astronomy.SharedKernel.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.Infrastructure;

public static class InfrastructureRegistrar
{
    public static IServiceCollection AddAstronomyInfrastructure(this IServiceCollection services, AstronomyDbConfig config, string dataRoot)
    {
        services.AddSingleton<IDatasetRegistry>(_ => new DatasetRegistry(() => CreateRegistryContext(config)));
        services.AddSingleton<Astronomy.SharedKernel.Datasets.IDatasetCatalog>(sp =>
            new DatasetCatalog(sp.GetRequiredService<IDatasetRegistry>(), dataRoot));
        return services;
    }

    public static RegistryDbContext CreateRegistryContext(AstronomyDbConfig config)
    {
        if (config.IsPostgres)
            return new RegistryDbContext(new DbContextOptionsBuilder<RegistryDbContext>()
                .UseNpgsql(config.ConnectionString!).Options);
        var conn = new SqliteConnection($"Data Source={config.SqlitePath}");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
            cmd.ExecuteNonQuery();
        }
        var options = new DbContextOptionsBuilder<RegistryDbContext>().UseSqlite(conn).Options;
        return new RegistryDbContext(options);
    }

    /// <summary>
    /// Postgres: apply the Npgsql migrations. SQLite (dev/throwaway): create this
    /// context's tables idempotently. EnsureCreated is all-or-nothing per database,
    /// and the registry + satellite contexts share one SQLite file, so it would
    /// silently skip whichever context runs second - use the model's create script
    /// when the primary table is missing instead. Never use EnsureCreated for
    /// Postgres (it would conflict with a later Migrate()).
    /// </summary>
    public static void EnsureSchema(AstronomyDbConfig config)
    {
        using var ctx = CreateRegistryContext(config);
        if (config.IsPostgres)
        {
            ctx.Database.Migrate();
            return;
        }
        const string primaryTable = "Datasets";
        var exists = ctx.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*) AS Value FROM sqlite_master WHERE type='table' AND name={0}", primaryTable).First() > 0;
        if (!exists)
            ctx.Database.ExecuteSqlRaw(ctx.Database.GenerateCreateScript());
    }

    public static void BackupDatabase(AstronomyDbConfig config, string destinationPath)
    {
        if (config.IsPostgres)
            throw new NotSupportedException("backup is SQLite-only; production uses Coolify Postgres backups");
        using var source = new SqliteConnection($"Data Source={config.SqlitePath};Mode=ReadOnly");
        source.Open();
        using var target = new SqliteConnection($"Data Source={destinationPath}");
        target.Open();
        source.BackupDatabase(target);
    }
}
