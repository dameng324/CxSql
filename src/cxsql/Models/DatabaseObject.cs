namespace CxSql.Models;

public sealed class DatabaseObject
{
    public string Name { get; init; } = string.Empty;

    public string? Schema { get; init; }

    public DatabaseObjectType ObjectType { get; init; }

    public string? ParentName { get; init; }

    public string DisplayName => string.IsNullOrWhiteSpace(Schema) ? Name : $"{Schema}.{Name}";
}
