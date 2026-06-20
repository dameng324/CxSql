using CxSql.Application.Services;
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

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Warning);
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
        });

        try
        {
            var appPaths = AppPaths.CreateDefault();
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
                new CsvExportService(),
                loggerFactory.CreateLogger<SharpConsoleSqlClient>()
            );

            return app.Run(args);
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger("cxsql").LogCritical(ex, "Unhandled application failure");
            Console.Error.WriteLine($"cxsql failed: {ex.Message}");
            return 1;
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("cxsql - cross-platform terminal SQL client");
        Console.WriteLine();
        Console.WriteLine("Run without arguments to open the TUI.");
        Console.WriteLine();
        Console.WriteLine("Visible shortcuts:");
        Console.WriteLine("  F5      Execute current SQL");
        Console.WriteLine("  Ctrl+N  New query");
        Console.WriteLine("  Ctrl+S  Save SQL");
        Console.WriteLine("  Esc     Close dialog");
        Console.WriteLine();
        Console.WriteLine("No other implicit shortcuts are assigned.");
    }
}
