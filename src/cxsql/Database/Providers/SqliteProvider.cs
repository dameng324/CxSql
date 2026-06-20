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
}
