using Microsoft.Extensions.Logging;

namespace CxSql.Infrastructure.Logging;

internal sealed class FileLoggerProvider(string filePath) : ILoggerProvider
{
    private readonly object gate = new();

    public ILogger CreateLogger(string categoryName) =>
        new FileLogger(categoryName, filePath, gate);

    public void Dispose() { }
}

internal sealed class FileLogger(string categoryName, string filePath, object gate) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        ArgumentNullException.ThrowIfNull(formatter);

        if (!IsEnabled(logLevel))
        {
            return;
        }

        var line =
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {logLevel}: {categoryName}: {formatter(state, exception)}";
        if (exception is not null)
        {
            line = $"{line}{Environment.NewLine}{exception}";
        }

        try
        {
            lock (gate)
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(filePath, line + Environment.NewLine);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (NotSupportedException) { }
    }
}
