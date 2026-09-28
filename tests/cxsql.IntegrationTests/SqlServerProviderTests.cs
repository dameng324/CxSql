using CxSql.Database.Providers;
using CxSql.Models;
using Testcontainers.MsSql;
using TUnit.Core;

namespace CxSql.IntegrationTests;

public sealed class SqlServerProviderTests
{
    [Test, NotInParallel]
    public async Task ReadsObjectsDetailsAndQueryResults()
    {
        await using var server = new MsSqlBuilder(
            "mcr.microsoft.com/mssql/server:2022-latest"
        ).Build();
        await server.StartAsync();

        var provider = new SqlServerProvider();
        await using var connection = provider.CreateConnection(server.GetConnectionString());
        await connection.OpenAsync();

        await ExecuteAsync(
            "CREATE TABLE dbo.people (id int PRIMARY KEY, name nvarchar(60) NOT NULL)"
        );
        await ExecuteAsync("CREATE UNIQUE INDEX ix_people_name ON dbo.people (name)");
        await ExecuteAsync(
            "CREATE TRIGGER dbo.people_name_guard ON dbo.people AFTER INSERT AS BEGIN SET NOCOUNT ON; END"
        );
        await ExecuteAsync("CREATE VIEW dbo.people_view AS SELECT id, name FROM dbo.people");
        await ExecuteAsync("INSERT INTO dbo.people (id, name) VALUES (1, N'Ada')");

        var objects = await provider.GetDatabaseObjectsAsync(connection, CancellationToken.None);
        var table = objects.Single(item =>
            item.Schema == "dbo"
            && item.Name == "people"
            && item.ObjectType == DatabaseObjectType.Table
        );
        if (
            !objects.Any(item =>
                item.Schema == "dbo"
                && item.Name == "people_view"
                && item.ObjectType == DatabaseObjectType.View
            )
        )
        {
            throw new InvalidOperationException("Expected SQL Server view metadata.");
        }

        var columns = await provider.GetColumnsAsync(connection, table, CancellationToken.None);
        if (!columns.Any(column => column.Name == "name" && !column.IsNullable))
        {
            throw new InvalidOperationException("Expected SQL Server column metadata.");
        }

        var details = await provider.GetObjectDetailsAsync(
            connection,
            table,
            CancellationToken.None
        );
        if (
            !details.Indexes.Any(index => index.Name == "ix_people_name" && index.IsUnique)
            || !details.Constraints.Any(item => item.Type == "PRIMARY KEY")
            || !details.Triggers.Any(item => item.Name == "people_name_guard")
        )
        {
            throw new InvalidOperationException(
                "Expected SQL Server index, constraint, and trigger metadata."
            );
        }

        var result = await provider.ExecuteSqlAsync(
            connection,
            provider.BuildPreviewSql(table, 10),
            CancellationToken.None
        );
        if (result.Rows.Count != 1 || result.Rows[0].Values[1] != "Ada")
        {
            throw new InvalidOperationException("Expected SQL Server query result.");
        }

        async Task ExecuteAsync(string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
    }
}
