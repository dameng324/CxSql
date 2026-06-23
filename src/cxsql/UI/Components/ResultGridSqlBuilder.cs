using System.Globalization;
using System.Text;
using CxSql.Models;
using SharpConsoleUI.Controls;

namespace CxSql.UI.Components;

public static class ResultGridSqlBuilder
{
    public static string Build(
        DatabaseType databaseType,
        string baseSql,
        ResultGridFilterRequest? filter,
        ResultGridSortRequest? sort
    )
    {
        var normalizedBaseSql = NormalizeBaseSql(baseSql);
        if (filter is null && (sort is null || sort.Direction == SortDirection.None))
        {
            return normalizedBaseSql + ";";
        }

        var builder = new StringBuilder();
        builder.AppendLine("SELECT *");
        builder.AppendLine("FROM (");
        foreach (var line in normalizedBaseSql.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            builder.Append("    ");
            builder.AppendLine(line);
        }

        builder.Append(") AS ");
        builder.AppendLine(QuoteIdentifier(databaseType, "result_source"));

        if (filter is not null)
        {
            builder.Append("WHERE ");
            builder.AppendLine(BuildPredicate(databaseType, filter));
        }

        if (sort is not null && sort.Direction != SortDirection.None)
        {
            builder.Append("ORDER BY ");
            builder.Append(QuoteIdentifier(databaseType, sort.ColumnName));
            builder.Append(sort.Direction == SortDirection.Descending ? " DESC" : " ASC");
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd() + ";";
    }

    private static string BuildPredicate(DatabaseType databaseType, ResultGridFilterRequest filter)
    {
        var column = QuoteIdentifier(databaseType, filter.ColumnName);
        return filter.Operator switch
        {
            ResultGridFilterOperator.Equal => $"{column} = {BuildStringLiteral(filter.Value)}",
            ResultGridFilterOperator.Contains =>
                $"{column} LIKE {BuildLikeLiteral(filter.Value)} ESCAPE '\\'",
            ResultGridFilterOperator.GreaterThan =>
                $"{column} > {BuildComparisonLiteral(filter.Value)}",
            ResultGridFilterOperator.LessThan =>
                $"{column} < {BuildComparisonLiteral(filter.Value)}",
            ResultGridFilterOperator.GreaterThanOrEqual =>
                $"{column} >= {BuildComparisonLiteral(filter.Value)}",
            ResultGridFilterOperator.LessThanOrEqual =>
                $"{column} <= {BuildComparisonLiteral(filter.Value)}",
            _ => throw new ArgumentOutOfRangeException(nameof(filter), filter.Operator, null),
        };
    }

    private static string BuildComparisonLiteral(string value)
    {
        return decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var number
        )
            ? number.ToString(CultureInfo.InvariantCulture)
            : BuildStringLiteral(value);
    }

    private static string BuildLikeLiteral(string value)
    {
        return BuildStringLiteral($"%{EscapeLikeValue(value)}%");
    }

    private static string BuildStringLiteral(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    private static string EscapeLikeValue(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }

    private static string QuoteIdentifier(DatabaseType databaseType, string identifier)
    {
        return databaseType == DatabaseType.SqlServer
            ? "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]"
            : "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string NormalizeBaseSql(string baseSql)
    {
        var normalized = baseSql.Trim();
        while (normalized.EndsWith(';'))
        {
            normalized = normalized[..^1].TrimEnd();
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("A result source SQL statement is required.");
        }

        return normalized;
    }
}
