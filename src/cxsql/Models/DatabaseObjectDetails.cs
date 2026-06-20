namespace CxSql.Models;

public sealed class DatabaseObjectDetails
{
    public DatabaseObject DatabaseObject { get; init; } = new();

    public IReadOnlyList<DatabaseColumn> Columns { get; init; } = [];

    public IReadOnlyList<DatabaseIndex> Indexes { get; init; } = [];

    public IReadOnlyList<DatabaseTrigger> Triggers { get; init; } = [];

    public IReadOnlyList<DatabaseConstraint> Constraints { get; init; } = [];

    public string Ddl { get; init; } = string.Empty;
}

public sealed class DatabaseIndex
{
    public string Name { get; init; } = string.Empty;

    public bool IsUnique { get; init; }

    public string? Definition { get; init; }
}

public sealed class DatabaseTrigger
{
    public string Name { get; init; } = string.Empty;

    public string? Event { get; init; }

    public string? Definition { get; init; }
}

public sealed class DatabaseConstraint
{
    public string Name { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string? Definition { get; init; }
}
