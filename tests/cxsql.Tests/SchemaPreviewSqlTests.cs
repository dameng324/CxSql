using CxSql.Database.Providers;
using TUnit.Core;

namespace CxSql.Tests;

public sealed class SchemaPreviewSqlTests
{
    [Test]
    public void MySqlComparesTheSchemaAsAString()
    {
        var sql = new MySqlProvider().BuildSchemaPreviewSql("o'ne\\");

        Expect(sql, "WHERE TABLE_SCHEMA = 'o''ne\\\\'", "MySQL");
    }

    [Test]
    public void PostgreSqlComparesTheSchemaAsAStringOnce()
    {
        var sql = new PostgreSqlProvider().BuildSchemaPreviewSql("o'ne");

        Expect(sql, "WHERE c.table_schema = 'o''ne'", "PostgreSQL");
        if (sql.Contains("UNION", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"PostgreSQL schema preview repeats its rows: {sql}");
        }
    }

    [Test]
    public void SqlServerComparesTheSchemaAsAString()
    {
        var sql = new SqlServerProvider().BuildSchemaPreviewSql("o'ne");

        Expect(sql, "WHERE s.name = N'o''ne'", "SQL Server");
    }

    private static void Expect(string sql, string fragment, string provider)
    {
        if (!sql.Contains(fragment, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unexpected {provider} schema preview SQL: {sql}");
        }
    }
}
