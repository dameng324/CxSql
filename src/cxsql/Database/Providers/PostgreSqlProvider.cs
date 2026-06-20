using System.Data.Common;
using CxSql.Models;
using Npgsql;

namespace CxSql.Database.Providers;

public sealed class PostgreSqlProvider : DatabaseProviderBase
{
    public override string ProviderName => "PostgreSQL";

    public override DbConnection CreateConnection(string connectionString)
    {
        return new NpgsqlConnection(connectionString);
    }

    public override async Task<IReadOnlyList<DatabaseObject>> GetDatabaseObjectsAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            SELECT n.nspname AS schema_name,
                   NULL::text AS object_name,
                   'schema' AS object_type,
                   NULL::text AS parent_name
            FROM pg_namespace n
            WHERE n.nspname NOT LIKE 'pg_%'
              AND n.nspname <> 'information_schema'
            UNION ALL
            SELECT schemaname, tablename, 'table', schemaname
            FROM pg_tables
            WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
            UNION ALL
            SELECT schemaname, viewname, 'view', schemaname
            FROM pg_views
            WHERE schemaname NOT IN ('pg_catalog', 'information_schema')
            UNION ALL
            SELECT n.nspname, p.proname, 'function', n.nspname
            FROM pg_proc p
            JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname NOT LIKE 'pg_%'
              AND n.nspname <> 'information_schema'
            ORDER BY 1, 3, 2
            """;

        return await ReadObjectsAsync(
            connection,
            sql,
            reader =>
            {
                var objectType = reader.GetString(2);
                return new DatabaseObject
                {
                    Schema = objectType == "schema" ? null : reader.GetString(0),
                    Name = objectType == "schema" ? reader.GetString(0) : reader.GetString(1),
                    ObjectType = objectType switch
                    {
                        "schema" => DatabaseObjectType.Schema,
                        "table" => DatabaseObjectType.Table,
                        "view" => DatabaseObjectType.View,
                        _ => DatabaseObjectType.Function,
                    },
                    ParentName = reader.IsDBNull(3) ? null : reader.GetString(3),
                };
            },
            cancellationToken
        );
    }

    public override async Task<IReadOnlyList<DatabaseColumn>> GetColumnsAsync(
        DbConnection connection,
        DatabaseObject databaseObject,
        CancellationToken cancellationToken
    )
    {
        if (
            databaseObject.ObjectType
            is not DatabaseObjectType.Table
                and not DatabaseObjectType.View
        )
        {
            return [];
        }

        const string sql = """
            SELECT column_name,
                   data_type,
                   is_nullable,
                   ordinal_position
            FROM information_schema.columns
            WHERE table_schema = @schema
              AND table_name = @table
            ORDER BY ordinal_position
            """;

        var schema = string.IsNullOrWhiteSpace(databaseObject.Schema)
            ? "public"
            : databaseObject.Schema;

        return await ReadColumnsAsync(
            connection,
            sql,
            command =>
            {
                var schemaParameter = command.CreateParameter();
                schemaParameter.ParameterName = "@schema";
                schemaParameter.Value = schema;
                command.Parameters.Add(schemaParameter);

                var tableParameter = command.CreateParameter();
                tableParameter.ParameterName = "@table";
                tableParameter.Value = databaseObject.Name;
                command.Parameters.Add(tableParameter);
            },
            reader => new DatabaseColumn
            {
                Name = reader.GetString(0),
                TableName = databaseObject.Name,
                Schema = schema,
                DataType = reader.IsDBNull(1) ? null : reader.GetString(1),
                IsNullable = string.Equals(reader.GetString(2), "YES", StringComparison.Ordinal),
                Ordinal = reader.GetInt32(3),
            },
            cancellationToken
        );
    }

    public override async Task<DatabaseObjectDetails> GetObjectDetailsAsync(
        DbConnection connection,
        DatabaseObject databaseObject,
        CancellationToken cancellationToken
    )
    {
        var columns = await GetColumnsAsync(connection, databaseObject, cancellationToken);
        var schema = string.IsNullOrWhiteSpace(databaseObject.Schema)
            ? "public"
            : databaseObject.Schema;

        var indexes = new List<DatabaseIndex>();
        await EnsureOpenAsync(connection, cancellationToken);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT indexname, indexdef
                FROM pg_indexes
                WHERE schemaname = @schema
                  AND tablename = @table
                ORDER BY indexname
                """;
            AddParameter(command, "@schema", schema);
            AddParameter(command, "@table", databaseObject.Name);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var definition = reader.IsDBNull(1) ? null : reader.GetString(1);
                indexes.Add(
                    new DatabaseIndex
                    {
                        Name = reader.GetString(0),
                        Definition = definition,
                        IsUnique =
                            definition?.Contains(" UNIQUE ", StringComparison.OrdinalIgnoreCase)
                            == true,
                    }
                );
            }
        }

        var constraints = new List<DatabaseConstraint>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT constraint_name, constraint_type
                FROM information_schema.table_constraints
                WHERE table_schema = @schema
                  AND table_name = @table
                ORDER BY constraint_name
                """;
            AddParameter(command, "@schema", schema);
            AddParameter(command, "@table", databaseObject.Name);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                constraints.Add(
                    new DatabaseConstraint
                    {
                        Name = reader.GetString(0),
                        Type = reader.GetString(1),
                    }
                );
            }
        }

        var triggers = new List<DatabaseTrigger>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT trigger_name, event_manipulation, action_statement
                FROM information_schema.triggers
                WHERE event_object_schema = @schema
                  AND event_object_table = @table
                ORDER BY trigger_name
                """;
            AddParameter(command, "@schema", schema);
            AddParameter(command, "@table", databaseObject.Name);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                triggers.Add(
                    new DatabaseTrigger
                    {
                        Name = reader.GetString(0),
                        Event = reader.IsDBNull(1) ? null : reader.GetString(1),
                        Definition = reader.IsDBNull(2) ? null : reader.GetString(2),
                    }
                );
            }
        }

        return new DatabaseObjectDetails
        {
            DatabaseObject = databaseObject,
            Columns = columns,
            Indexes = indexes,
            Constraints = constraints,
            Triggers = triggers,
            Ddl = BuildCreateTableSketch(databaseObject, columns),
        };
    }

    public override string BuildPreviewSql(DatabaseObject databaseObject, int rowLimit)
    {
        if (
            databaseObject.ObjectType
            is not DatabaseObjectType.Table
                and not DatabaseObjectType.View
        )
        {
            throw new InvalidOperationException("Only tables and views can be previewed.");
        }

        var name = string.IsNullOrWhiteSpace(databaseObject.Schema)
            ? QuoteDouble(databaseObject.Name)
            : $"{QuoteDouble(databaseObject.Schema)}.{QuoteDouble(databaseObject.Name)}";

        return $"SELECT * FROM {name} LIMIT {rowLimit};";
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
