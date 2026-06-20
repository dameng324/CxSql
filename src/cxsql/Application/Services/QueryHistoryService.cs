using CxSql.Infrastructure.Storage;
using CxSql.Models;

namespace CxSql.Application.Services;

public sealed class QueryHistoryService(IQueryHistoryStore queryHistoryStore)
{
    private const int MaxHistoryEntries = 500;

    public Task<IReadOnlyList<QueryHistory>> ListAsync(CancellationToken cancellationToken)
    {
        return queryHistoryStore.LoadAsync(cancellationToken);
    }

    public async Task RecordAsync(QueryHistory entry, CancellationToken cancellationToken)
    {
        var entries = (await queryHistoryStore.LoadAsync(cancellationToken)).ToList();
        entries.Insert(0, entry);

        if (entries.Count > MaxHistoryEntries)
        {
            entries.RemoveRange(MaxHistoryEntries, entries.Count - MaxHistoryEntries);
        }

        await queryHistoryStore.SaveAsync(entries, cancellationToken);
    }
}
