using CxSql.Application.Services;
using CxSql.Models;
using CxSql.UI.Components;
using CxSql.UI.Dialogs;
using Microsoft.Extensions.Logging;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Configuration;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Core;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Highlighting;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Rendering;

namespace CxSql.UI.Screens;

public sealed class SharpConsoleSqlClient(
    ConnectionManager connectionManager,
    DatabaseProviderRegistry providerRegistry,
    QueryExecutionService queryExecutionService,
    CsvExportService csvExportService,
    ILogger<SharpConsoleSqlClient> logger
)
{
    private readonly List<QueryEditorTab> queryTabs = [];
    private readonly ObjectExplorerPanel objectExplorer = new();
    private readonly QueryResultDataSource resultDataSource = new();
    private readonly ConnectionSummaryDataSource connectionDataSource = new();

    private ConsoleWindowSystem windowSystem = null!;
    private Window mainWindow = null!;
    private ToolbarControl toolbar = null!;
    private TabControl editorTabs = null!;
    private TabControl bottomTabs = null!;
    private TableControl resultTable = null!;
    private TableControl connectionTable = null!;
    private MarkupControl messageLog = null!;
    private MarkupControl statusLine = null!;

    private IReadOnlyList<DatabaseConnection> connections = [];
    private IReadOnlyList<DatabaseObject> currentObjects = [];
    private DatabaseConnection? activeConnection;
    private QueryResult? lastResult;
    private CancellationTokenSource? executionCts;
    private int queryNumber;

    public int Run(string[] args)
    {
        PtyShim.RunIfShim(args);

        if (!IsInteractiveTerminal())
        {
            Console.Error.WriteLine("cxsql requires an interactive terminal.");
            return 1;
        }

        var driver = new NetConsoleDriver(RenderMode.Buffer);
        windowSystem = new ConsoleWindowSystem(
            driver,
            options: new ConsoleWindowSystemOptions(
                ShowTopPanel: false,
                ShowBottomPanel: false,
                WindowCycleKey: null,
                InstallSynchronizationContext: true
            )
        );

        BuildUi();
        windowSystem.AddWindow(mainWindow);
        windowSystem.SetActiveWindow(mainWindow);

        _ = LoadConnectionsAsync();
        windowSystem.Run();
        return 0;
    }

    private void BuildUi()
    {
        toolbar = BuildToolbar();
        var topRule = Controls.RuleBuilder().StickyTop().WithColor(Color.Grey27).Build();
        var bottomRule = Controls.RuleBuilder().StickyBottom().WithColor(Color.Grey27).Build();

        objectExplorer.ConnectionSelected += connection => _ = OpenConnectionAsync(connection);
        objectExplorer.ObjectActivated += databaseObject => _ = PreviewObjectAsync(databaseObject);
        objectExplorer.ObjectRightClicked += (databaseObject, _) =>
            ShowObjectContext(databaseObject);
        objectExplorer.ConnectionRightClicked += (connection, _) =>
            ShowConnectionContext(connection);

        editorTabs = new TabControlBuilder().WithHeaderStyle(TabHeaderStyle.Classic).Fill().Build();
        editorTabs.HorizontalAlignment = HorizontalAlignment.Stretch;
        editorTabs.VerticalAlignment = VerticalAlignment.Fill;
        editorTabs.BackgroundColor = Color.Transparent;
        editorTabs.TabCloseRequested += (_, args) =>
        {
            if (editorTabs.TabCount <= 1)
            {
                ShowNotification(
                    "Query",
                    "At least one query tab must stay open.",
                    NotificationSeverity.Warning
                );
                return;
            }

            queryTabs.RemoveAll(item => ReferenceEquals(item.Editor, args.TabPage.Content));
            editorTabs.RemoveTab(args.Index);
            UpdateStatus("Query tab closed.");
        };

        CreateQueryTab(isInitial: true);

        resultTable = Controls
            .Table()
            .WithDataSource(resultDataSource)
            .Interactive()
            .WithCellNavigation()
            .WithSorting()
            .WithFiltering()
            .WithFuzzyFilter()
            .WithColumnResize()
            .WithColumnSeparator('|', Color.Grey27)
            .WithBorderStyle(BorderStyle.None)
            .WithHeaderColors(Color.Grey70, new Color(25, 35, 55))
            .WithHorizontalScrollbar(ScrollbarVisibility.Auto)
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithHorizontalAlignment(HorizontalAlignment.Stretch)
            .Build();
        resultTable.TruncationFade = true;
        resultTable.ClearSelectionOnEmptyClick = true;
        resultTable.MouseRightClick += (_, _) => ShowResultGridContext();

        messageLog = Controls
            .Markup()
            .AddLine("[grey70]Messages will appear here after execution.[/]")
            .WithMargin(1, 0, 1, 0)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Build();

        connectionTable = Controls
            .Table()
            .WithDataSource(connectionDataSource)
            .Interactive()
            .WithSorting()
            .WithFiltering()
            .WithFuzzyFilter()
            .WithColumnSeparator('|', Color.Grey27)
            .WithBorderStyle(BorderStyle.None)
            .WithHeaderColors(Color.Grey70, new Color(25, 35, 55))
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithHorizontalAlignment(HorizontalAlignment.Stretch)
            .Build();
        connectionTable.RowActivated += (_, rowIndex) =>
        {
            if (connectionDataSource.GetRowTag(rowIndex) is DatabaseConnection connection)
            {
                _ = OpenConnectionAsync(connection);
            }
        };

        bottomTabs = new TabControlBuilder()
            .WithHeaderStyle(TabHeaderStyle.Classic)
            .AddTab("Result Grid", resultTable)
            .AddTab("Messages", messageLog)
            .AddTab("Connections", connectionTable)
            .Fill()
            .Build();
        bottomTabs.HorizontalAlignment = HorizontalAlignment.Stretch;
        bottomTabs.VerticalAlignment = VerticalAlignment.Fill;
        bottomTabs.BackgroundColor = Color.Transparent;

        var editorStack = Controls
            .HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Column(column =>
            {
                editorTabs.Height = 13;
                column.Add(editorTabs);
                column.Add(Controls.HorizontalSplitter().WithMinHeights(8, 6).Build());
                column.Add(bottomTabs);
            })
            .Build();

        var leftHeader = Controls
            .StatusBar()
            .AddLeftText("[grey70]Connection & Object Tree[/]")
            .WithMargin(1, 0, 0, 0)
            .Build();
        leftHeader.BackgroundColor = new Color(40, 50, 70, 160);

        var mainGrid = Controls
            .HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Column(column => column.Width(34).Add(leftHeader).Add(objectExplorer.Control))
            .Column(column => column.Flex(1).Add(editorStack))
            .WithSplitterAfter(0)
            .Build();

        foreach (var splitter in mainGrid.Splitters)
        {
            splitter.ForegroundColor = Color.Grey27;
        }

        statusLine = Controls
            .Markup("[grey70]Ready. Mouse-first UI. Shortcuts shown on toolbar only.[/]")
            .StickyBottom()
            .WithMargin(1, 0, 1, 0)
            .Build();

        var gradient = ColorGradient.FromColors(new Color(25, 32, 52), new Color(7, 7, 13));

        mainWindow = new WindowBuilder(windowSystem)
            .HideTitle()
            .HideTitleButtons()
            .WithBorderStyle(BorderStyle.Rounded)
            .WithBorderColor(Color.Grey27)
            .Maximized()
            .Movable(false)
            .Resizable(false)
            .Minimizable(false)
            .Maximizable(false)
            .WithBackgroundGradient(gradient, GradientDirection.Vertical)
            .AddControl(toolbar)
            .AddControl(topRule)
            .AddControl(mainGrid)
            .AddControl(bottomRule)
            .AddControl(statusLine)
            .OnKeyPressed(OnGlobalKeyPressed)
            .Build();
    }

    private ToolbarControl BuildToolbar()
    {
        var builder = Controls
            .Toolbar()
            .StickyTop()
            .WithSpacing(1)
            .WithWrap()
            .WithMargin(1, 0, 1, 0)
            .WithBackgroundColor(Color.Transparent)
            .WithBelowLineColor(Color.Grey27);

        AddToolbarButton(builder, "New Connection", () => _ = NewConnectionAsync());
        AddToolbarButton(builder, "Open Connection", () => _ = SelectConnectionAsync());
        AddToolbarButton(
            builder,
            "New Query [grey50]Ctrl+N[/]",
            () => CreateQueryTab(isInitial: false)
        );
        AddToolbarButton(builder, "Execute [grey50]F5[/]", () => _ = ExecuteCurrentSqlAsync());
        AddToolbarButton(builder, "Stop", StopExecution);
        AddToolbarButton(builder, "Save SQL [grey50]Ctrl+S[/]", () => _ = SaveSqlAsync());
        AddToolbarButton(builder, "Export", () => _ = ExportCsvAsync());
        AddToolbarButton(builder, "Refresh", () => _ = RefreshObjectsAsync());

        return builder.Build();
    }

    private static void AddToolbarButton(ToolbarBuilder builder, string label, Action action)
    {
        builder.AddButton(label, (_, _) => action());
    }

    private void CreateQueryTab(bool isInitial)
    {
        queryNumber++;
        var title = isInitial ? "Query 1" : $"Query {queryNumber}";
        var editorBuilder = Controls
            .MultilineEdit(isInitial ? "SELECT 1;" : string.Empty)
            .WithLineNumbers(true)
            .WithHighlightCurrentLine(true)
            .WithAutoIndent(true)
            .WithEditingHints(true)
            .WithEscapeExitsEditMode(true)
            .NoWrap()
            .WithHorizontalScrollbar(ScrollbarVisibility.Auto)
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .WithBackgroundColor(Color.Transparent)
            .WithForegroundColor(Color.White)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithAlignment(HorizontalAlignment.Stretch);

        var syntaxHighlighter = SyntaxHighlighters.For("sql");
        if (syntaxHighlighter is not null)
        {
            editorBuilder.WithSyntaxHighlighter(syntaxHighlighter);
        }

        var editor = editorBuilder.Build();

        var tab = new QueryEditorTab(title, editor);
        queryTabs.Add(tab);
        editorTabs.AddTab(title, editor, isClosable: !isInitial);
        editorTabs.ActiveTabIndex = editorTabs.TabCount - 1;
        editor.RequestFocus();
        UpdateStatus($"{title} opened. Click Execute or press F5.");
    }

    private async Task LoadConnectionsAsync()
    {
        connections = await connectionManager.ListAsync(CancellationToken.None);
        connectionDataSource.SetConnections(connections);
        objectExplorer.SetConnections(connections, activeConnection, currentObjects);
        UpdateStatus(
            connections.Count == 0
                ? "No connections yet. Click New Connection."
                : "Connections loaded."
        );
    }

    private async Task NewConnectionAsync()
    {
        var request = await NewConnectionModal.ShowAsync(windowSystem, mainWindow);
        if (request is null)
        {
            return;
        }

        try
        {
            var connection = await connectionManager.CreateAsync(
                request.Name,
                request.DatabaseType,
                request.ConnectionString,
                CancellationToken.None
            );

            activeConnection = connection;
            connections = await connectionManager.ListAsync(CancellationToken.None);
            connectionDataSource.SetConnections(connections);
            await RefreshObjectsAsync();
            ShowNotification(
                "Connection",
                $"Created {connection.Name}.",
                NotificationSeverity.Success
            );
        }
        catch (Exception ex)
        {
            ShowError("New connection failed", ex);
        }
    }

    private async Task SelectConnectionAsync()
    {
        if (connections.Count == 0)
        {
            ShowNotification(
                "Open Connection",
                "No saved connections. Click New Connection.",
                NotificationSeverity.Warning
            );
            bottomTabs.ActiveTabIndex = 2;
            connectionTable.RequestFocus();
            return;
        }

        bottomTabs.ActiveTabIndex = 2;
        connectionTable.RequestFocus();
        UpdateStatus(
            "Double-click a row in Connections or select a connection in the object tree."
        );
        await Task.CompletedTask;
    }

    private async Task OpenConnectionAsync(DatabaseConnection connection)
    {
        activeConnection = connection;
        var test = await connectionManager.TestAsync(connection, CancellationToken.None);
        if (!test.Succeeded)
        {
            UpdateStatus($"Connection failed: {test.ErrorMessage}");
            ShowNotification(
                "Connection failed",
                test.ErrorMessage ?? "Unknown error.",
                NotificationSeverity.Danger
            );
            return;
        }

        await RefreshObjectsAsync();
        ShowNotification("Connection opened", connection.Name, NotificationSeverity.Success);
    }

    private async Task RefreshObjectsAsync()
    {
        if (activeConnection is null)
        {
            currentObjects = [];
            objectExplorer.SetConnections(connections, activeConnection, currentObjects);
            UpdateStatus("Open a connection before refreshing objects.");
            return;
        }

        try
        {
            var provider = providerRegistry.GetProvider(activeConnection.DatabaseType);
            await using var connection = provider.CreateConnection(
                activeConnection.ConnectionString
            );
            currentObjects = await provider.GetDatabaseObjectsAsync(
                connection,
                CancellationToken.None
            );
            objectExplorer.SetConnections(connections, activeConnection, currentObjects);
            UpdateStatus($"Loaded {currentObjects.Count} object(s) from {activeConnection.Name}.");
        }
        catch (Exception ex)
        {
            ShowError("Refresh failed", ex);
        }
    }

    private async Task PreviewObjectAsync(DatabaseObject databaseObject)
    {
        if (activeConnection is null)
        {
            return;
        }

        try
        {
            var previewSql = providerRegistry
                .GetPreviewSqlBuilder(activeConnection.DatabaseType)
                .BuildPreviewSql(databaseObject, 100);
            ActiveEditor?.SetContent(previewSql);
            await ExecuteCurrentSqlAsync();
        }
        catch (Exception ex)
        {
            ShowError("Preview failed", ex);
        }
    }

    private async Task ExecuteCurrentSqlAsync()
    {
        if (executionCts is not null)
        {
            ShowNotification(
                "Execute",
                "A query is already running.",
                NotificationSeverity.Warning
            );
            return;
        }

        if (activeConnection is null)
        {
            ShowNotification("Execute", "Open a connection first.", NotificationSeverity.Warning);
            return;
        }

        var editor = ActiveEditor;
        if (editor is null || string.IsNullOrWhiteSpace(editor.Content))
        {
            ShowNotification(
                "Execute",
                "The current SQL editor is empty.",
                NotificationSeverity.Warning
            );
            return;
        }

        executionCts = new CancellationTokenSource();
        UpdateStatus("Executing SQL...");

        try
        {
            lastResult = await queryExecutionService.ExecuteAsync(
                activeConnection,
                editor.Content,
                executionCts.Token
            );
            resultDataSource.SetResult(lastResult);
            bottomTabs.ActiveTabIndex = lastResult.Success ? 0 : 1;
            UpdateMessages(lastResult);
            UpdateStatus(
                lastResult.Success
                    ? $"Executed in {lastResult.ElapsedMilliseconds} ms. Rows: {lastResult.Rows.Count}."
                    : $"SQL failed: {lastResult.ErrorMessage}"
            );
        }
        catch (OperationCanceledException)
        {
            UpdateStatus("Execution canceled.");
            ShowNotification("Execute", "Execution canceled.", NotificationSeverity.Warning);
        }
        finally
        {
            executionCts?.Dispose();
            executionCts = null;
        }
    }

    private void StopExecution()
    {
        if (executionCts is null)
        {
            ShowNotification("Stop", "No SQL execution is running.", NotificationSeverity.Warning);
            return;
        }

        executionCts.Cancel();
    }

    private async Task SaveSqlAsync()
    {
        var editor = ActiveEditor;
        if (editor is null)
        {
            return;
        }

        var path = await TextInputModal.ShowAsync(
            windowSystem,
            mainWindow,
            "Save SQL",
            "File path: ",
            "query.sql"
        );
        if (path is null)
        {
            return;
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(path, editor.Content);
            ShowNotification("Save SQL", $"Saved to {path}.", NotificationSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowError("Save SQL failed", ex);
        }
    }

    private async Task ExportCsvAsync()
    {
        if (lastResult is null || lastResult.Columns.Count == 0)
        {
            ShowNotification(
                "Export",
                "Run a query that returns rows first.",
                NotificationSeverity.Warning
            );
            return;
        }

        var path = await TextInputModal.ShowAsync(
            windowSystem,
            mainWindow,
            "Export CSV",
            "CSV path: ",
            "query-result.csv"
        );
        if (path is null)
        {
            return;
        }

        try
        {
            await csvExportService.ExportAsync(lastResult, path, CancellationToken.None);
            ShowNotification("Export", $"Exported to {path}.", NotificationSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowError("Export failed", ex);
        }
    }

    private void ShowConnectionContext(DatabaseConnection connection)
    {
        ShowNotification(
            "Connection menu",
            $"Right-click: Open/Refresh/Edit/Delete planned. Opening {connection.Name}.",
            NotificationSeverity.Info
        );
        _ = OpenConnectionAsync(connection);
    }

    private void ShowObjectContext(DatabaseObject databaseObject)
    {
        ShowNotification(
            "Table menu",
            $"{databaseObject.DisplayName}: double-click previews data; context actions are queued for the next pass.",
            NotificationSeverity.Info
        );
    }

    private void ShowResultGridContext()
    {
        ShowNotification(
            "Result grid",
            "Use Export button for CSV. Copy cell/row context menu is queued for the next pass.",
            NotificationSeverity.Info
        );
    }

    private void UpdateMessages(QueryResult result)
    {
        var lines = new List<string>
        {
            result.Success ? "[green]Success[/]" : "[red]Failed[/]",
            $"Elapsed: {result.ElapsedMilliseconds} ms",
            $"Rows: {result.Rows.Count}",
        };

        if (result.AffectedRows is not null)
        {
            lines.Add($"Affected rows: {result.AffectedRows}");
        }

        if (!string.IsNullOrWhiteSpace(result.ProviderErrorCode))
        {
            lines.Add($"Provider error code: {result.ProviderErrorCode}");
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            lines.Add($"[red]{result.ErrorMessage}[/]");
        }

        lines.Add(string.Empty);
        foreach (var message in result.Messages)
        {
            var color = message.Level switch
            {
                SqlExecutionMessageLevel.Error => "red",
                SqlExecutionMessageLevel.Warning => "yellow",
                _ => "grey70",
            };
            lines.Add($"[{color}]{message.Level}: {message.Message}[/]");
        }

        messageLog.SetContent(lines);
    }

    private void OnGlobalKeyPressed(object? sender, KeyPressedEventArgs args)
    {
        if (args.KeyInfo.Key == ConsoleKey.F5 && args.KeyInfo.Modifiers == 0)
        {
            _ = ExecuteCurrentSqlAsync();
            args.Handled = true;
            return;
        }

        if (
            args.KeyInfo.Key == ConsoleKey.N
            && args.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control)
        )
        {
            CreateQueryTab(isInitial: false);
            args.Handled = true;
            return;
        }

        if (
            args.KeyInfo.Key == ConsoleKey.S
            && args.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control)
        )
        {
            _ = SaveSqlAsync();
            args.Handled = true;
        }
    }

    private MultilineEditControl? ActiveEditor
    {
        get
        {
            if (!editorTabs.HasTabs)
            {
                return null;
            }

            return editorTabs.ActiveTab?.Content as MultilineEditControl;
        }
    }

    private void ShowError(string title, Exception exception)
    {
        logger.LogError(exception, "{Title}", title);
        UpdateStatus($"{title}: {exception.Message}");
        ShowNotification(title, exception.Message, NotificationSeverity.Danger);
    }

    private void ShowNotification(string title, string message, NotificationSeverity severity)
    {
        windowSystem.NotificationStateService.ShowNotification(title, message, severity);
    }

    private void UpdateStatus(string message)
    {
        statusLine?.SetContent([$"[grey70]{message}[/]"]);
    }

    private static bool IsInteractiveTerminal()
    {
        try
        {
            return !Console.IsInputRedirected
                && Console.WindowWidth > 0
                && Console.WindowHeight > 0;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
