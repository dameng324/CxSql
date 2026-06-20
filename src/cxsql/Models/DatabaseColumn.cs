namespace CxSql.Models;

public sealed class DatabaseColumn
{
    public string Name { get; init; } = string.Empty;

    public string TableName { get; init; } = string.Empty;

    public string? Schema { get; init; }

    public string? DataType { get; init; }

    public bool IsNullable { get; init; }

    public int Ordinal { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(Schema) ? Name : $"{Schema}.{Name}";
}
