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
}
