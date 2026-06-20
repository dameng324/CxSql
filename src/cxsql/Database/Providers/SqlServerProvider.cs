using System.Data.Common;
using CxSql.Models;
using Microsoft.Data.SqlClient;

namespace CxSql.Database.Providers;

public sealed class SqlServerProvider : DatabaseProviderBase
{
    public override string ProviderName => "SQL Server";

    public override DbConnection CreateConnection(string connectionString)
    {
        return new SqlConnection(connectionString);
    }

    public override async Task<IReadOnlyList<DatabaseObject>> GetDatabaseObjectsAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            SELECT s.name AS schema_name,
                   NULL AS object_name,
                   'schema' AS object_type,
                   NULL AS parent_name
            FROM sys.schemas s
            WHERE s.name NOT IN ('sys', 'INFORMATION_SCHEMA')
            UNION ALL
            SELECT s.name, t.name, 'table', s.name
            FROM sys.tables t
            JOIN sys.schemas s ON s.schema_id = t.schema_id
            UNION ALL
            SELECT s.name, v.name, 'view', s.name
            FROM sys.views v
            JOIN sys.schemas s ON s.schema_id = v.schema_id
            UNION ALL
            SELECT s.name, p.name, 'procedure', s.name
            FROM sys.procedures p
            JOIN sys.schemas s ON s.schema_id = p.schema_id
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
                        _ => DatabaseObjectType.Procedure,
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
            SELECT c.name,
                   t.name,
                   c.is_nullable,
                   c.column_id
            FROM sys.columns c
            JOIN sys.types t ON t.user_type_id = c.user_type_id
            JOIN sys.objects o ON o.object_id = c.object_id
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE s.name = @schema
              AND o.name = @table
            ORDER BY c.column_id
            """;

        var schema = string.IsNullOrWhiteSpace(databaseObject.Schema)
            ? "dbo"
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
                IsNullable = reader.GetBoolean(2),
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
            ? "dbo"
            : databaseObject.Schema;

        var indexes = new List<DatabaseIndex>();
        await EnsureOpenAsync(connection, cancellationToken);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT i.name,
                       i.is_unique,
                       i.type_desc
                FROM sys.indexes i
                JOIN sys.objects o ON o.object_id = i.object_id
                JOIN sys.schemas s ON s.schema_id = o.schema_id
                WHERE s.name = @schema
                  AND o.name = @table
                  AND i.name IS NOT NULL
                ORDER BY i.name
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
                        IsUnique = reader.GetBoolean(1),
                        Definition = reader.IsDBNull(2) ? null : reader.GetString(2),
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
                SELECT tr.name,
                       OBJECT_DEFINITION(tr.object_id)
                FROM sys.triggers tr
                JOIN sys.objects o ON o.object_id = tr.parent_id
                JOIN sys.schemas s ON s.schema_id = o.schema_id
                WHERE s.name = @schema
                  AND o.name = @table
                ORDER BY tr.name
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
                        Definition = reader.IsDBNull(1) ? null : reader.GetString(1),
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
            ? QuoteBracket(databaseObject.Name)
            : $"{QuoteBracket(databaseObject.Schema)}.{QuoteBracket(databaseObject.Name)}";

        return $"SELECT TOP ({rowLimit}) * FROM {name};";
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
