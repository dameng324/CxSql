namespace CxSql.Infrastructure.Logging;

internal static class LogFileMaintenance
{
    private const string LogFileSearchPattern = "cxsql*.log";

    public static string CreateStartupLogFilePath(string logsDirectory, DateTimeOffset now) =>
        Path.Combine(logsDirectory, $"cxsql-{now:yyyyMMdd-HHmmss}.log");

    public static void DeleteExpiredLogs(
        string logsDirectory,
        DateTimeOffset now,
        TimeSpan retention
    )
    {
        if (!Directory.Exists(logsDirectory))
        {
            return;
        }

        IReadOnlyList<string> logFiles;
        try
        {
            logFiles = Directory.EnumerateFiles(logsDirectory, LogFileSearchPattern).ToList();
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        var cutoffUtc = now.ToUniversalTime().Subtract(retention).UtcDateTime;
        foreach (var filePath in logFiles)
        {
            TryDeleteExpiredLog(filePath, cutoffUtc);
        }
    }

    private static void TryDeleteExpiredLog(string filePath, DateTime cutoffUtc)
    {
        try
        {
            if (File.GetLastWriteTimeUtc(filePath) < cutoffUtc)
            {
                File.Delete(filePath);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (NotSupportedException) { }
    }
}
