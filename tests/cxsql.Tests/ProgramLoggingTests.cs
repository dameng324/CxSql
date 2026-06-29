using CxSql.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using TUnit.Core;

namespace CxSql.Tests;

public sealed class ProgramLoggingTests
{
    [Test]
    public void CreateLoggerFactoryWritesWarningsToTimestampedLogFile()
    {
        var directory = CreateTempDirectory();
        var appPaths = new AppPaths(directory);
        var now = new DateTimeOffset(2026, 6, 29, 14, 30, 15, TimeSpan.FromHours(8));

        try
        {
            using var loggerFactory = Program.CreateLoggerFactory(appPaths, now);
            var logger = loggerFactory.CreateLogger("CxSql.Tests.Logging");

            logger.LogInformation("info should be ignored");
            logger.LogWarning("connection failed for {ConnectionName}", "broken");

            var logFile = Path.Combine(appPaths.LogsDirectory, "cxsql-20260629-143015.log");
            if (!File.Exists(logFile))
            {
                throw new InvalidOperationException(
                    "Expected warning logs to be written to the startup log file."
                );
            }

            var contents = File.ReadAllText(logFile);
            RequireContains(contents, "Warning: CxSql.Tests.Logging: connection failed for broken");
            RequireDoesNotContain(contents, "info should be ignored");
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    [Test]
    public void CreateLoggerFactoryDeletesCxSqlLogsOlderThanThirtyDays()
    {
        var directory = CreateTempDirectory();
        var appPaths = new AppPaths(directory);
        var now = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);

        try
        {
            Directory.CreateDirectory(appPaths.LogsDirectory);
            var expiredLog = Path.Combine(appPaths.LogsDirectory, "cxsql-20260501-120000.log");
            var expiredLegacyLog = Path.Combine(appPaths.LogsDirectory, "cxsql.log");
            var retainedLog = Path.Combine(appPaths.LogsDirectory, "cxsql-20260615-120000.log");
            var unrelatedLog = Path.Combine(appPaths.LogsDirectory, "other.log");

            WriteLogFile(expiredLog, now.AddDays(-31));
            WriteLogFile(expiredLegacyLog, now.AddDays(-31));
            WriteLogFile(retainedLog, now.AddDays(-29));
            WriteLogFile(unrelatedLog, now.AddDays(-31));

            using var loggerFactory = Program.CreateLoggerFactory(appPaths, now);

            RequireFileMissing(expiredLog);
            RequireFileMissing(expiredLegacyLog);
            RequireFileExists(retainedLog);
            RequireFileExists(unrelatedLog);
        }
        finally
        {
            TryDeleteDirectory(directory);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "cxsql-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void RequireContains(string value, string expected)
    {
        if (!value.Contains(expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected log to contain: {expected}");
        }
    }

    private static void RequireDoesNotContain(string value, string unexpected)
    {
        if (value.Contains(unexpected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Expected log not to contain: {unexpected}");
        }
    }

    private static void RequireFileExists(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new InvalidOperationException($"Expected file to exist: {filePath}");
        }
    }

    private static void RequireFileMissing(string filePath)
    {
        if (File.Exists(filePath))
        {
            throw new InvalidOperationException($"Expected file to be deleted: {filePath}");
        }
    }

    private static void WriteLogFile(string filePath, DateTimeOffset lastWriteTime)
    {
        File.WriteAllText(filePath, "log");
        File.SetLastWriteTimeUtc(filePath, lastWriteTime.UtcDateTime);
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
