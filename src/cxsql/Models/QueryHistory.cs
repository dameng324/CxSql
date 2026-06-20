namespace CxSql.Models;

public sealed class QueryHistory
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string SqlText { get; init; } = string.Empty;

    public long ExecutionElapsedMilliseconds { get; init; }

    public string ConnectionName { get; init; } = string.Empty;

    public long TimestampUnixMs { get; init; } = UnixTime.NowMilliseconds();

    public bool Succeeded { get; init; }

    public string? ErrorMessage { get; init; }
}
