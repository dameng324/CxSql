namespace CxSql.Models;

public sealed class QueryResult
{
    public List<QueryColumn> Columns { get; init; } = [];

    public List<QueryRow> Rows { get; init; } = [];

    public List<SqlExecutionMessage> Messages { get; init; } = [];

    public long ElapsedMilliseconds { get; init; }

    public int? AffectedRows { get; init; }

    public bool Success { get; init; } = true;

    public string? ErrorMessage { get; init; }

    public string? ProviderErrorCode { get; init; }

    public static QueryResult Failure(
        string message,
        long elapsedMilliseconds,
        string? providerErrorCode = null
    )
    {
        return new QueryResult
        {
            Success = false,
            ErrorMessage = message,
            ProviderErrorCode = providerErrorCode,
            ElapsedMilliseconds = elapsedMilliseconds,
            Messages =
            [
                new SqlExecutionMessage
                {
                    Level = SqlExecutionMessageLevel.Error,
                    Message = message,
                },
            ],
        };
    }
}
