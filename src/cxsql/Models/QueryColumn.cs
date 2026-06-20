namespace CxSql.Models;

public sealed class QueryColumn
{
    public int Ordinal { get; init; }

    public string Name { get; init; } = string.Empty;

    public string DataType { get; init; } = string.Empty;
}
