using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using CxSql.Models;

namespace CxSql.Database.Providers;

public abstract class DatabaseProviderBase : IDatabaseProvider, IPreviewSqlBuilder
{
    private const int MaxResultRows = 1_000;

    public abstract string ProviderName { get; }

    public abstract DbConnection CreateConnection(string connectionString);

    public abstract Task<IReadOnlyList<DatabaseObject>> GetDatabaseObjectsAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    );

    public abstract string BuildPreviewSql(DatabaseObject databaseObject, int rowLimit);

    public async Task<QueryResult> ExecuteSqlAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await EnsureOpenAsync(connection, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = new List<QueryColumn>();
        var rows = new List<QueryRow>();
        var messages = new List<SqlExecutionMessage>();
        var capturedResultSet = false;
        var resultSetIndex = 0;

        do
        {
            if (reader.FieldCount <= 0)
            {
                continue;
            }

            resultSetIndex++;

            if (capturedResultSet)
            {
                messages.Add(
                    new SqlExecutionMessage
                    {
                        Level = SqlExecutionMessageLevel.Warning,
                        Message =
                            $"Result set {resultSetIndex} was skipped in the MVP grid. Multiple result set viewing is scheduled for a later milestone.",
                    }
                );
                continue;
            }

            capturedResultSet = true;
            columns.AddRange(ReadColumns(reader));

            var truncated = false;
            while (await reader.ReadAsync(cancellationToken))
            {
                if (rows.Count >= MaxResultRows)
                {
                    truncated = true;
                    break;
                }

                rows.Add(ReadRow(reader));
            }

            if (truncated)
            {
                messages.Add(
                    new SqlExecutionMessage
                    {
                        Level = SqlExecutionMessageLevel.Warning,
                        Message =
                            $"Only the first {MaxResultRows.ToString(CultureInfo.InvariantCulture)} rows were loaded.",
                    }
                );
            }
        } while (await reader.NextResultAsync(cancellationToken));

        stopwatch.Stop();
        int? affectedRows = reader.RecordsAffected >= 0 ? reader.RecordsAffected : null;
        messages.Add(
            new SqlExecutionMessage
            {
                Level = SqlExecutionMessageLevel.Info,
                Message = capturedResultSet
                    ? $"Loaded {rows.Count.ToString(CultureInfo.InvariantCulture)} row(s)."
                    : $"Statement completed. Affected rows: {(affectedRows?.ToString(CultureInfo.InvariantCulture) ?? "unknown")}.",
            }
        );

        return new QueryResult
        {
            Columns = columns,
            Rows = rows,
            Messages = messages,
            AffectedRows = affectedRows,
            ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
        };
    }

    protected static async Task EnsureOpenAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }
    }

    protected static async Task<List<DatabaseObject>> ReadObjectsAsync(
        DbConnection connection,
        string sql,
        Func<DbDataReader, DatabaseObject> map,
        CancellationToken cancellationToken
    )
    {
        await EnsureOpenAsync(connection, cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var objects = new List<DatabaseObject>();
        while (await reader.ReadAsync(cancellationToken))
        {
            objects.Add(map(reader));
        }

        return objects;
    }

    protected static string QuoteDouble(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    protected static string QuoteBracket(string identifier)
    {
        return "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    private static IEnumerable<QueryColumn> ReadColumns(DbDataReader reader)
    {
        for (var index = 0; index < reader.FieldCount; index++)
        {
            yield return new QueryColumn
            {
                Ordinal = index,
                Name = reader.GetName(index),
                DataType = reader.GetFieldType(index).Name,
            };
        }
    }

    private static QueryRow ReadRow(DbDataReader reader)
    {
        var values = new List<string?>(reader.FieldCount);
        for (var index = 0; index < reader.FieldCount; index++)
        {
            values.Add(FormatValue(reader.GetValue(index)));
        }

        return new QueryRow { Values = values };
    }

    private static string? FormatValue(object value)
    {
        return value switch
        {
            DBNull => null,
            null => null,
            byte[] bytes => Convert.ToBase64String(bytes),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }
}
