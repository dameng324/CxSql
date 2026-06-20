using System.Text;
using CxSql.Models;

namespace CxSql.Application.Services;

public sealed class CsvExportService
{
    public async Task ExportAsync(
        QueryResult queryResult,
        string filePath,
        CancellationToken cancellationToken
    )
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
            filePath,
            ToCsv(queryResult),
            Encoding.UTF8,
            cancellationToken
        );
    }

    public string ToCsv(QueryResult queryResult)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            string.Join(',', queryResult.Columns.Select(column => Escape(column.Name)))
        );

        foreach (var row in queryResult.Rows)
        {
            builder.AppendLine(string.Join(',', row.Values.Select(Escape)));
        }

        return builder.ToString();
    }

    private static string Escape(string? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var mustQuote =
            value.Contains(',', StringComparison.Ordinal)
            || value.Contains('"', StringComparison.Ordinal)
            || value.Contains('\r', StringComparison.Ordinal)
            || value.Contains('\n', StringComparison.Ordinal);

        if (!mustQuote)
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
