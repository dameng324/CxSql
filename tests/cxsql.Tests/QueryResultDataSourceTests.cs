using CxSql.Models;
using CxSql.UI.Components;
using SharpConsoleUI.Controls;
using TUnit.Core;

namespace CxSql.Tests;

public sealed class QueryResultDataSourceTests
{
    [Test]
    public void SetResultKeepsDatabaseRowOrderUntilUserSorts()
    {
        var dataSource = new QueryResultDataSource();
        dataSource.SetResult(BuildSingleColumnResult("id", ["2", "1"]));

        if (dataSource.GetPlainCellValue(0, 0) != "2")
        {
            throw new InvalidOperationException("Result rows must keep database order by default.");
        }

        dataSource.Sort(0, SortDirection.Ascending);

        if (dataSource.GetPlainCellValue(0, 0) != "1")
        {
            throw new InvalidOperationException("Explicit user sorting should still sort rows.");
        }
    }

    [Test]
    public void VisibleResultReflectsCurrentFilterAndSort()
    {
        var dataSource = new QueryResultDataSource();
        dataSource.SetResult(BuildSingleColumnResult("name", ["beta", "alpha", "gamma"]));

        dataSource.Sort(0, SortDirection.Descending);
        dataSource.ApplyFilter("et", null, FilterOperator.Contains);

        var visible = dataSource.ToVisibleResult();
        if (visible is null || visible.Rows.Count != 1 || visible.Rows[0].Values[0] != "beta")
        {
            throw new InvalidOperationException(
                "Visible result should export the filtered and sorted grid state."
            );
        }
    }

    [Test]
    public void SortUsesColumnDataTypeForNumericValues()
    {
        var dataSource = new QueryResultDataSource();
        dataSource.SetResult(BuildSingleColumnResult("amount", ["10", "2", "1"], "Int32"));

        dataSource.Sort(0, SortDirection.Ascending);

        if (dataSource.GetPlainCellValue(1, 0) != "2")
        {
            throw new InvalidOperationException("Numeric columns should sort by numeric value.");
        }
    }

    private static QueryResult BuildSingleColumnResult(
        string columnName,
        IEnumerable<string> values,
        string dataType = "String"
    )
    {
        return new QueryResult
        {
            Columns =
            [
                new QueryColumn
                {
                    Ordinal = 0,
                    Name = columnName,
                    DataType = dataType,
                },
            ],
            Rows = values.Select(value => new QueryRow { Values = [value] }).ToList(),
        };
    }
}
