using System.Data.Common;
using CxSql.Models;
using Microsoft.Data.Sqlite;

namespace CxSql.Database.Providers;

public sealed class SqliteProvider : DatabaseProviderBase
{
    public override string ProviderName => "SQLite";

    public override DbConnection CreateConnection(string connectionString)
    {
        return new SqliteConnection(connectionString);
    }

    public override async Task<IReadOnlyList<DatabaseObject>> GetDatabaseObjectsAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        const string sql = """
            SELECT name, type, tbl_name
            FROM sqlite_master
            WHERE type IN ('table', 'view', 'index')
              AND name NOT LIKE 'sqlite_%'
            ORDER BY
              CASE type
                WHEN 'table' THEN 1
                WHEN 'view' THEN 2
                ELSE 3
              END,
              name
            """;

        return await ReadObjectsAsync(
            connection,
            sql,
            reader => new DatabaseObject
            {
                Name = reader.GetString(0),
                ObjectType = reader.GetString(1) switch
                {
                    "table" => DatabaseObjectType.Table,
                    "view" => DatabaseObjectType.View,
                    _ => DatabaseObjectType.Index,
                },
                ParentName = reader.IsDBNull(2) ? null : reader.GetString(2),
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

        var sql = $"PRAGMA table_info({QuoteDouble(databaseObject.Name)});";
        return await ReadColumnsAsync(
            connection,
            sql,
            configureCommand: null,
            reader => new DatabaseColumn
            {
                Name = reader.GetString(1),
                TableName = databaseObject.Name,
                DataType = reader.IsDBNull(2) ? null : reader.GetString(2),
                IsNullable = Convert.ToInt32(reader.GetValue(3)) == 0,
                Ordinal = Convert.ToInt32(reader.GetValue(0)),
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
        var ddl = await ReadScalarStringAsync(
            connection,
            """
            SELECT sql
            FROM sqlite_master
            WHERE name = $name
              AND type IN ('table', 'view')
            LIMIT 1
            """,
            "$name",
            databaseObject.Name,
            cancellationToken
        );

        var indexHeaders = new List<(string Name, bool IsUnique)>();
        await EnsureOpenAsync(connection, cancellationToken);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA index_list({QuoteDouble(databaseObject.Name)});";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(1);
                var isUnique = Convert.ToInt32(reader.GetValue(2)) != 0;
                indexHeaders.Add((name, isUnique));
            }
        }

        var indexes = new List<DatabaseIndex>();
        foreach (var index in indexHeaders)
        {
            indexes.Add(
                new DatabaseIndex
                {
                    Name = index.Name,
                    IsUnique = index.IsUnique,
                    Definition = await ReadScalarStringAsync(
                        connection,
                        "SELECT sql FROM sqlite_master WHERE name = $name LIMIT 1",
                        "$name",
                        index.Name,
                        cancellationToken
                    ),
                }
            );
        }

        var triggers = new List<DatabaseTrigger>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT name, sql
                FROM sqlite_master
                WHERE type = 'trigger'
                  AND tbl_name = $table
                ORDER BY name
                """;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "$table";
            parameter.Value = databaseObject.Name;
            command.Parameters.Add(parameter);

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

        var constraints = indexes
            .Where(index => index.IsUnique)
            .Select(index => new DatabaseConstraint
            {
                Name = index.Name,
                Type = "UNIQUE",
                Definition = index.Definition,
            })
            .ToList();

        return new DatabaseObjectDetails
        {
            DatabaseObject = databaseObject,
            Columns = columns,
            Indexes = indexes,
            Triggers = triggers,
            Constraints = constraints,
            Ddl = ddl ?? BuildCreateTableSketch(databaseObject, columns),
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

        return $"SELECT * FROM {QuoteDouble(databaseObject.Name)} LIMIT {rowLimit};";
    }

    private static async Task<string?> ReadScalarStringAsync(
        DbConnection connection,
        string sql,
        string parameterName,
        string value,
        CancellationToken cancellationToken
    )
    {
        await EnsureOpenAsync(connection, cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = parameterName;
        parameter.Value = value;
        command.Parameters.Add(parameter);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : result.ToString();
    }
}
