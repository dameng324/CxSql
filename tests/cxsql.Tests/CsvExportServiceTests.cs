using CxSql.Application.Services;
using CxSql.Models;
using TUnit.Core;

namespace CxSql.Tests;

public sealed class CsvExportServiceTests
{
    [Test]
    public void ToCsvEscapesCommasQuotesAndLineBreaks()
    {
        var result = new QueryResult
        {
            Columns =
            [
                new QueryColumn
                {
                    Ordinal = 0,
                    Name = "name",
                    DataType = "String",
                },
                new QueryColumn
                {
                    Ordinal = 1,
                    Name = "note",
                    DataType = "String",
                },
            ],
            Rows =
            [
                new QueryRow { Values = ["alpha", "plain"] },
                new QueryRow { Values = ["beta,gamma", "quote \"inside\""] },
                new QueryRow { Values = ["delta", "line\r\nbreak"] },
            ],
        };

        var csv = new CsvExportService().ToCsv(result);

        RequireContains(csv, "name,note");
        RequireContains(csv, "\"beta,gamma\",\"quote \"\"inside\"\"\"");
        RequireContains(csv, "delta,\"line\r\nbreak\"");
    }

    private static void RequireContains(string value, string expected)
    {
        if (!value.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected CSV to contain: {expected}");
        }
    }
}
