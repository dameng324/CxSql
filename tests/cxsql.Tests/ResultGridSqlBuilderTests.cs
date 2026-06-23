using CxSql.Models;
using CxSql.UI.Components;
using SharpConsoleUI.Controls;
using TUnit.Core;

namespace CxSql.Tests;

public sealed class ResultGridSqlBuilderTests
{
    [Test]
    public void BuildWrapsBaseSqlWithFilterAndSort()
    {
        var sql = ResultGridSqlBuilder.Build(
            DatabaseType.Sqlite,
            """
            SELECT *
            FROM "people";
            """,
            new ResultGridFilterRequest("name", ResultGridFilterOperator.Contains, "Ada_%"),
            new ResultGridSortRequest("id", SortDirection.Descending)
        );

        RequireContains(sql, "FROM (");
        RequireContains(sql, "SELECT *");
        RequireContains(sql, "WHERE \"name\" LIKE '%Ada\\_\\%%' ESCAPE '\\'");
        RequireContains(sql, "ORDER BY \"id\" DESC");
        if (!sql.EndsWith(';'))
        {
            throw new InvalidOperationException("Generated SQL should be executable directly.");
        }
    }

    private static void RequireContains(string value, string expected)
    {
        if (!value.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected SQL to contain: {expected}");
        }
    }
}
