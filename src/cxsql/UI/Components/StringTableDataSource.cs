using System.Collections.Specialized;
using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;

namespace CxSql.UI.Components;

public sealed class StringTableDataSource : ITableDataSource
{
    private readonly IReadOnlyList<string> headers;
    private readonly IReadOnlyList<IReadOnlyList<string>> rows;

    public StringTableDataSource(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows
    )
    {
        this.headers = headers;
        this.rows = rows;
    }

    public event NotifyCollectionChangedEventHandler? CollectionChanged
    {
        add { }
        remove { }
    }

    public int RowCount => rows.Count;

    public int ColumnCount => headers.Count;

    public bool CanFilter => false;

    public string GetColumnHeader(int col)
    {
        return col >= 0 && col < headers.Count ? headers[col] : string.Empty;
    }

    public string GetCellValue(int row, int col)
    {
        if (row < 0 || row >= rows.Count || col < 0 || col >= headers.Count)
        {
            return string.Empty;
        }

        var value = col < rows[row].Count ? rows[row][col] : string.Empty;
        return string.IsNullOrEmpty(value) ? "[grey50]-[/]" : MarkupParser.Escape(value);
    }

    public TextJustification GetColumnAlignment(int col)
    {
        return TextJustification.Left;
    }

    public int? GetColumnWidth(int col)
    {
        if (col < 0 || col >= headers.Count)
        {
            return null;
        }

        var width = Math.Max(8, headers[col].Length + 2);
        foreach (var row in rows.Take(40))
        {
            if (col < row.Count)
            {
                width = Math.Max(width, row[col].Length + 2);
            }
        }

        return Math.Min(48, width);
    }

    public Color? GetRowBackgroundColor(int row)
    {
        return row % 2 == 1 ? new Color(18, 22, 35) : null;
    }

    public Color? GetRowForegroundColor(int row)
    {
        return null;
    }

    public bool IsRowEnabled(int row)
    {
        return row >= 0 && row < rows.Count;
    }

    public object? GetRowTag(int row)
    {
        return row >= 0 && row < rows.Count ? rows[row] : null;
    }

    public bool CanSort(int col)
    {
        return false;
    }

    public void Sort(int col, SortDirection dir) { }

    public void ApplyFilter(string filterText, string? columnName, FilterOperator op) { }

    public void ClearFilter() { }
}
