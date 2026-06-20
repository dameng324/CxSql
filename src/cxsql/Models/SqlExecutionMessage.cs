namespace CxSql.Models;

public sealed class SqlExecutionMessage
{
    public SqlExecutionMessageLevel Level { get; init; }

    public string Message { get; init; } = string.Empty;
}

public enum SqlExecutionMessageLevel
{
    Info = 1,
    Warning = 2,
    Error = 3,
}
