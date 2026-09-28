using System.Data.Common;
using System.Globalization;
using CxSql.Models;
using MySqlConnector;

namespace CxSql.Database.Providers;

public sealed class MySqlProvider : DatabaseProviderBase
{
    public override string ProviderName => "MySQL";

    public override DbConnection CreateConnection(string connectionString)
    {
        return new MySqlConnection(connectionString);
    }

    public override async Task<IReadOnlyList<DatabaseObject>> GetDatabaseObjectsAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            SELECT s.SCHEMA_NAME, NULL AS object_name, 'schema' AS object_type,
                   NULL AS parent_name
            FROM information_schema.SCHEMATA s
            WHERE s.SCHEMA_NAME NOT IN ('information_schema', 'mysql', 'performance_schema', 'sys')
            UNION ALL
            SELECT t.TABLE_SCHEMA, t.TABLE_NAME,
                   CASE WHEN t.TABLE_TYPE = 'VIEW' THEN 'view' ELSE 'table' END,
                   t.TABLE_SCHEMA
            FROM information_schema.TABLES t
            WHERE t.TABLE_SCHEMA NOT IN ('information_schema', 'mysql', 'performance_schema', 'sys')
            UNION ALL
            SELECT r.ROUTINE_SCHEMA, r.ROUTINE_NAME,
                   CASE WHEN r.ROUTINE_TYPE = 'PROCEDURE' THEN 'procedure' ELSE 'function' END,
                   r.ROUTINE_SCHEMA
            FROM information_schema.ROUTINES r
            WHERE r.ROUTINE_SCHEMA NOT IN ('information_schema', 'mysql', 'performance_schema', 'sys')
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
                        "procedure" => DatabaseObjectType.Procedure,
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
            SELECT COLUMN_NAME, COLUMN_TYPE, IS_NULLABLE, ORDINAL_POSITION
            FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
            ORDER BY ORDINAL_POSITION
            """;
        var schema = GetSchema(connection, databaseObject);
        return await ReadColumnsAsync(
            connection,
            sql,
            command =>
            {
                AddParameter(command, "@schema", schema);
                AddParameter(command, "@table", databaseObject.Name);
            },
            reader => new DatabaseColumn
            {
                Name = reader.GetString(0),
                TableName = databaseObject.Name,
                Schema = schema,
                DataType = reader.IsDBNull(1) ? null : reader.GetString(1),
                IsNullable = reader.GetString(2) == "YES",
                Ordinal = Convert.ToInt32(reader.GetValue(3), CultureInfo.InvariantCulture),
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
        if (
            databaseObject.ObjectType
            is not DatabaseObjectType.Table
                and not DatabaseObjectType.View
        )
        {
            return await base.GetObjectDetailsAsync(connection, databaseObject, cancellationToken);
        }

        var schema = GetSchema(connection, databaseObject);
        var columns = await GetColumnsAsync(connection, databaseObject, cancellationToken);
        await EnsureOpenAsync(connection, cancellationToken);

        var indexes = new List<DatabaseIndex>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT INDEX_NAME, MIN(NON_UNIQUE)
                FROM information_schema.STATISTICS
                WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
                GROUP BY INDEX_NAME
                ORDER BY INDEX_NAME
                """;
            AddParameter(command, "@schema", schema);
            AddParameter(command, "@table", databaseObject.Name);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                indexes.Add(
                    new DatabaseIndex
                    {
                        Name = reader.GetString(0),
                        IsUnique =
                            Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture) == 0,
                    }
                );
            }
        }

        var constraints = new List<DatabaseConstraint>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT CONSTRAINT_NAME, CONSTRAINT_TYPE
                FROM information_schema.TABLE_CONSTRAINTS
                WHERE TABLE_SCHEMA = @schema AND TABLE_NAME = @table
                ORDER BY CONSTRAINT_NAME
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
                SELECT TRIGGER_NAME, EVENT_MANIPULATION, ACTION_STATEMENT
                FROM information_schema.TRIGGERS
                WHERE EVENT_OBJECT_SCHEMA = @schema AND EVENT_OBJECT_TABLE = @table
                ORDER BY TRIGGER_NAME
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
                        Event = reader.GetString(1),
                        Definition = reader.IsDBNull(2) ? null : reader.GetString(2),
                    }
                );
            }
        }

        string? ddl;
        using (var command = connection.CreateCommand())
        {
            var objectName = $"{QuoteBacktick(schema)}.{QuoteBacktick(databaseObject.Name)}";
            command.CommandText =
                databaseObject.ObjectType == DatabaseObjectType.View
                    ? $"SHOW CREATE VIEW {objectName}"
                    : $"SHOW CREATE TABLE {objectName}";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            ddl =
                await reader.ReadAsync(cancellationToken) && !reader.IsDBNull(1)
                    ? reader.GetString(1)
                    : null;
        }

        return new DatabaseObjectDetails
        {
            DatabaseObject = databaseObject,
            Columns = columns,
            Indexes = indexes,
            Constraints = constraints,
            Triggers = triggers,
            Ddl = ddl ?? string.Empty,
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
            ? QuoteBacktick(databaseObject.Name)
            : $"{QuoteBacktick(databaseObject.Schema)}.{QuoteBacktick(databaseObject.Name)}";
        return $"SELECT * FROM {name} LIMIT {rowLimit};";
    }

    private static string GetSchema(DbConnection connection, DatabaseObject databaseObject)
    {
        var schema = string.IsNullOrWhiteSpace(databaseObject.Schema)
            ? connection.Database
            : databaseObject.Schema;
        return !string.IsNullOrWhiteSpace(schema)
            ? schema
            : throw new InvalidOperationException("Select a MySQL database first.");
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
