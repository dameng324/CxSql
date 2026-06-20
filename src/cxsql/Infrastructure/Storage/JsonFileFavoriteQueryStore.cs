using CxSql.Models;

namespace CxSql.Infrastructure.Storage;

public sealed class JsonFileFavoriteQueryStore(string filePath) : IFavoriteQueryStore
{
    private readonly JsonFileListStore<FavoriteQuery> store = new(
        filePath,
        CxSqlJsonContext.Default.ListFavoriteQuery
    );

    public Task<IReadOnlyList<FavoriteQuery>> LoadAsync(CancellationToken cancellationToken)
    {
        return store.LoadAsync(cancellationToken);
    }

    public Task SaveAsync(
        IReadOnlyList<FavoriteQuery> favorites,
        CancellationToken cancellationToken
    )
    {
        return store.SaveAsync(favorites, cancellationToken);
    }
}
