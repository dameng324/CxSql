using CxSql.Infrastructure.Storage;
using CxSql.Models;

namespace CxSql.Application.Services;

public sealed class FavoriteQueryService(IFavoriteQueryStore favoriteQueryStore)
{
    public Task<IReadOnlyList<FavoriteQuery>> ListAsync(CancellationToken cancellationToken)
    {
        return favoriteQueryStore.LoadAsync(cancellationToken);
    }

    public async Task<FavoriteQuery> SaveAsync(
        string name,
        string sqlText,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Favorite name is required.");
        }

        if (string.IsNullOrWhiteSpace(sqlText))
        {
            throw new InvalidOperationException("SQL text is required.");
        }

        var favorites = (await favoriteQueryStore.LoadAsync(cancellationToken)).ToList();
        var existing = favorites.FirstOrDefault(favorite =>
            string.Equals(favorite.Name, name, StringComparison.OrdinalIgnoreCase)
        );

        if (existing is null)
        {
            existing = new FavoriteQuery
            {
                Name = name.Trim(),
                SqlText = sqlText,
                CreatedAtUnixMs = UnixTime.NowMilliseconds(),
                UpdatedAtUnixMs = UnixTime.NowMilliseconds(),
            };
            favorites.Add(existing);
        }
        else
        {
            existing.SqlText = sqlText;
            existing.UpdatedAtUnixMs = UnixTime.NowMilliseconds();
        }

        await favoriteQueryStore.SaveAsync(favorites, cancellationToken);
        return existing;
    }
}
