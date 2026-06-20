using CxSql.Models;

namespace CxSql.Infrastructure.Storage;

public interface IQueryHistoryStore
{
    Task<IReadOnlyList<QueryHistory>> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(IReadOnlyList<QueryHistory> entries, CancellationToken cancellationToken);
}
