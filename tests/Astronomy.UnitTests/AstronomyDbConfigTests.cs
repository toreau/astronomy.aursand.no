using Astronomy.SharedKernel.Persistence;

namespace Astronomy.UnitTests;

public class AstronomyDbConfigTests
{
    [Fact]
    public void NoEnv_DefaultsToSqlite_AndDefaultPath()
    {
        var cfg = AstronomyDbConfig.FromValues(null, null, null);

        Assert.Equal(AstronomyDbProvider.Sqlite, cfg.Provider);
        Assert.Equal(AstronomyDbConfig.DefaultSqlitePath, cfg.SqlitePath);
        Assert.Null(cfg.ConnectionString);
        Assert.False(cfg.IsPostgres);
    }

    [Fact]
    public void ExplicitSqlite_IsCaseInsensitive_AndTrimmed()
    {
        var cfg = AstronomyDbConfig.FromValues(" SQLITE ", "Host=ignored", null);

        Assert.Equal(AstronomyDbProvider.Sqlite, cfg.Provider);
    }

    [Fact]
    public void Postgres_PreservesConnectionString()
    {
        var cfg = AstronomyDbConfig.FromValues("postgres", "Host=db;Database=astronomy", null);

        Assert.Equal(AstronomyDbProvider.Postgres, cfg.Provider);
        Assert.Equal("Host=db;Database=astronomy", cfg.ConnectionString);
        Assert.True(cfg.IsPostgres);
    }

    [Fact]
    public void PostgresqlAlias_MapsToPostgres()
    {
        var cfg = AstronomyDbConfig.FromValues("postgresql", "Host=db;Database=astronomy", null);

        Assert.Equal(AstronomyDbProvider.Postgres, cfg.Provider);
    }

    [Fact]
    public void Postgres_WithoutConnectionString_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => AstronomyDbConfig.FromValues("postgres", null, null));

        Assert.Contains("ASTRONOMY_DB_CONNECTION", ex.Message);
    }

    [Fact]
    public void Postgres_WithWhitespaceConnectionString_Throws()
    {
        Assert.Throws<ArgumentException>(() => AstronomyDbConfig.FromValues("postgres", "   ", null));
    }

    [Fact]
    public void UnknownProvider_Throws_WithValidValues()
    {
        var ex = Assert.Throws<ArgumentException>(() => AstronomyDbConfig.FromValues("oracle", "Host=db", null));

        Assert.Contains("sqlite", ex.Message);
        Assert.Contains("postgres", ex.Message);
    }

    [Fact]
    public void Sqlite_IgnoresConnectionString()
    {
        var cfg = AstronomyDbConfig.FromValues("sqlite", "Host=db;Database=astronomy", null);

        Assert.Equal(AstronomyDbProvider.Sqlite, cfg.Provider);
        Assert.Null(cfg.ConnectionString);
    }

    [Fact]
    public void EmptyPath_FallsBackToDefault()
    {
        var cfg = AstronomyDbConfig.FromValues("sqlite", null, "  ");

        Assert.Equal(AstronomyDbConfig.DefaultSqlitePath, cfg.SqlitePath);
    }

    [Fact]
    public void CustomPath_IsTrimmed()
    {
        var cfg = AstronomyDbConfig.FromValues("sqlite", null, " /tmp/x.db ");

        Assert.Equal("/tmp/x.db", cfg.SqlitePath);
    }
}
