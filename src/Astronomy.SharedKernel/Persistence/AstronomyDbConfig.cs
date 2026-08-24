namespace Astronomy.SharedKernel.Persistence;

public enum AstronomyDbProvider
{
    Sqlite,
    Postgres,
}

/// <summary>
/// Resolved database configuration for this process. Provider is authoritative:
/// the connection string is only populated for Postgres, the path only for SQLite.
/// </summary>
public sealed record AstronomyDbConfig
{
    public const string DefaultSqlitePath = "/data/astronomy.db";

    public AstronomyDbProvider Provider { get; init; } = AstronomyDbProvider.Sqlite;
    public string? ConnectionString { get; init; }
    public string SqlitePath { get; init; } = DefaultSqlitePath;

    public bool IsPostgres => Provider == AstronomyDbProvider.Postgres;

    /// <summary>
    /// Reads ASTRONOMY_DB_PROVIDER / ASTRONOMY_DB_CONNECTION / ASTRONOMY_DB_PATH.
    /// Unknown providers and a missing connection string for postgres fail fast.
    /// </summary>
    public static AstronomyDbConfig FromEnvironment() => FromValues(
        Environment.GetEnvironmentVariable("ASTRONOMY_DB_PROVIDER"),
        Environment.GetEnvironmentVariable("ASTRONOMY_DB_CONNECTION"),
        Environment.GetEnvironmentVariable("ASTRONOMY_DB_PATH"));

    /// <summary>
    /// Pure parsing of the three raw values; environment-independent for testability.
    /// Whitespace-only values are treated as unset. The provider wins when both a
    /// connection string and a path are supplied.
    /// </summary>
    public static AstronomyDbConfig FromValues(string? provider, string? connectionString, string? sqlitePath)
    {
        var providerName = string.IsNullOrWhiteSpace(provider) ? null : provider.Trim().ToLowerInvariant();
        var resolved = providerName switch
        {
            null or "" or "sqlite" => AstronomyDbProvider.Sqlite,
            "postgres" or "postgresql" => AstronomyDbProvider.Postgres,
            var other => throw new ArgumentException(
                $"Unsupported ASTRONOMY_DB_PROVIDER '{other}'. Valid values: 'sqlite', 'postgres'.", nameof(provider)),
        };

        if (resolved == AstronomyDbProvider.Postgres && string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException(
                "ASTRONOMY_DB_CONNECTION is required when ASTRONOMY_DB_PROVIDER=postgres.", nameof(connectionString));

        return new AstronomyDbConfig
        {
            Provider = resolved,
            ConnectionString = resolved == AstronomyDbProvider.Postgres ? connectionString!.Trim() : null,
            SqlitePath = string.IsNullOrWhiteSpace(sqlitePath) ? DefaultSqlitePath : sqlitePath.Trim(),
        };
    }
}
