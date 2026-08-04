using Astronomy.Infrastructure.Catalog;
using Astronomy.Infrastructure.Registry;
using Astronomy.SharedKernel.Datasets;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Astronomy.Infrastructure;

public static class InfrastructureRegistrar
{
    public static IServiceCollection AddAstronomyInfrastructure(this IServiceCollection services, string dbPath, string dataRoot)
    {
        services.AddSingleton<IDatasetRegistry>(_ => new DatasetRegistry(() => CreateRegistryContext(dbPath)));
        services.AddSingleton<Astronomy.SharedKernel.Datasets.IDatasetCatalog>(sp =>
            new DatasetCatalog(sp.GetRequiredService<IDatasetRegistry>(), dataRoot));
        return services;
    }

    public static RegistryDbContext CreateRegistryContext(string dbPath)
    {
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
            cmd.ExecuteNonQuery();
        }
        var options = new DbContextOptionsBuilder<RegistryDbContext>().UseSqlite(conn).Options;
        return new RegistryDbContext(options);
    }

    public static void MigrateRegistry(string dbPath)
    {
        using var ctx = CreateRegistryContext(dbPath);
        ctx.Database.Migrate();
    }

    public static void BackupDatabase(string dbPath, string destinationPath)
    {
        using var source = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        source.Open();
        using var target = new SqliteConnection($"Data Source={destinationPath}");
        target.Open();
        source.BackupDatabase(target);
    }
}
