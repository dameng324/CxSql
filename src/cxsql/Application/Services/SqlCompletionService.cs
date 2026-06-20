using CxSql.Models;

namespace CxSql.Application.Services;

public sealed record SqlCompletionSuggestion(string Text, string ReplacementText, string Kind);

public sealed class SqlCompletionService
{
    private static readonly string[] Keywords =
    [
        "SELECT",
        "FROM",
        "WHERE",
        "JOIN",
        "LEFT JOIN",
        "RIGHT JOIN",
        "INNER JOIN",
        "OUTER JOIN",
        "ON",
        "GROUP BY",
        "ORDER BY",
        "HAVING",
        "LIMIT",
        "TOP",
        "OFFSET",
        "INSERT INTO",
        "VALUES",
        "UPDATE",
        "SET",
        "DELETE FROM",
        "CREATE TABLE",
        "ALTER TABLE",
        "DROP TABLE",
        "CREATE INDEX",
        "BEGIN",
        "COMMIT",
        "ROLLBACK",
        "AND",
        "OR",
        "NOT",
        "NULL",
        "IS NULL",
        "IS NOT NULL",
        "COUNT",
        "SUM",
        "AVG",
        "MIN",
        "MAX",
    ];

    public IReadOnlyList<SqlCompletionSuggestion> GetSuggestions(
        string sql,
        IEnumerable<DatabaseObject> databaseObjects,
        IEnumerable<DatabaseColumn> knownColumns,
        IEnumerable<QueryColumn> resultColumns,
        int maxSuggestions = 20
    )
    {
        var prefix = GetCurrentPrefix(sql);
        var suggestions = new List<SqlCompletionSuggestion>();

        suggestions.AddRange(
            Keywords.Select(keyword => new SqlCompletionSuggestion(keyword, keyword, "Keyword"))
        );
        suggestions.AddRange(
            databaseObjects
                .Where(databaseObject =>
                    databaseObject.ObjectType
                        is DatabaseObjectType.Table
                            or DatabaseObjectType.View
                            or DatabaseObjectType.Procedure
                            or DatabaseObjectType.Function
                )
                .Select(databaseObject => new SqlCompletionSuggestion(
                    databaseObject.DisplayName,
                    databaseObject.DisplayName,
                    databaseObject.ObjectType.ToString()
                ))
        );
        suggestions.AddRange(
            knownColumns.Select(column => new SqlCompletionSuggestion(
                $"{column.Name} ({column.TableName})",
                column.Name,
                "Column"
            ))
        );
        suggestions.AddRange(
            resultColumns.Select(column => new SqlCompletionSuggestion(
                $"{column.Name} (result)",
                column.Name,
                "Result Column"
            ))
        );

        return suggestions
            .Where(suggestion =>
                string.IsNullOrWhiteSpace(prefix)
                || suggestion.ReplacementText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || suggestion.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            )
            .DistinctBy(
                suggestion => $"{suggestion.Kind}:{suggestion.ReplacementText}",
                StringComparer.OrdinalIgnoreCase
            )
            .OrderBy(suggestion => suggestion.Kind == "Keyword" ? 0 : 1)
            .ThenBy(suggestion => suggestion.ReplacementText, StringComparer.OrdinalIgnoreCase)
            .Take(maxSuggestions)
            .ToList();
    }

    public static string ApplyCompletion(string sql, SqlCompletionSuggestion suggestion)
    {
        var prefixStart = FindCurrentPrefixStart(sql);
        return string.Concat(sql.AsSpan(0, prefixStart), suggestion.ReplacementText, " ");
    }

    public static string GetCurrentPrefix(string sql)
    {
        var start = FindCurrentPrefixStart(sql);
        return start >= sql.Length ? string.Empty : sql[start..];
    }

    public static string GetCurrentPrefixAtCursor(string sql, int currentLine, int currentColumn)
    {
        var line = GetLineAt(sql, currentLine);
        if (line.Length == 0)
        {
            return string.Empty;
        }

        var cursorIndex = Math.Clamp(currentColumn - 1, 0, line.Length);
        var index = cursorIndex - 1;
        while (index >= 0)
        {
            var character = line[index];
            if (!char.IsLetterOrDigit(character) && character != '_' && character != '.')
            {
                break;
            }

            index--;
        }

        return line[(index + 1)..cursorIndex];
    }

    private static string GetLineAt(string sql, int currentLine)
    {
        if (string.IsNullOrEmpty(sql))
        {
            return string.Empty;
        }

        var lines = sql.Split('\n');
        var index = Math.Clamp(currentLine - 1, 0, lines.Length - 1);
        return lines[index].TrimEnd('\r');
    }

    private static int FindCurrentPrefixStart(string sql)
    {
        if (string.IsNullOrEmpty(sql))
        {
            return 0;
        }

        var index = sql.Length - 1;
        while (index >= 0)
        {
            var character = sql[index];
            if (!char.IsLetterOrDigit(character) && character != '_' && character != '.')
            {
                break;
            }

            index--;
        }

        return index + 1;
    }
}
