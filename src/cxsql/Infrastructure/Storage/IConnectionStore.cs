using CxSql.Models;

namespace CxSql.Infrastructure.Storage;

public interface IConnectionStore
{
    Task<IReadOnlyList<DatabaseConnection>> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(
        IReadOnlyList<DatabaseConnection> connections,
        CancellationToken cancellationToken
    );
}
