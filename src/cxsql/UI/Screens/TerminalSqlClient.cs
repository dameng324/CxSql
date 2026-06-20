using System.Text;
using CxSql.Application.Services;
using CxSql.Models;
using CxSql.UI.Components;
using CxSql.UI.Dialogs;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace CxSql.UI.Screens;

public sealed class TerminalSqlClient(
    ConnectionManager connectionManager,
    DatabaseProviderRegistry providerRegistry,
    QueryExecutionService queryExecutionService,
    CsvExportService csvExportService,
    ILogger<TerminalSqlClient> logger
)
{
    private readonly ObjectTreeRenderer objectTreeRenderer = new();
    private readonly ResultGridRenderer resultGridRenderer = new();
    private DatabaseConnection? activeConnection;
    private IReadOnlyList<DatabaseObject> currentObjects = [];
    private QueryResult? lastResult;
    private string currentSql = "SELECT 1;";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Render();
            var action = ReadToolbarAction();
            if (action == ToolbarAction.Exit)
            {
                return 0;
            }

            await ExecuteToolbarActionAsync(action, cancellationToken);
        }

        return 0;
    }

    private async Task ExecuteToolbarActionAsync(
        ToolbarAction action,
        CancellationToken cancellationToken
    )
    {
        try
        {
            switch (action)
            {
                case ToolbarAction.NewConnection:
                    await NewConnectionAsync(cancellationToken);
                    break;
                case ToolbarAction.OpenConnection:
                    await OpenConnectionAsync(cancellationToken);
                    break;
                case ToolbarAction.NewQuery:
                    NewQuery();
                    break;
                case ToolbarAction.Execute:
                    await ExecuteCurrentSqlAsync(cancellationToken);
                    break;
                case ToolbarAction.Stop:
                    ShowNotice("Stop", "No SQL execution is currently running.");
                    break;
                case ToolbarAction.SaveSql:
                    await SaveSqlAsync(cancellationToken);
                    break;
                case ToolbarAction.Export:
                    await ExportAsync(cancellationToken);
                    break;
                case ToolbarAction.Refresh:
                    await RefreshObjectsAsync(cancellationToken);
                    break;
                case ToolbarAction.ListConnections:
                    await ListConnectionsAsync(cancellationToken);
                    break;
                case ToolbarAction.Exit:
                    break;
                default:
                    ShowNotice("Unknown action", "Select one of the visible toolbar buttons.");
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Toolbar action {ToolbarAction} failed", action);
            ErrorDialog.Show("Action failed", ex.Message, ex.ToString());
        }
    }

    private void Render()
    {
        SafeClear();
        Console.WriteLine("cxsql - terminal SQL client");
        Console.WriteLine(
            $"UI framework: {ConsoleExShell.FrameworkName}; planned controls: {string.Join(", ", ConsoleExShell.PlannedControls)}"
        );
        Console.WriteLine(
            "Primary interaction: mouse first. Console fallback: select visible button number."
        );
        Console.WriteLine();
        RenderToolbar();
        Console.WriteLine();
        Console.WriteLine(
            "+-- Connection & Object Tree -------------+-- SQL Editor Tabs -------------"
        );
        Console.WriteLine(
            $"| Active: {activeConnection?.Name ?? "(none)"}".PadRight(42) + $"| Query: default"
        );
        Console.WriteLine(
            "+-----------------------------------------+--------------------------------"
        );
        RenderObjectTreePreview();
        Console.WriteLine(
            "+-- Result Grid / Messages / Execution Log --------------------------------"
        );
        RenderResultPreview();
        Console.WriteLine(
            "+-------------------------------------------------------------------------"
        );
        Console.WriteLine(
            "Select a toolbar button number or use a shortcut shown next to a button."
        );
    }

    private static void RenderToolbar()
    {
        Console.WriteLine("Toolbar:");
        Console.WriteLine(
            string.Join(
                " | ",
                ToolbarCatalog.Buttons.Select(button => $"[{button.ButtonKey}] {button.Caption}")
            )
        );
    }

    private void RenderObjectTreePreview()
    {
        using var writer = new StringWriter();
        objectTreeRenderer.Render(currentObjects, writer);
        foreach (var line in writer.ToString().Split(Environment.NewLine).Take(8))
        {
            if (!string.IsNullOrEmpty(line))
            {
                Console.WriteLine($"| {line}");
            }
        }
    }

    private void RenderResultPreview()
    {
        if (lastResult is null)
        {
            Console.WriteLine("No query result yet.");
            Console.WriteLine("Current SQL:");
            Console.WriteLine(currentSql);
            return;
        }

        resultGridRenderer.Render(lastResult, Console.Out);
    }

    private static ToolbarAction ReadToolbarAction()
    {
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (ToolbarCatalog.TryGetAction(key.KeyChar, out var toolbarAction))
            {
                return toolbarAction;
            }

            if (ShortcutPolicy.TryGetToolbarAction(key, out toolbarAction))
            {
                return toolbarAction;
            }

            Console.WriteLine(
                "That key is not assigned. Use a visible toolbar button or shown shortcut."
            );
        }
    }

    private async Task NewConnectionAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine("New Connection dialog");
        Console.WriteLine("Close Dialog (Esc) is available in ConsoleEx dialog mode.");
        Console.WriteLine("[1] SQLite | [2] PostgreSQL | [3] SQL Server");
        Console.Write("Select database type button: ");
        var databaseType = Console.ReadLine()?.Trim() switch
        {
            "1" => DatabaseType.Sqlite,
            "2" => DatabaseType.PostgreSql,
            "3" => DatabaseType.SqlServer,
            _ => throw new InvalidOperationException("Select a visible database type button."),
        };

        Console.Write("Connection name: ");
        var name = ReadNonEmptyLine("Connection name is required.");
        var connectionString =
            databaseType == DatabaseType.Sqlite
                ? ReadSqliteConnectionString()
                : ReadConnectionString();

        var connection = await connectionManager.CreateAsync(
            name,
            databaseType,
            connectionString,
            cancellationToken
        );
        activeConnection = connection;

        var test = await connectionManager.TestAsync(connection, cancellationToken);
        if (!test.Succeeded)
        {
            ErrorDialog.Show("Connection test failed", test.ErrorMessage ?? "Unknown error.");
            return;
        }

        await RefreshObjectsAsync(cancellationToken);
    }

    private async Task OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connections = await connectionManager.ListAsync(cancellationToken);
        if (connections.Count == 0)
        {
            ShowNotice("Open Connection", "No saved connections. Use New Connection first.");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Open Connection dialog");
        for (var index = 0; index < connections.Count; index++)
        {
            Console.WriteLine(
                $"[{index + 1}] {connections[index].Name} ({connections[index].DatabaseType})"
            );
        }

        Console.Write("Select connection button: ");
        if (
            !int.TryParse(Console.ReadLine(), out var selected)
            || selected < 1
            || selected > connections.Count
        )
        {
            throw new InvalidOperationException("Select a visible connection button.");
        }

        activeConnection = connections[selected - 1];
        var test = await connectionManager.TestAsync(activeConnection, cancellationToken);
        if (!test.Succeeded)
        {
            ErrorDialog.Show("Connection failed", test.ErrorMessage ?? "Unknown error.");
            return;
        }

        await RefreshObjectsAsync(cancellationToken);
    }

    private void NewQuery()
    {
        Console.WriteLine();
        Console.WriteLine("SQL Editor");
        Console.WriteLine("Paste or type SQL. Button: Finish SQL input [empty line].");
        Console.WriteLine(
            "Common actions remain visible on toolbar: Execute (F5), Save SQL (Ctrl+S)."
        );
        var builder = new StringBuilder();
        while (true)
        {
            var line = Console.ReadLine();
            if (line is null || line.Length == 0)
            {
                break;
            }

            builder.AppendLine(line);
        }

        var sql = builder.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(sql))
        {
            currentSql = sql;
            lastResult = null;
        }
    }

    private async Task ExecuteCurrentSqlAsync(CancellationToken cancellationToken)
    {
        if (activeConnection is null)
        {
            ShowNotice("Execute", "Open a connection before executing SQL.");
            return;
        }

        lastResult = await queryExecutionService.ExecuteAsync(
            activeConnection,
            currentSql,
            cancellationToken
        );

        if (!lastResult.Success)
        {
            ErrorDialog.Show(
                "SQL execution failed",
                lastResult.ErrorMessage ?? "Unknown SQL error.",
                lastResult.ProviderErrorCode is null
                    ? null
                    : $"Provider error code: {lastResult.ProviderErrorCode}"
            );
        }
    }

    private async Task SaveSqlAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine();
        Console.WriteLine("Save SQL dialog");
        Console.Write("File path: ");
        var filePath = ReadNonEmptyLine("File path is required.");
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(filePath, currentSql, Encoding.UTF8, cancellationToken);
        ShowNotice("Save SQL", $"Saved to {filePath}.");
    }

    private async Task ExportAsync(CancellationToken cancellationToken)
    {
        if (lastResult is null || lastResult.Columns.Count == 0)
        {
            ShowNotice("Export", "Run a query that returns rows before exporting.");
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Export Options dialog");
        Console.WriteLine("Format: CSV, UTF-8, header row, escaped commas/quotes/CRLF.");
        Console.Write("CSV file path: ");
        var filePath = ReadNonEmptyLine("CSV file path is required.");
        await csvExportService.ExportAsync(lastResult, filePath, cancellationToken);
        ShowNotice("Export", $"Exported to {filePath}.");
    }

    private async Task RefreshObjectsAsync(CancellationToken cancellationToken)
    {
        if (activeConnection is null)
        {
            ShowNotice("Refresh", "Open a connection before refreshing.");
            return;
        }

        var provider = providerRegistry.GetProvider(activeConnection.DatabaseType);
        await using var connection = provider.CreateConnection(activeConnection.ConnectionString);
        currentObjects = await provider.GetDatabaseObjectsAsync(connection, cancellationToken);
    }

    private async Task ListConnectionsAsync(CancellationToken cancellationToken)
    {
        var connections = await connectionManager.ListAsync(cancellationToken);
        Console.WriteLine();
        Console.WriteLine("Connections");
        if (connections.Count == 0)
        {
            Console.WriteLine("No saved connections.");
        }
        else
        {
            foreach (var connection in connections)
            {
                Console.WriteLine($"- {connection.Name} ({connection.DatabaseType})");
            }
        }

        WaitForVisibleClose();
    }

    private static string ReadSqliteConnectionString()
    {
        Console.Write("SQLite database file path: ");
        var databasePath = ReadNonEmptyLine("SQLite database file path is required.");
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWriteCreate,
        };
        return builder.ToString();
    }

    private static string ReadConnectionString()
    {
        Console.Write("Connection string: ");
        return ReadNonEmptyLine("Connection string is required.");
    }

    private static string ReadNonEmptyLine(string errorMessage)
    {
        var value = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return value;
    }

    private static void ShowNotice(string title, string message)
    {
        Console.WriteLine();
        Console.WriteLine($"{title}: {message}");
        WaitForVisibleClose();
    }

    private static void WaitForVisibleClose()
    {
        Console.WriteLine("Close Dialog (Esc)");
        while (Console.ReadKey(intercept: true).Key != ConsoleKey.Escape)
        {
            Console.WriteLine("Use the visible Close Dialog action: Esc.");
        }
    }

    private static void SafeClear()
    {
        try
        {
            Console.Clear();
        }
        catch (IOException) { }
    }
}
