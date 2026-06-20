using System.Collections.Specialized;
using CxSql.Models;
using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;

namespace CxSql.UI.Components;

public sealed class ConnectionSummaryDataSource : ITableDataSource
{
    private static readonly string[] Headers = ["Name", "Type", "Updated"];
    private List<DatabaseConnection> connections = [];

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public int RowCount => connections.Count;

    public int ColumnCount => Headers.Length;

    public bool CanFilter => true;

    public void SetConnections(IEnumerable<DatabaseConnection> nextConnections)
    {
        connections = nextConnections.ToList();
        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)
        );
    }

    public string GetColumnHeader(int col)
    {
        return col >= 0 && col < Headers.Length ? Headers[col] : string.Empty;
    }

    public string GetCellValue(int row, int col)
    {
        if (row < 0 || row >= connections.Count)
        {
            return string.Empty;
        }

        var connection = connections[row];
        return col switch
        {
            0 => MarkupParser.Escape(connection.Name),
            1 => connection.DatabaseType.ToString(),
            2 => DateTimeOffset
                .FromUnixTimeMilliseconds(connection.UpdatedAtUnixMs)
                .ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm"),
            _ => string.Empty,
        };
    }

    public TextJustification GetColumnAlignment(int col)
    {
        return TextJustification.Left;
    }

    public int? GetColumnWidth(int col)
    {
        return col switch
        {
            0 => null,
            1 => 12,
            2 => 18,
            _ => null,
        };
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
        return row >= 0 && row < connections.Count;
    }

    public object? GetRowTag(int row)
    {
        return row >= 0 && row < connections.Count ? connections[row] : null;
    }

    public bool CanSort(int col)
    {
        return col >= 0 && col < Headers.Length;
    }

    public void Sort(int col, SortDirection dir)
    {
        connections = (col, dir) switch
        {
            (0, SortDirection.Descending) => connections
                .OrderByDescending(item => item.Name)
                .ToList(),
            (0, _) => connections.OrderBy(item => item.Name).ToList(),
            (1, SortDirection.Descending) => connections
                .OrderByDescending(item => item.DatabaseType)
                .ToList(),
            (1, _) => connections.OrderBy(item => item.DatabaseType).ToList(),
            (2, SortDirection.Descending) => connections
                .OrderByDescending(item => item.UpdatedAtUnixMs)
                .ToList(),
            (2, _) => connections.OrderBy(item => item.UpdatedAtUnixMs).ToList(),
            _ => connections,
        };
        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)
        );
    }

    public void ApplyFilter(string filterText, string? columnName, FilterOperator op) { }

    public void ClearFilter() { }
}
