using SharpConsoleUI.Controls;

namespace CxSql.UI.Components;

public enum ResultGridFilterOperator
{
    Equal,
    Contains,
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual,
}

public sealed record ResultGridFilterRequest(
    string ColumnName,
    ResultGridFilterOperator Operator,
    string Value
);

public sealed record ResultGridSortRequest(string ColumnName, SortDirection Direction);
