using System.Collections.Specialized;
using System.Globalization;
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
    private int? sortColumn;
    private SortDirection sortDirection = SortDirection.Ascending;
    private string? filterText;
    private ColumnFilter? columnFilter;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public int RowCount => rows.Count;

    public int ColumnCount => queryResult?.Columns.Count ?? 0;

    public bool CanFilter => true;

    public QueryResult? QueryResult => queryResult;

    public void SetResult(QueryResult? result)
    {
        queryResult = result;
        sortColumn = null;
        sortDirection = SortDirection.Ascending;
        filterText = null;
        columnFilter = null;
        RebuildRows();
    }

    public QueryResult? ToVisibleResult()
    {
        if (queryResult is null)
        {
            return null;
        }

        return new QueryResult
        {
            Columns = queryResult.Columns.ToList(),
            Rows = rows.Select(row => new QueryRow { Values = row.Values.ToList() }).ToList(),
            Messages = queryResult.Messages.ToList(),
            ElapsedMilliseconds = queryResult.ElapsedMilliseconds,
            AffectedRows = queryResult.AffectedRows,
            Success = queryResult.Success,
            ErrorMessage = queryResult.ErrorMessage,
            ProviderErrorCode = queryResult.ProviderErrorCode,
        };
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

    public string GetPlainCellValue(int row, int col)
    {
        if (row < 0 || row >= rows.Count || col < 0 || col >= ColumnCount)
        {
            return string.Empty;
        }

        var values = rows[row].Values;
        return col < values.Count ? values[col] ?? string.Empty : string.Empty;
    }

    public IReadOnlyList<string> GetPlainRowValues(int row)
    {
        if (row < 0 || row >= rows.Count)
        {
            return [];
        }

        return rows[row].Values.Select(value => value ?? string.Empty).ToList();
    }

    public string ToTabDelimitedText()
    {
        if (queryResult is null)
        {
            return string.Empty;
        }

        var lines = new List<string>
        {
            string.Join('\t', queryResult.Columns.Select(column => column.Name)),
        };
        lines.AddRange(rows.Select(row => string.Join('\t', row.Values)));
        return string.Join(Environment.NewLine, lines);
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
        if (
            !string.IsNullOrWhiteSpace(columnName)
            && TryGetColumnIndex(columnName, out var columnIndex)
        )
        {
            columnFilter = new ColumnFilter(
                columnIndex,
                ToResultGridFilterOperator(op),
                filterText
            );
            this.filterText = null;
        }
        else
        {
            this.filterText = filterText;
            columnFilter = null;
        }

        RebuildRows();
    }

    public void ApplyColumnFilter(
        int columnIndex,
        ResultGridFilterOperator filterOperator,
        string value
    )
    {
        if (queryResult is null || columnIndex < 0 || columnIndex >= queryResult.Columns.Count)
        {
            return;
        }

        columnFilter = new ColumnFilter(columnIndex, filterOperator, value);
        filterText = null;
        RebuildRows();
    }

    public void ClearFilter()
    {
        filterText = null;
        columnFilter = null;
        RebuildRows();
    }

    public void ClearSort()
    {
        sortColumn = null;
        sortDirection = SortDirection.Ascending;
        RebuildRows();
    }

    private void RebuildRows()
    {
        IEnumerable<QueryRow> nextRows = queryResult?.Rows ?? [];

        if (columnFilter is not null)
        {
            nextRows = nextRows.Where(row => MatchesColumnFilter(row, columnFilter));
        }
        else if (!string.IsNullOrWhiteSpace(filterText))
        {
            nextRows = nextRows.Where(row =>
                row.Values.Any(value =>
                    value?.Contains(filterText, StringComparison.OrdinalIgnoreCase) == true
                )
            );
        }

        if (queryResult is not null && sortColumn is { } column && CanSort(column))
        {
            nextRows =
                sortDirection == SortDirection.Descending
                    ? nextRows
                        .OrderBy(row => IsNullSortValue(row, column))
                        .ThenByDescending(row => GetSortValue(row, column))
                    : nextRows
                        .OrderBy(row => IsNullSortValue(row, column))
                        .ThenBy(row => GetSortValue(row, column));
        }

        rows = nextRows.ToList();
        CollectionChanged?.Invoke(
            this,
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)
        );
    }

    private bool TryGetColumnIndex(string columnName, out int columnIndex)
    {
        columnIndex = -1;
        if (queryResult is null)
        {
            return false;
        }

        for (var index = 0; index < queryResult.Columns.Count; index++)
        {
            if (
                string.Equals(
                    queryResult.Columns[index].Name,
                    columnName,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                columnIndex = index;
                return true;
            }
        }

        return false;
    }

    private bool MatchesColumnFilter(QueryRow row, ColumnFilter filter)
    {
        if (filter.ColumnIndex < 0 || filter.ColumnIndex >= row.Values.Count)
        {
            return false;
        }

        var value = row.Values[filter.ColumnIndex];
        if (value is null)
        {
            return false;
        }

        return filter.Operator switch
        {
            ResultGridFilterOperator.Equal => string.Equals(
                value,
                filter.Value,
                StringComparison.OrdinalIgnoreCase
            ),
            ResultGridFilterOperator.Contains => value.Contains(
                filter.Value,
                StringComparison.OrdinalIgnoreCase
            ),
            ResultGridFilterOperator.GreaterThan => CompareFilterValues(value, filter) > 0,
            ResultGridFilterOperator.LessThan => CompareFilterValues(value, filter) < 0,
            ResultGridFilterOperator.GreaterThanOrEqual => CompareFilterValues(value, filter) >= 0,
            ResultGridFilterOperator.LessThanOrEqual => CompareFilterValues(value, filter) <= 0,
            _ => false,
        };
    }

    private int CompareFilterValues(string value, ColumnFilter filter)
    {
        var dataType =
            queryResult is not null && filter.ColumnIndex < queryResult.Columns.Count
                ? queryResult.Columns[filter.ColumnIndex].DataType
                : string.Empty;

        if (
            IsNumericType(dataType)
            && decimal.TryParse(
                value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var leftNumber
            )
            && decimal.TryParse(
                filter.Value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var rightNumber
            )
        )
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if (
            IsTemporalType(dataType)
            && DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out var leftInstant
            )
            && DateTimeOffset.TryParse(
                filter.Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out var rightInstant
            )
        )
        {
            return leftInstant
                .ToUnixTimeMilliseconds()
                .CompareTo(rightInstant.ToUnixTimeMilliseconds());
        }

        return StringComparer.OrdinalIgnoreCase.Compare(value, filter.Value);
    }

    private static ResultGridFilterOperator ToResultGridFilterOperator(FilterOperator op)
    {
        return op switch
        {
            FilterOperator.GreaterThan => ResultGridFilterOperator.GreaterThan,
            FilterOperator.LessThan => ResultGridFilterOperator.LessThan,
            _ => ResultGridFilterOperator.Contains,
        };
    }

    private SortKey GetSortValue(QueryRow row, int column)
    {
        if (column < 0 || column >= row.Values.Count)
        {
            return SortKey.Text(string.Empty);
        }

        var value = row.Values[column];
        if (value is null)
        {
            return SortKey.Text(string.Empty);
        }

        var dataType =
            queryResult is not null && column < queryResult.Columns.Count
                ? queryResult.Columns[column].DataType
                : string.Empty;
        if (
            IsNumericType(dataType)
            && decimal.TryParse(
                value,
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var number
            )
        )
        {
            return SortKey.Number(number);
        }

        if (
            IsTemporalType(dataType)
            && DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
                out var instant
            )
        )
        {
            return SortKey.Instant(instant.ToUnixTimeMilliseconds());
        }

        return SortKey.Text(value);
    }

    private static bool IsNullSortValue(QueryRow row, int column)
    {
        return column < 0 || column >= row.Values.Count || row.Values[column] is null;
    }

    private static bool IsNumericType(string dataType)
    {
        return dataType.Equals("Byte", StringComparison.OrdinalIgnoreCase)
            || dataType.Equals("SByte", StringComparison.OrdinalIgnoreCase)
            || dataType.Equals("Int16", StringComparison.OrdinalIgnoreCase)
            || dataType.Equals("UInt16", StringComparison.OrdinalIgnoreCase)
            || dataType.Equals("Int32", StringComparison.OrdinalIgnoreCase)
            || dataType.Equals("UInt32", StringComparison.OrdinalIgnoreCase)
            || dataType.Equals("Int64", StringComparison.OrdinalIgnoreCase)
            || dataType.Equals("UInt64", StringComparison.OrdinalIgnoreCase)
            || dataType.Equals("Single", StringComparison.OrdinalIgnoreCase)
            || dataType.Equals("Double", StringComparison.OrdinalIgnoreCase)
            || dataType.Equals("Decimal", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTemporalType(string dataType)
    {
        return dataType.Equals("DateTime", StringComparison.OrdinalIgnoreCase)
            || dataType.Equals("DateTimeOffset", StringComparison.OrdinalIgnoreCase)
            || dataType.Equals("DateOnly", StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct SortKey(
        int TypeRank,
        decimal NumberValue,
        long InstantValue,
        string TextValue
    ) : IComparable<SortKey>
    {
        public static SortKey Number(decimal value)
        {
            return new SortKey(0, value, 0, string.Empty);
        }

        public static SortKey Instant(long value)
        {
            return new SortKey(1, 0, value, string.Empty);
        }

        public static SortKey Text(string value)
        {
            return new SortKey(2, 0, 0, value);
        }

        public int CompareTo(SortKey other)
        {
            var typeCompare = TypeRank.CompareTo(other.TypeRank);
            if (typeCompare != 0)
            {
                return typeCompare;
            }

            return TypeRank switch
            {
                0 => NumberValue.CompareTo(other.NumberValue),
                1 => InstantValue.CompareTo(other.InstantValue),
                _ => StringComparer.OrdinalIgnoreCase.Compare(TextValue, other.TextValue),
            };
        }
    }

    private sealed record ColumnFilter(
        int ColumnIndex,
        ResultGridFilterOperator Operator,
        string Value
    );
}
