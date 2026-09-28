using CxSql.Database.Providers;
using CxSql.Models;
using Testcontainers.PostgreSql;
using TUnit.Core;

namespace CxSql.IntegrationTests;

public sealed class PostgreSqlProviderTests
{
    [Test, NotInParallel]
    public async Task ReadsObjectsDetailsAndQueryResults()
    {
        await using var server = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await server.StartAsync();

        var provider = new PostgreSqlProvider();
        await using var connection = provider.CreateConnection(server.GetConnectionString());
        await connection.OpenAsync();

        await ExecuteAsync(
            "CREATE TABLE public.people (id integer PRIMARY KEY, name text NOT NULL)"
        );
        await ExecuteAsync("CREATE UNIQUE INDEX ix_people_name ON public.people (name)");
        await ExecuteAsync(
            "CREATE FUNCTION public.people_upper() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN NEW.name := upper(NEW.name); RETURN NEW; END; $$"
        );
        await ExecuteAsync(
            "CREATE TRIGGER people_name_guard BEFORE INSERT ON public.people FOR EACH ROW EXECUTE FUNCTION public.people_upper()"
        );
        await ExecuteAsync("CREATE VIEW public.people_view AS SELECT id, name FROM public.people");
        await ExecuteAsync("INSERT INTO public.people (id, name) VALUES (1, 'Ada')");

        var objects = await provider.GetDatabaseObjectsAsync(connection, CancellationToken.None);
        var table = objects.Single(item =>
            item.Schema == "public"
            && item.Name == "people"
            && item.ObjectType == DatabaseObjectType.Table
        );
        if (
            !objects.Any(item =>
                item.Schema == "public"
                && item.Name == "people_view"
                && item.ObjectType == DatabaseObjectType.View
            )
        )
        {
            throw new InvalidOperationException("Expected PostgreSQL view metadata.");
        }

        var columns = await provider.GetColumnsAsync(connection, table, CancellationToken.None);
        if (!columns.Any(column => column.Name == "name" && !column.IsNullable))
        {
            throw new InvalidOperationException("Expected PostgreSQL column metadata.");
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
                "Expected PostgreSQL index, constraint, and trigger metadata."
            );
        }

        var result = await provider.ExecuteSqlAsync(
            connection,
            provider.BuildPreviewSql(table, 10),
            CancellationToken.None
        );
        if (result.Rows.Count != 1 || result.Rows[0].Values[1] != "ADA")
        {
            throw new InvalidOperationException("Expected PostgreSQL query result.");
        }

        async Task ExecuteAsync(string sql)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
    }
}
