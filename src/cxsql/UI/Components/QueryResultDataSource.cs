using System.Collections.Specialized;
using CxSql.Models;
using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;

namespace CxSql.UI.Components;

public sealed class QueryResultDataSource : ITableDataSource
{
    private static readonly Color OddRowBackground = new(18, 22, 35);
    private QueryResult? queryResult;
    private List<QueryRow> rows = [];
    private int sortColumn;
    private SortDirection sortDirection = SortDirection.Ascending;
    private string? filterText;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public int RowCount => rows.Count;

    public int ColumnCount => queryResult?.Columns.Count ?? 0;

    public bool CanFilter => true;

    public QueryResult? QueryResult => queryResult;

    public void SetResult(QueryResult? result)
    {
        queryResult = result;
        sortColumn = 0;
        sortDirection = SortDirection.Ascending;
        filterText = null;
        RebuildRows();
    }

    public string GetColumnHeader(int col)
    {
        if (queryResult is null || col < 0 || col >= queryResult.Columns.Count)
        {
            return string.Empty;
        }

        return queryResult.Columns[col].Name;
    }

    public string GetCellValue(int row, int col)
    {
        if (row < 0 || row >= rows.Count || col < 0)
        {
            return string.Empty;
        }

        var values = rows[row].Values;
        if (col >= values.Count)
        {
            return string.Empty;
        }

        return values[col] is null ? "[grey50]NULL[/]" : MarkupParser.Escape(values[col]!);
    }

    public TextJustification GetColumnAlignment(int col)
    {
        return TextJustification.Left;
    }

    public int? GetColumnWidth(int col)
    {
        if (queryResult is null || col < 0 || col >= queryResult.Columns.Count)
        {
            return null;
        }

        var width = Math.Max(8, queryResult.Columns[col].Name.Length + 2);
        foreach (var row in rows.Take(50))
        {
            if (col < row.Values.Count)
            {
                width = Math.Max(width, row.Values[col]?.Length ?? 4);
            }
        }

        return Math.Min(36, width + 2);
    }

    public Color? GetRowBackgroundColor(int row)
    {
        return row % 2 == 1 ? OddRowBackground : null;
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
        return queryResult is not null && col >= 0 && col < queryResult.Columns.Count;
    }

    public void Sort(int col, SortDirection dir)
    {
        if (!CanSort(col))
        {
            return;
        }

        sortColumn = col;
        sortDirection = dir;
        RebuildRows();
    }

    public void ApplyFilter(string filterText, string? columnName, FilterOperator op)
    {
        this.filterText = filterText;
        RebuildRows();
    }

    public void ClearFilter()
    {
        filterText = null;
        RebuildRows();
    }

    private void RebuildRows()
    {
        IEnumerable<QueryRow> nextRows = queryResult?.Rows ?? [];

        if (!string.IsNullOrWhiteSpace(filterText))
        {
            nextRows = nextRows.Where(row =>
                row.Values.Any(value =>
                    value?.Contains(filterText, StringComparison.OrdinalIgnoreCase) == true
                )
            );
        }

        if (queryResult is not null && CanSort(sortColumn))
        {
            nextRows =
                sortDirection == SortDirection.Descending
                    ? nextRows.OrderByDescending(row => GetSortValue(row, sortColumn))
                    : nextRows.OrderBy(row => GetSortValue(row, sortColumn));
        }

        rows = nextRows.ToList();
        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)
        );
    }

    private static string GetSortValue(QueryRow row, int column)
    {
        return column >= 0 && column < row.Values.Count
            ? row.Values[column] ?? string.Empty
            : string.Empty;
    }
}
