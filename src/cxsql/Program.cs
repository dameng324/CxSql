using CxSql.Application.Services;
using CxSql.Infrastructure.Logging;
using CxSql.Infrastructure.Storage;
using CxSql.UI.Screens;
using Microsoft.Extensions.Logging;

namespace CxSql;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            PrintHelp();
            return 0;
        }

        ILoggerFactory? loggerFactory = null;

        try
        {
            var appPaths = AppPaths.CreateDefault();
            loggerFactory = CreateLoggerFactory(appPaths);
            var providerRegistry = DatabaseProviderRegistry.CreateDefault();
            var connectionManager = new ConnectionManager(
                new JsonFileConnectionStore(appPaths.ConnectionsFile),
                providerRegistry,
                loggerFactory.CreateLogger<ConnectionManager>()
            );
            var queryHistoryService = new QueryHistoryService(
                new JsonFileQueryHistoryStore(appPaths.QueryHistoryFile)
            );
            var queryExecutionService = new QueryExecutionService(
                providerRegistry,
                queryHistoryService,
                loggerFactory.CreateLogger<QueryExecutionService>()
            );
            var app = new SharpConsoleSqlClient(
                connectionManager,
                providerRegistry,
                queryExecutionService,
                queryHistoryService,
                new CsvExportService(),
                loggerFactory.CreateLogger<SharpConsoleSqlClient>()
            );

            return app.Run(args);
        }
        catch (Exception ex)
        {
            loggerFactory?.CreateLogger("cxsql").LogCritical(ex, "Unhandled application failure");
            Console.Error.WriteLine($"cxsql failed: {ex.Message}");
            return 1;
        }
        finally
        {
            loggerFactory?.Dispose();
        }
    }

    internal static ILoggerFactory CreateLoggerFactory(AppPaths appPaths) =>
        CreateLoggerFactory(appPaths, DateTimeOffset.Now);

    internal static ILoggerFactory CreateLoggerFactory(AppPaths appPaths, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(appPaths);

        LogFileMaintenance.DeleteExpiredLogs(appPaths.LogsDirectory, now, TimeSpan.FromDays(30));
        var logFilePath = LogFileMaintenance.CreateStartupLogFilePath(appPaths.LogsDirectory, now);

        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Warning);
            builder.AddProvider(new FileLoggerProvider(logFilePath));
        });
    }

    private static void PrintHelp()
    {
        Console.WriteLine("cxsql - cross-platform terminal SQL client");
        Console.WriteLine();
        Console.WriteLine("Run without arguments to open the TUI.");
        Console.WriteLine();
        Console.WriteLine("Visible shortcuts:");
        Console.WriteLine("  F5      Execute current SQL");
        Console.WriteLine("  Ctrl+S  Save SQL");
        Console.WriteLine("  Ctrl+Q  Exit");
        Console.WriteLine("  Esc     Close dialog");
        Console.WriteLine();
        Console.WriteLine("No other implicit shortcuts are assigned.");
    }
}
