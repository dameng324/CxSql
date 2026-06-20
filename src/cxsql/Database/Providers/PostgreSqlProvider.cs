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
}
