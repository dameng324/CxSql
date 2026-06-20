using CxSql.Database.Providers;
using CxSql.Models;
using TUnit.Core;

namespace CxSql.Tests;

public sealed class SqliteProviderTests
{
    [Test]
    public async Task SqliteProviderReadsMetadataAndQueryResults()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            "cxsql-tests",
            $"{Guid.NewGuid():N}.db"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        var provider = new SqliteProvider();
        await using var connection = provider.CreateConnection($"Data Source={databasePath}");
        await connection.OpenAsync(CancellationToken.None);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE people (id INTEGER PRIMARY KEY, name TEXT NOT NULL);
                CREATE UNIQUE INDEX ix_people_name ON people (name);
                CREATE TRIGGER people_name_guard
                BEFORE INSERT ON people
                WHEN NEW.name IS NULL
                BEGIN
                    SELECT RAISE(ABORT, 'name required');
                END;
                INSERT INTO people (name) VALUES ('Ada'), ('Grace');
                """;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        var objects = await provider.GetDatabaseObjectsAsync(connection, CancellationToken.None);
        if (
            !objects.Any(databaseObject =>
                databaseObject.ObjectType == DatabaseObjectType.Table
                && databaseObject.Name == "people"
            )
        )
        {
            throw new InvalidOperationException("Expected people table in SQLite metadata.");
        }

        var peopleTable = objects.Single(databaseObject =>
            databaseObject.ObjectType == DatabaseObjectType.Table && databaseObject.Name == "people"
        );
        var columns = await provider.GetColumnsAsync(
            connection,
            peopleTable,
            CancellationToken.None
        );
        if (!columns.Any(column => column.Name == "name" && column.DataType == "TEXT"))
        {
            throw new InvalidOperationException("Expected SQLite column metadata.");
        }

        var details = await provider.GetObjectDetailsAsync(
            connection,
            peopleTable,
            CancellationToken.None
        );
        if (!details.Ddl.Contains("CREATE TABLE people", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Expected SQLite DDL.");
        }

        if (!details.Indexes.Any(index => index.Name == "ix_people_name" && index.IsUnique))
        {
            throw new InvalidOperationException("Expected SQLite index metadata.");
        }

        if (!details.Triggers.Any(trigger => trigger.Name == "people_name_guard"))
        {
            throw new InvalidOperationException("Expected SQLite trigger metadata.");
        }

        var result = await provider.ExecuteSqlAsync(
            connection,
            "SELECT id, name FROM people ORDER BY id;",
            CancellationToken.None
        );

        if (result.Columns.Count != 2)
        {
            throw new InvalidOperationException("Expected two result columns.");
        }

        if (result.Rows.Count != 2 || result.Rows[0].Values[1] != "Ada")
        {
            throw new InvalidOperationException("Expected query rows to be loaded.");
        }
    }
}
