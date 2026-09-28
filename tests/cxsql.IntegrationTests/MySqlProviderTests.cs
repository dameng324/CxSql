using CxSql.Database.Providers;
using CxSql.Models;
using CxSql.UI.Components;
using SharpConsoleUI.Controls;
using Testcontainers.MySql;
using TUnit.Core;

namespace CxSql.IntegrationTests;

public sealed class MySqlProviderTests
{
    [Test, NotInParallel]
    public async Task ReadsObjectsDetailsAndQueryResults()
    {
        await using var server = new MySqlBuilder("mysql:8.4")
            .WithCommand("--log-bin-trust-function-creators=1")
            .Build();
        await server.StartAsync();

        var provider = new MySqlProvider();
        await using var connection = provider.CreateConnection(server.GetConnectionString());
        await connection.OpenAsync();

        await ExecuteAsync("CREATE TABLE people (id INT PRIMARY KEY, name VARCHAR(60) NOT NULL)");
        await ExecuteAsync("CREATE UNIQUE INDEX ix_people_name ON people (name)");
        await ExecuteAsync(
            "CREATE TRIGGER people_name_guard BEFORE INSERT ON people FOR EACH ROW SET NEW.name = UPPER(NEW.name)"
        );
        await ExecuteAsync("CREATE VIEW people_view AS SELECT id, name FROM people");
        await ExecuteAsync("INSERT INTO people (id, name) VALUES (1, 'Ada')");

        var objects = await provider.GetDatabaseObjectsAsync(connection, CancellationToken.None);
        var table = objects.Single(item =>
            item.Name == "people" && item.ObjectType == DatabaseObjectType.Table
        );
        var view = objects.Single(item =>
            item.Name == "people_view" && item.ObjectType == DatabaseObjectType.View
        );
        if (string.IsNullOrWhiteSpace(table.Schema) || table.Schema != view.Schema)
        {
            throw new InvalidOperationException(
                "Expected MySQL objects to have a database schema."
            );
        }

        var columns = await provider.GetColumnsAsync(connection, table, CancellationToken.None);
        if (!columns.Any(column => column.Name == "name" && !column.IsNullable))
        {
            throw new InvalidOperationException("Expected MySQL column metadata.");
        }

        var details = await provider.GetObjectDetailsAsync(
            connection,
            table,
            CancellationToken.None
        );
        if (
            !details.Ddl.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase)
            || !details.Indexes.Any(index => index.Name == "ix_people_name" && index.IsUnique)
            || !details.Constraints.Any(item => item.Type == "PRIMARY KEY")
            || !details.Triggers.Any(item => item.Name == "people_name_guard")
        )
        {
            throw new InvalidOperationException(
                "Expected MySQL DDL, index, constraint, and trigger metadata."
            );
        }

        var viewDetails = await provider.GetObjectDetailsAsync(
            connection,
            view,
            CancellationToken.None
        );
        if (!viewDetails.Ddl.Contains(" VIEW ", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Expected MySQL view DDL.");
        }

        var result = await provider.ExecuteSqlAsync(
            connection,
            provider.BuildPreviewSql(table, 10),
            CancellationToken.None
        );
        if (result.Rows.Count != 1 || result.Rows[0].Values[1] != "ADA")
        {
            throw new InvalidOperationException("Expected MySQL query result.");
        }

        var filteredSql = ResultGridSqlBuilder.Build(
            DatabaseType.MySql,
            "SELECT id, name FROM people;",
            new ResultGridFilterRequest("name", ResultGridFilterOperator.Contains, "DA"),
            new ResultGridSortRequest("id", SortDirection.Descending)
        );
        var filteredResult = await provider.ExecuteSqlAsync(
            connection,
            filteredSql,
            CancellationToken.None
        );
        if (filteredResult.Rows.Count != 1 || filteredResult.Rows[0].Values[1] != "ADA")
        {
            throw new InvalidOperationException("Expected MySQL result-grid SQL to execute.");
        }

        async Task ExecuteAsync(string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
    }
}
