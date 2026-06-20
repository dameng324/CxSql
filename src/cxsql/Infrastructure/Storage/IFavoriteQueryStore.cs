using CxSql.Models;

namespace CxSql.Infrastructure.Storage;

public interface IFavoriteQueryStore
{
    Task<IReadOnlyList<FavoriteQuery>> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(IReadOnlyList<FavoriteQuery> favorites, CancellationToken cancellationToken);
}
