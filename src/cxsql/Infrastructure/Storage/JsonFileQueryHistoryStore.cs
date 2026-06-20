using CxSql.Models;

namespace CxSql.Infrastructure.Storage;

public sealed class JsonFileQueryHistoryStore(string filePath) : IQueryHistoryStore
{
    private readonly JsonFileListStore<QueryHistory> store = new(
        filePath,
        CxSqlJsonContext.Default.ListQueryHistory
    );

    public Task<IReadOnlyList<QueryHistory>> LoadAsync(CancellationToken cancellationToken)
    {
        return store.LoadAsync(cancellationToken);
    }

    public Task SaveAsync(IReadOnlyList<QueryHistory> entries, CancellationToken cancellationToken)
    {
        return store.SaveAsync(entries, cancellationToken);
    }
}
