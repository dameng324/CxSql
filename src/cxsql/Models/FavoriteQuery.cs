namespace CxSql.Models;

public sealed class FavoriteQuery
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string SqlText { get; set; } = string.Empty;

    public long CreatedAtUnixMs { get; set; } = UnixTime.NowMilliseconds();

    public long UpdatedAtUnixMs { get; set; } = UnixTime.NowMilliseconds();
}
