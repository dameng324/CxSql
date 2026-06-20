using CxSql.Models;

namespace CxSql.UI.Components;

public sealed class ResultGridRenderer
{
    private const int MaxColumnWidth = 32;

    public void Render(QueryResult queryResult, TextWriter writer)
    {
        if (queryResult.Columns.Count == 0)
        {
            foreach (var message in queryResult.Messages)
            {
                writer.WriteLine($"[{message.Level}] {message.Message}");
            }

            return;
        }

        var widths = CalculateColumnWidths(queryResult);
        WriteSeparator(widths, writer);
        WriteRow(queryResult.Columns.Select(column => column.Name).ToList(), widths, writer);
        WriteSeparator(widths, writer);

        foreach (var row in queryResult.Rows)
        {
            WriteRow(row.Values, widths, writer);
        }

        WriteSeparator(widths, writer);
        writer.WriteLine(
            $"{queryResult.Rows.Count} row(s), {queryResult.ElapsedMilliseconds} ms elapsed."
        );

        foreach (var message in queryResult.Messages)
        {
            writer.WriteLine($"[{message.Level}] {message.Message}");
        }
    }

    private static List<int> CalculateColumnWidths(QueryResult queryResult)
    {
        var widths = queryResult
            .Columns.Select(column => Math.Min(MaxColumnWidth, Math.Max(3, column.Name.Length)))
            .ToList();

        foreach (var row in queryResult.Rows)
        {
            for (var index = 0; index < row.Values.Count && index < widths.Count; index++)
            {
                widths[index] = Math.Min(
                    MaxColumnWidth,
                    Math.Max(widths[index], row.Values[index]?.Length ?? 4)
                );
            }
        }

        return widths;
    }

    private static void WriteSeparator(IReadOnlyList<int> widths, TextWriter writer)
    {
        writer.Write('+');
        foreach (var width in widths)
        {
            writer.Write(new string('-', width + 2));
            writer.Write('+');
        }

        writer.WriteLine();
    }

    private static void WriteRow(
        IReadOnlyList<string?> values,
        IReadOnlyList<int> widths,
        TextWriter writer
    )
    {
        writer.Write('|');
        for (var index = 0; index < widths.Count; index++)
        {
            var value = index < values.Count ? values[index] ?? "NULL" : string.Empty;
            writer.Write(' ');
            writer.Write(Trim(value, widths[index]).PadRight(widths[index]));
            writer.Write(" |");
        }

        writer.WriteLine();
    }

    private static string Trim(string value, int width)
    {
        if (value.Length <= width)
        {
            return value;
        }

        return width <= 3 ? value[..width] : value[..(width - 3)] + "...";
    }
}
