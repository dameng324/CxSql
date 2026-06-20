using CxSql.Database.Providers;
using CxSql.Models;

namespace CxSql.Application.Services;

public sealed class DatabaseProviderRegistry
{
    private readonly IReadOnlyDictionary<DatabaseType, IDatabaseProvider> providers;

    public DatabaseProviderRegistry(
        IEnumerable<(DatabaseType Type, IDatabaseProvider Provider)> providers
    )
    {
        this.providers = providers.ToDictionary(item => item.Type, item => item.Provider);
    }

    public static DatabaseProviderRegistry CreateDefault()
    {
        return new DatabaseProviderRegistry([
            (DatabaseType.Sqlite, new SqliteProvider()),
            (DatabaseType.PostgreSql, new PostgreSqlProvider()),
            (DatabaseType.SqlServer, new SqlServerProvider()),
        ]);
    }

    public IDatabaseProvider GetProvider(DatabaseType databaseType)
    {
        return providers.TryGetValue(databaseType, out var provider)
            ? provider
            : throw new InvalidOperationException($"No provider registered for {databaseType}.");
    }

    public IPreviewSqlBuilder GetPreviewSqlBuilder(DatabaseType databaseType)
    {
        var provider = GetProvider(databaseType);
        return provider as IPreviewSqlBuilder
            ?? throw new InvalidOperationException(
                $"Provider {provider.ProviderName} does not support preview SQL."
            );
    }
}
