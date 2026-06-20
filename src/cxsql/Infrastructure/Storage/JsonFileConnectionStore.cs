using CxSql.Models;

namespace CxSql.Infrastructure.Storage;

public sealed class JsonFileConnectionStore(string filePath) : IConnectionStore
{
    private readonly JsonFileListStore<DatabaseConnection> store = new(
        filePath,
        CxSqlJsonContext.Default.ListDatabaseConnection
    );

    public Task<IReadOnlyList<DatabaseConnection>> LoadAsync(CancellationToken cancellationToken)
    {
        return store.LoadAsync(cancellationToken);
    }

    public Task SaveAsync(
        IReadOnlyList<DatabaseConnection> connections,
        CancellationToken cancellationToken
    )
    {
        return store.SaveAsync(connections, cancellationToken);
    }
}
