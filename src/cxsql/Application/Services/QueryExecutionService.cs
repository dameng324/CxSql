using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using CxSql.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CxSql.Application.Services;

public sealed class QueryExecutionService(
    DatabaseProviderRegistry providerRegistry,
    QueryHistoryService queryHistoryService,
    ILogger<QueryExecutionService> logger
)
{
    public async Task<QueryResult> ExecuteAsync(
        DatabaseConnection databaseConnection,
        string sql,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return QueryResult.Failure("SQL text is required.", 0);
        }

        var stopwatch = Stopwatch.StartNew();
        QueryResult result;

        try
        {
            var provider = providerRegistry.GetProvider(databaseConnection.DatabaseType);
            await using var connection = provider.CreateConnection(
                databaseConnection.ConnectionString
            );
            result = await provider.ExecuteSqlAsync(connection, sql, cancellationToken);
        }
        catch (DbException ex)
        {
            stopwatch.Stop();
            logger.LogWarning(
                ex,
                "SQL execution failed for connection {ConnectionName}",
                databaseConnection.Name
            );
            result = QueryResult.Failure(
                ex.Message,
                stopwatch.ElapsedMilliseconds,
                GetProviderErrorCode(ex)
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            logger.LogError(
                ex,
                "Unexpected SQL execution failure for connection {ConnectionName}",
                databaseConnection.Name
            );
            result = QueryResult.Failure(ex.Message, stopwatch.ElapsedMilliseconds);
        }

        await queryHistoryService.RecordAsync(
            new QueryHistory
            {
                SqlText = sql,
                ConnectionName = databaseConnection.Name,
                ExecutionElapsedMilliseconds = result.ElapsedMilliseconds,
                Succeeded = result.Success,
                ErrorMessage = result.ErrorMessage,
                TimestampUnixMs = UnixTime.NowMilliseconds(),
            },
            cancellationToken
        );

        return result;
    }

    private static string GetProviderErrorCode(DbException exception)
    {
        return exception switch
        {
            SqliteException sqliteException => sqliteException.SqliteErrorCode.ToString(
                CultureInfo.InvariantCulture
            ),
            PostgresException postgresException => postgresException.SqlState,
            SqlException sqlException => sqlException.Number.ToString(CultureInfo.InvariantCulture),
            _ => exception.ErrorCode.ToString(CultureInfo.InvariantCulture),
        };
    }
}
