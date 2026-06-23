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
using SharpConsoleUI.Parsing;
using SharpConsoleUI.Rendering;

namespace CxSql.UI.Screens;

public sealed class SharpConsoleSqlClient(
    ConnectionManager connectionManager,
    DatabaseProviderRegistry providerRegistry,
    QueryExecutionService queryExecutionService,
    QueryHistoryService queryHistoryService,
    CsvExportService csvExportService,
    ILogger<SharpConsoleSqlClient> logger
)
{
    private const int ResultGridTabIndex = 0;
    private const int MessagesTabIndex = 1;
    private const int TableDetailsTabIndex = 2;
    private const int MaxMessageLines = 240;
    private const int MaxStatusLength = 180;

    private readonly List<QueryEditorTab> queryTabs = [];
    private readonly ObjectExplorerPanel objectExplorer = new();
    private readonly QueryResultDataSource resultDataSource = new();
    private readonly SqlCompletionService completionService = new();
    private readonly Dictionary<string, IReadOnlyList<DatabaseColumn>> knownColumnsByObject = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly List<string> errorMessageLines = [];

    private ConsoleWindowSystem windowSystem = null!;
    private Window mainWindow = null!;
    private StatusBarControl headerBar = null!;
    private ToolbarControl connectionToolbar = null!;
    private ToolbarControl queryToolbar = null!;
    private ToolbarControl resultToolbar = null!;
    private TabControl editorTabs = null!;
    private TabControl bottomTabs = null!;
    private TableControl resultTable = null!;
    private MarkupControl messagesPanel = null!;
    private MarkupControl statusLine = null!;
    private SqlContextMenuController contextMenuController = null!;
    private SqlCompletionController completionController = null!;

    private IReadOnlyList<DatabaseConnection> connections = [];
    private IReadOnlyList<DatabaseObject> currentObjects = [];
    private DatabaseConnection? activeConnection;
    private QueryResult? lastResult;
    private string? lastResultBaseSql;
    private string? pendingResultBaseSql;
    private ResultGridFilterRequest? activeResultFilter;
    private ResultGridSortRequest? activeResultSort;
    private CancellationTokenSource? executionCts;
    private int queryNumber;
    private bool suppressCompletion;
    private MultilineEditControl? lastCompletionEditor;
    private string lastCompletionPrefix = string.Empty;

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
        headerBar = BuildHeaderBar();
        connectionToolbar = BuildConnectionToolbar();
        queryToolbar = BuildQueryToolbar();
        var topRule = Controls.RuleBuilder().StickyTop().WithColor(Color.Grey27).Build();
        var bottomRule = Controls.RuleBuilder().StickyBottom().WithColor(Color.Grey27).Build();

        objectExplorer.ConnectionSelected += connection =>
            UpdateStatus(
                $"Selected {connection.Name}. Double-click to open, or right-click for actions."
            );
        objectExplorer.ConnectionActivated += connection => _ = OpenConnectionAsync(connection);
        objectExplorer.ConnectionsRightClicked += args =>
        {
            ShowConnectionsContext(args);
        };
        objectExplorer.ObjectSelected += databaseObject => _ = LoadColumnsAsync(databaseObject);
        objectExplorer.ObjectActivated += databaseObject => _ = PreviewObjectAsync(databaseObject);
        objectExplorer.ObjectRightClicked += (databaseObject, args) =>
        {
            ShowObjectContext(databaseObject, args);
        };
        objectExplorer.ConnectionRightClicked += (connection, args) =>
        {
            ShowConnectionContext(connection, args);
        };

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
        resultTable.MouseRightClick += (_, args) => ShowResultGridContext(args);
        resultToolbar = BuildResultToolbar();
        var resultGridStack = Controls
            .HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Column(column =>
            {
                column.Add(resultToolbar);
                column.Add(resultTable);
            })
            .Build();

        messagesPanel = Controls
            .Markup()
            .AddLine("[grey70]Execution messages will appear here.[/]")
            .AddLine("[grey50]Latest message is also mirrored to the status bar.[/]")
            .WithMargin(1, 0, 1, 0)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Build();

        bottomTabs = new TabControlBuilder()
            .WithHeaderStyle(TabHeaderStyle.Classic)
            .AddTab("ResultGrid", resultGridStack)
            .AddTab("Messages", messagesPanel)
            .AddTab("TableDetails", BuildEmptyTableDetailsControl())
            .Fill()
            .Build();
        bottomTabs.HorizontalAlignment = HorizontalAlignment.Stretch;
        bottomTabs.VerticalAlignment = VerticalAlignment.Fill;
        bottomTabs.BackgroundColor = Color.Transparent;
        bottomTabs.TabChanged += (_, _) =>
        {
            completionController.Dismiss();
        };

        var editorStack = Controls
            .HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Column(column =>
            {
                editorTabs.Height = 12;
                column.Add(queryToolbar);
                column.Add(editorTabs);
                column.Add(Controls.HorizontalSplitter().WithMinHeights(8, 6).Build());
                column.Add(bottomTabs);
            })
            .Build();

        var objectExplorerStack = Controls
            .HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Column(column =>
            {
                column.Add(connectionToolbar);
                column.Add(objectExplorer.Control);
            })
            .Build();

        var mainGrid = Controls
            .HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Column(column => column.Width(34).Add(objectExplorerStack))
            .Column(column => column.Flex(1).Add(editorStack))
            .WithSplitterAfter(0)
            .Build();

        foreach (var splitter in mainGrid.Splitters)
        {
            splitter.ForegroundColor = Color.Grey27;
        }

        statusLine = Controls
            .Markup("[grey70]Ready. Left panel manages connections. Exit: Ctrl+Q.[/]")
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
            .AddControl(headerBar)
            .AddControl(topRule)
            .AddControl(mainGrid)
            .AddControl(bottomRule)
            .AddControl(statusLine)
            .OnKeyPressed(OnGlobalKeyPressed)
            .Build();

        contextMenuController = new SqlContextMenuController(mainWindow);
        completionController = new SqlCompletionController(mainWindow, AcceptCompletion);
        mainWindow.PreviewKeyPressed += OnPreviewKeyPressed;
    }

    private StatusBarControl BuildHeaderBar()
    {
        return Controls
            .StatusBar()
            .StickyTop()
            .WithMargin(1, 0, 1, 0)
            .AddLeftText("[bold cyan]cxsql[/]", () => UpdateStatus("cxsql"))
            .AddCenterText(
                "[grey50]Mouse-first terminal SQL client[/]",
                () => UpdateStatus("Use the left panel for connection actions.")
            )
            .AddRight("Exit", "Ctrl+Q", ExitApplication)
            .Build();
    }

    private ToolbarControl BuildConnectionToolbar()
    {
        var builder = Controls
            .Toolbar()
            .WithSpacing(1)
            .WithWrap()
            .WithMargin(1, 0, 1, 0)
            .WithBackgroundColor(Color.Transparent)
            .WithBelowLineColor(Color.Grey27);

        AddToolbarButton(builder, "New", () => _ = NewConnectionAsync());
        AddToolbarButton(builder, "Open", () => _ = SelectConnectionAsync());
        AddToolbarButton(builder, "Refresh", () => _ = RefreshObjectsAsync());

        return builder.Build();
    }

    private ToolbarControl BuildQueryToolbar()
    {
        var builder = Controls
            .Toolbar()
            .WithSpacing(1)
            .WithWrap()
            .WithMargin(1, 0, 1, 0)
            .WithBackgroundColor(Color.Transparent)
            .WithBelowLineColor(Color.Grey27);

        AddToolbarButton(builder, "Execute [grey50]F5[/]", () => _ = ExecuteCurrentSqlAsync());
        AddToolbarButton(builder, "Stop", StopExecution);
        AddToolbarButton(builder, "Save SQL [grey50]Ctrl+S[/]", () => _ = SaveSqlAsync());
        AddToolbarButton(builder, "History", () => _ = ShowHistoryAsync());

        return builder.Build();
    }

    private ToolbarControl BuildResultToolbar()
    {
        var builder = Controls
            .Toolbar()
            .WithSpacing(1)
            .WithWrap()
            .WithMargin(1, 0, 1, 0)
            .WithBackgroundColor(Color.Transparent)
            .WithBelowLineColor(Color.Grey27);

        AddToolbarButton(builder, "Export CSV", () => _ = ExportCsvAsync());
        AddToolbarButton(builder, "Copy All", CopyAllResults);
        AddToolbarButton(builder, "Clear Filter", ClearResultFilter);

        return builder.Build();
    }

    private static IWindowControl BuildEmptyTableDetailsControl()
    {
        return Controls
            .Markup("[grey70]Right-click a table or view and choose Open Details.[/]")
            .WithMargin(1)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Build();
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
            .IsEditing(true)
            .WithEditingHints(false)
            .WithEscapeExitsEditMode(false)
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
        editor.ContentChanged += (_, content) => OnEditorContentChanged(editor, content);
        editor.MouseRightClick += (_, args) => ShowEditorContext(editor, args);

        var tab = new QueryEditorTab(title, editor);
        queryTabs.Add(tab);
        editorTabs.AddTab(title, editor, isClosable: !isInitial);
        editorTabs.ActiveTabIndex = editorTabs.TabCount - 1;
        editor.RequestFocus();
        UpdateStatus($"{title} opened. Query actions are above the editor.");
    }

    private async Task LoadConnectionsAsync()
    {
        connections = await connectionManager.ListAsync(CancellationToken.None);
        objectExplorer.SetConnections(connections, activeConnection, currentObjects);
        UpdateStatus(
            connections.Count == 0
                ? "No connections yet. Right-click the left panel and choose New Connection."
                : "Connections loaded. Double-click a connection to open it."
        );
    }

    private async Task NewConnectionAsync()
    {
        var request = await NewConnectionModal.ShowAsync(
            windowSystem,
            mainWindow,
            async testRequest =>
                await connectionManager.TestAsync(
                    new DatabaseConnection
                    {
                        Name = testRequest.Name,
                        DatabaseType = testRequest.DatabaseType,
                        ConnectionString = testRequest.ConnectionString,
                    },
                    CancellationToken.None
                )
        );
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

            connections = await connectionManager.ListAsync(CancellationToken.None);
            activeConnection = connection;
            await RefreshObjectsAsync();
            UpdateStatus($"Created and opened {connection.Name}.");
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
                "No saved connections. Use New Connection first.",
                NotificationSeverity.Warning
            );
            UpdateStatus("No saved connections.");
            return;
        }

        contextMenuController.Show(
            connections.Select(connection => new SqlContextMenuItem(
                connection.Name,
                connection.DatabaseType.ToString(),
                () => OpenConnectionAsync(connection)
            )),
            objectExplorer.Control.ActualX + 2,
            objectExplorer.Control.ActualY + 1,
            objectExplorer.Control
        );
    }

    private async Task OpenConnectionAsync(DatabaseConnection connection)
    {
        UpdateStatus($"Opening {connection.Name}...");
        var test = await connectionManager.TestAsync(connection, CancellationToken.None);
        if (!test.Succeeded)
        {
            AppendMessage(
                SqlExecutionMessageLevel.Error,
                $"Connection failed: {connection.Name}",
                test.ErrorMessage ?? "Unknown error."
            );
            ShowNotification(
                "Connection failed",
                TruncateStatus(test.ErrorMessage ?? "Unknown error."),
                NotificationSeverity.Danger
            );
            return;
        }

        activeConnection = connection;
        await RefreshObjectsAsync();
        UpdateStatus($"Opened {connection.Name}.");
    }

    private async Task RefreshObjectsAsync()
    {
        knownColumnsByObject.Clear();
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

    private async Task<IReadOnlyList<DatabaseColumn>> LoadColumnsAsync(
        DatabaseObject databaseObject
    )
    {
        if (activeConnection is null)
        {
            return [];
        }

        var key = GetObjectKey(databaseObject);
        if (knownColumnsByObject.TryGetValue(key, out var cached))
        {
            return cached;
        }

        try
        {
            var provider = providerRegistry.GetProvider(activeConnection.DatabaseType);
            await using var connection = provider.CreateConnection(
                activeConnection.ConnectionString
            );
            var columns = await provider.GetColumnsAsync(
                connection,
                databaseObject,
                CancellationToken.None
            );
            knownColumnsByObject[key] = columns;
            return columns;
        }
        catch (Exception ex)
        {
            AppendMessage(
                SqlExecutionMessageLevel.Warning,
                $"Load columns failed: {databaseObject.DisplayName}",
                ex.Message
            );
            return [];
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
            await LoadColumnsAsync(databaseObject);
            var previewSql = providerRegistry
                .GetPreviewSqlBuilder(activeConnection.DatabaseType)
                .BuildPreviewSql(databaseObject, 100);
            pendingResultBaseSql = BuildSelectSql(databaseObject);
            try
            {
                ActiveEditor?.SetContent(previewSql);
                await ExecuteCurrentSqlAsync();
            }
            finally
            {
                pendingResultBaseSql = null;
            }
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
            UpdateStatus("Open a connection before executing SQL.");
            return;
        }

        var editor = ActiveEditor;
        if (editor is null)
        {
            ShowNotification(
                "Execute",
                "Select a SQL editor tab before executing SQL.",
                NotificationSeverity.Warning
            );
            UpdateStatus("Select a SQL editor tab before executing SQL.");
            return;
        }

        if (string.IsNullOrWhiteSpace(editor.Content))
        {
            ShowNotification(
                "Execute",
                "The current SQL editor is empty.",
                NotificationSeverity.Warning
            );
            return;
        }

        var resultBaseSql = pendingResultBaseSql ?? editor.Content;
        executionCts = new CancellationTokenSource();
        UpdateStatus("Executing SQL...");

        try
        {
            lastResult = await queryExecutionService.ExecuteAsync(
                activeConnection,
                editor.Content,
                executionCts.Token
            );
            var displayResult =
                lastResult.Success && lastResult.Columns.Count > 0 ? lastResult : null;
            resultDataSource.SetResult(displayResult);
            if (displayResult is not null)
            {
                lastResultBaseSql = resultBaseSql;
                activeResultFilter = null;
                activeResultSort = null;
                bottomTabs.ActiveTabIndex = ResultGridTabIndex;
            }
            else if (lastResult.Success)
            {
                lastResultBaseSql = null;
                activeResultFilter = null;
                activeResultSort = null;
                bottomTabs.ActiveTabIndex = MessagesTabIndex;
            }
            else
            {
                lastResultBaseSql = null;
                activeResultFilter = null;
                activeResultSort = null;
                bottomTabs.ActiveTabIndex = MessagesTabIndex;
            }

            RecordQueryResultMessages(lastResult);
        }
        catch (OperationCanceledException)
        {
            AppendMessage(SqlExecutionMessageLevel.Warning, "Execute", "Execution canceled.");
            ShowNotification("Execute", "Execution canceled.", NotificationSeverity.Warning);
        }
        finally
        {
            pendingResultBaseSql = null;
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
            ShowNotification(
                "Save SQL",
                "Select a SQL editor tab before saving SQL.",
                NotificationSeverity.Warning
            );
            UpdateStatus("Select a SQL editor tab before saving SQL.");
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
            UpdateStatus($"Saved SQL to {path}.");
        }
        catch (Exception ex)
        {
            ShowError("Save SQL failed", ex);
        }
    }

    private async Task ExportCsvAsync()
    {
        var exportResult = resultDataSource.ToVisibleResult();
        if (exportResult is null || exportResult.Columns.Count == 0)
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
            await csvExportService.ExportAsync(exportResult, path, CancellationToken.None);
            ShowNotification("Export", $"Exported to {path}.", NotificationSeverity.Success);
            UpdateStatus($"Exported visible result grid to {path}.");
        }
        catch (Exception ex)
        {
            ShowError("Export failed", ex);
        }
    }

    private void ShowConnectionsContext()
    {
        ShowConnectionsContext(null);
    }

    private void ShowConnectionsContext(SharpConsoleUI.Events.MouseEventArgs? args)
    {
        var owner = objectExplorer.Control;
        var anchorX = args is null ? owner.ActualX + 2 : owner.ActualX + args.Position.X;
        var anchorY = args is null ? owner.ActualY + 1 : owner.ActualY + args.Position.Y;
        contextMenuController.Show(
            [
                new SqlContextMenuItem("New Connection", null, NewConnectionAsync),
                new SqlContextMenuItem("Open Connection", null, SelectConnectionAsync),
                SqlContextMenuItem.Create("New Query", () => CreateQueryTab(isInitial: false)),
                new SqlContextMenuItem("History", null, ShowHistoryAsync),
                SqlContextMenuItem.Separator(),
                new SqlContextMenuItem("Refresh Active Connection", null, RefreshObjectsAsync),
                SqlContextMenuItem.Separator(),
                SqlContextMenuItem.Create("Exit", ExitApplication, "Ctrl+Q"),
            ],
            anchorX,
            anchorY,
            owner
        );
    }

    private void ShowConnectionContext(
        DatabaseConnection connection,
        SharpConsoleUI.Events.MouseEventArgs args
    )
    {
        var owner = objectExplorer.Control;
        contextMenuController.Show(
            [
                new SqlContextMenuItem(
                    "Open Connection",
                    null,
                    () => OpenConnectionAsync(connection)
                ),
                new SqlContextMenuItem(
                    "Test Connection",
                    null,
                    () => TestConnectionAsync(connection)
                ),
                SqlContextMenuItem.Create("New Query", () => CreateQueryTab(isInitial: false)),
                new SqlContextMenuItem("History", null, ShowHistoryAsync),
                SqlContextMenuItem.Separator(),
                new SqlContextMenuItem(
                    "Edit Connection",
                    null,
                    () => EditConnectionAsync(connection)
                ),
                new SqlContextMenuItem(
                    "Delete Connection",
                    null,
                    () => DeleteConnectionAsync(connection)
                ),
                SqlContextMenuItem.Create("Close Connection", () => CloseConnection(connection)),
                SqlContextMenuItem.Separator(),
                new SqlContextMenuItem("Refresh Objects", null, RefreshObjectsAsync),
                SqlContextMenuItem.Create(
                    "Copy Safe Connection String",
                    () => CopySafeConnectionString(connection)
                ),
            ],
            owner.ActualX + args.Position.X,
            owner.ActualY + args.Position.Y,
            owner
        );
    }

    private void ShowObjectContext(
        DatabaseObject databaseObject,
        SharpConsoleUI.Events.MouseEventArgs args
    )
    {
        _ = LoadColumnsAsync(databaseObject);
        var owner = objectExplorer.Control;
        contextMenuController.Show(
            [
                new SqlContextMenuItem(
                    "Open Details",
                    null,
                    () => OpenObjectDetailsAsync(databaseObject)
                ),
                new SqlContextMenuItem(
                    "Preview Data",
                    null,
                    () => PreviewObjectAsync(databaseObject)
                ),
                new SqlContextMenuItem(
                    "Export Table CSV",
                    null,
                    () => ExportObjectCsvAsync(databaseObject)
                ),
                SqlContextMenuItem.Separator(),
                SqlContextMenuItem.Create(
                    "New Query Here",
                    () =>
                        SetEditorContent($"SELECT *\nFROM {GetObjectReference(databaseObject)};\n")
                ),
                SqlContextMenuItem.Create(
                    "Generate SELECT",
                    () => SetEditorContent(BuildSelectSql(databaseObject))
                ),
                SqlContextMenuItem.Create(
                    "Generate INSERT",
                    () => SetEditorContent(BuildInsertSql(databaseObject))
                ),
                SqlContextMenuItem.Create(
                    "Generate UPDATE",
                    () => SetEditorContent(BuildUpdateSql(databaseObject))
                ),
                SqlContextMenuItem.Create(
                    "Generate DELETE",
                    () => SetEditorContent(BuildDeleteSql(databaseObject))
                ),
                SqlContextMenuItem.Separator(),
                new SqlContextMenuItem(
                    "Show Columns",
                    null,
                    () => ShowColumnsAsync(databaseObject)
                ),
                new SqlContextMenuItem("Copy DDL", null, () => CopyObjectDdlAsync(databaseObject)),
                SqlContextMenuItem.Create(
                    "Copy Name",
                    () => CopyToClipboard(databaseObject.DisplayName)
                ),
                SqlContextMenuItem.Separator(),
                new SqlContextMenuItem("Refresh Objects", null, RefreshObjectsAsync),
            ],
            owner.ActualX + args.Position.X,
            owner.ActualY + args.Position.Y,
            owner
        );
    }

    private void ShowResultGridContext(SharpConsoleUI.Events.MouseEventArgs args)
    {
        if (TryGetResultHeaderColumn(args, out var headerColumn))
        {
            ShowResultHeaderContext(headerColumn, args);
            return;
        }

        contextMenuController.Show(
            [
                new SqlContextMenuItem("Export CSV", null, ExportCsvAsync),
                new SqlContextMenuItem("Execute Current SQL", "F5", ExecuteCurrentSqlAsync),
                SqlContextMenuItem.Separator(),
                SqlContextMenuItem.Create("Copy Cell", CopySelectedCell),
                SqlContextMenuItem.Create("Copy Row", CopySelectedRow),
                SqlContextMenuItem.Create("Copy All", CopyAllResults),
                SqlContextMenuItem.Separator(),
                SqlContextMenuItem.Create("Clear Filter", ClearResultFilter),
            ],
            resultTable.ActualX + args.Position.X,
            resultTable.ActualY + args.Position.Y,
            resultTable
        );
    }

    private void ShowResultHeaderContext(int columnIndex, SharpConsoleUI.Events.MouseEventArgs args)
    {
        var columnName = resultDataSource.GetColumnHeader(columnIndex);
        contextMenuController.Show(
            [
                SqlContextMenuItem.Create(
                    $"Sort Asc: {columnName}",
                    () => SortResultGridColumn(columnIndex, SortDirection.Ascending)
                ),
                SqlContextMenuItem.Create(
                    $"Sort Desc: {columnName}",
                    () => SortResultGridColumn(columnIndex, SortDirection.Descending)
                ),
                SqlContextMenuItem.Create("Clear Sort", ClearResultSort),
                SqlContextMenuItem.Separator(),
                new SqlContextMenuItem(
                    $"Filter: {columnName}",
                    null,
                    () => FilterResultGridColumnAsync(columnIndex)
                ),
                SqlContextMenuItem.Create("Clear Filter", ClearResultFilter),
                SqlContextMenuItem.Separator(),
                new SqlContextMenuItem("Export CSV", null, ExportCsvAsync),
                SqlContextMenuItem.Create("Copy All", CopyAllResults),
            ],
            resultTable.ActualX + args.Position.X,
            resultTable.ActualY + args.Position.Y,
            resultTable
        );
    }

    private void ShowEditorContext(
        MultilineEditControl editor,
        SharpConsoleUI.Events.MouseEventArgs args
    )
    {
        contextMenuController.Show(
            [
                SqlContextMenuItem.Create(
                    "Cut",
                    () =>
                        editor.ProcessKey(new ConsoleKeyInfo('x', ConsoleKey.X, false, false, true))
                ),
                SqlContextMenuItem.Create(
                    "Copy",
                    () =>
                        editor.ProcessKey(new ConsoleKeyInfo('c', ConsoleKey.C, false, false, true))
                ),
                SqlContextMenuItem.Create(
                    "Paste",
                    () =>
                        editor.ProcessKey(new ConsoleKeyInfo('v', ConsoleKey.V, false, false, true))
                ),
                SqlContextMenuItem.Separator(),
                SqlContextMenuItem.Create(
                    "Select All",
                    () =>
                        editor.ProcessKey(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, true))
                ),
                new SqlContextMenuItem("History", null, ShowHistoryAsync),
                new SqlContextMenuItem("Save SQL", "Ctrl+S", SaveSqlAsync),
            ],
            editor.ActualX + args.Position.X,
            editor.ActualY + args.Position.Y,
            editor
        );
    }

    private async Task OpenObjectDetailsAsync(DatabaseObject databaseObject)
    {
        if (activeConnection is null)
        {
            return;
        }

        try
        {
            var provider = providerRegistry.GetProvider(activeConnection.DatabaseType);
            await using var connection = provider.CreateConnection(
                activeConnection.ConnectionString
            );
            var details = await provider.GetObjectDetailsAsync(
                connection,
                databaseObject,
                CancellationToken.None
            );
            knownColumnsByObject[GetObjectKey(databaseObject)] = details.Columns;

            bottomTabs.SetTabContent(TableDetailsTabIndex, BuildObjectDetailsControl(details));
            bottomTabs.ActiveTabIndex = TableDetailsTabIndex;
            UpdateStatus($"Loaded TableDetails for {databaseObject.DisplayName}.");
        }
        catch (Exception ex)
        {
            ShowError("Open details failed", ex);
        }
    }

    private async Task ExportObjectCsvAsync(DatabaseObject databaseObject)
    {
        if (activeConnection is null)
        {
            return;
        }

        if (
            databaseObject.ObjectType
            is not DatabaseObjectType.Table
                and not DatabaseObjectType.View
        )
        {
            ShowNotification(
                "Export Table CSV",
                "Only tables and views can be exported.",
                NotificationSeverity.Warning
            );
            return;
        }

        var path = await TextInputModal.ShowAsync(
            windowSystem,
            mainWindow,
            "Export Table CSV",
            "CSV path: ",
            $"{databaseObject.Name}.csv"
        );
        if (path is null)
        {
            return;
        }

        try
        {
            var provider = providerRegistry.GetProvider(activeConnection.DatabaseType);
            await using var connection = provider.CreateConnection(
                activeConnection.ConnectionString
            );
            var sql = providerRegistry
                .GetPreviewSqlBuilder(activeConnection.DatabaseType)
                .BuildPreviewSql(databaseObject, 1000);
            var result = await provider.ExecuteSqlAsync(connection, sql, CancellationToken.None);
            await csvExportService.ExportAsync(result, path, CancellationToken.None);
            UpdateStatus($"Exported {databaseObject.DisplayName} to {path}.");
        }
        catch (Exception ex)
        {
            ShowError("Export table failed", ex);
        }
    }

    private async Task CopyObjectDdlAsync(DatabaseObject databaseObject)
    {
        if (activeConnection is null)
        {
            return;
        }

        try
        {
            var provider = providerRegistry.GetProvider(activeConnection.DatabaseType);
            await using var connection = provider.CreateConnection(
                activeConnection.ConnectionString
            );
            var details = await provider.GetObjectDetailsAsync(
                connection,
                databaseObject,
                CancellationToken.None
            );
            CopyToClipboard(details.Ddl);
        }
        catch (Exception ex)
        {
            ShowError("Copy DDL failed", ex);
        }
    }

    private async Task ShowHistoryAsync()
    {
        var entries = await queryHistoryService.ListAsync(CancellationToken.None);
        if (entries.Count == 0)
        {
            ShowNotification("History", "No query history yet.", NotificationSeverity.Warning);
            return;
        }

        var selected = await QueryHistoryModal.ShowAsync(windowSystem, mainWindow, entries);
        if (selected is null)
        {
            return;
        }

        SetEditorContent(selected.SqlText);
        UpdateStatus("Loaded SQL from history.");
    }

    private async Task TestConnectionAsync(DatabaseConnection connection)
    {
        var result = await connectionManager.TestAsync(connection, CancellationToken.None);
        if (result.Succeeded)
        {
            AppendMessage(
                SqlExecutionMessageLevel.Info,
                $"Connection test: {connection.Name}",
                "Connection test succeeded."
            );
            return;
        }

        AppendMessage(
            SqlExecutionMessageLevel.Error,
            $"Connection test: {connection.Name}",
            result.ErrorMessage ?? "Unknown error."
        );
        ShowNotification(
            "Test Connection",
            TruncateStatus(result.ErrorMessage ?? "Unknown error."),
            NotificationSeverity.Danger
        );
    }

    private async Task EditConnectionAsync(DatabaseConnection connection)
    {
        if (activeConnection?.Id == connection.Id)
        {
            UpdateStatus("Close this connection before editing its connection string or path.");
            AppendMessage(
                SqlExecutionMessageLevel.Warning,
                "Edit connection",
                "Close the connection before editing its connection string or SQLite file path."
            );
            return;
        }

        var request = await EditConnectionModal.ShowAsync(
            windowSystem,
            mainWindow,
            connection,
            async testRequest =>
                await connectionManager.TestAsync(
                    new DatabaseConnection
                    {
                        Name = testRequest.Name,
                        DatabaseType = testRequest.DatabaseType,
                        ConnectionString = testRequest.ConnectionString,
                    },
                    CancellationToken.None
                )
        );
        if (request is null)
        {
            return;
        }

        try
        {
            var updatedConnection = new DatabaseConnection
            {
                Id = connection.Id,
                Name = request.Name,
                DatabaseType = connection.DatabaseType,
                ConnectionString = request.ConnectionString,
                CreatedAtUnixMs = connection.CreatedAtUnixMs,
                UpdatedAtUnixMs = connection.UpdatedAtUnixMs,
            };
            await connectionManager.UpdateAsync(updatedConnection, CancellationToken.None);
            connections = await connectionManager.ListAsync(CancellationToken.None);

            objectExplorer.SetConnections(connections, activeConnection, currentObjects);
            UpdateStatus($"Updated connection {updatedConnection.Name}.");
        }
        catch (Exception ex)
        {
            ShowError("Edit connection failed", ex);
        }
    }

    private async Task DeleteConnectionAsync(DatabaseConnection connection)
    {
        var confirmation = await TextInputModal.ShowAsync(
            windowSystem,
            mainWindow,
            "Delete Connection",
            "Type DELETE: ",
            string.Empty,
            $"This removes {connection.Name}. Type DELETE to confirm."
        );
        if (!string.Equals(confirmation, "DELETE", StringComparison.Ordinal))
        {
            UpdateStatus("Delete connection canceled.");
            return;
        }

        try
        {
            await connectionManager.DeleteAsync(connection.Id, CancellationToken.None);
            if (activeConnection?.Id == connection.Id)
            {
                activeConnection = null;
                currentObjects = [];
                knownColumnsByObject.Clear();
                resultDataSource.SetResult(null);
            }

            connections = await connectionManager.ListAsync(CancellationToken.None);
            objectExplorer.SetConnections(connections, activeConnection, currentObjects);
            UpdateStatus($"Deleted connection {connection.Name}.");
        }
        catch (Exception ex)
        {
            ShowError("Delete connection failed", ex);
        }
    }

    private void CloseConnection(DatabaseConnection connection)
    {
        if (activeConnection?.Id != connection.Id)
        {
            UpdateStatus($"{connection.Name} is not the active connection.");
            return;
        }

        activeConnection = null;
        currentObjects = [];
        knownColumnsByObject.Clear();
        objectExplorer.SetConnections(connections, activeConnection, currentObjects);
        UpdateStatus($"Closed connection {connection.Name}.");
    }

    private async Task ShowColumnsAsync(DatabaseObject databaseObject)
    {
        var columns = await LoadColumnsAsync(databaseObject);
        var details =
            columns.Count == 0
                ? "No columns loaded."
                : string.Join(
                    ", ",
                    columns.Select(column =>
                        $"{column.Name}{(string.IsNullOrWhiteSpace(column.DataType) ? string.Empty : $" {column.DataType}")}"
                    )
                );
        AppendMessage(
            SqlExecutionMessageLevel.Info,
            $"Columns: {databaseObject.DisplayName}",
            details
        );
        bottomTabs.ActiveTabIndex = MessagesTabIndex;
    }

    private void RecordQueryResultMessages(QueryResult result)
    {
        foreach (var message in result.Messages)
        {
            AppendMessage(message.Level, "SQL message", message.Message);
        }

        if (result.Success)
        {
            AppendMessage(
                SqlExecutionMessageLevel.Info,
                "SQL executed",
                $"Elapsed {result.ElapsedMilliseconds} ms. Rows: {result.Rows.Count}. Affected rows: {result.AffectedRows?.ToString() ?? "unknown"}."
            );
            return;
        }

        var details = result.ErrorMessage ?? "Unknown SQL error.";
        if (!string.IsNullOrWhiteSpace(result.ProviderErrorCode))
        {
            details = $"{details} Provider error code: {result.ProviderErrorCode}.";
        }

        AppendMessage(SqlExecutionMessageLevel.Error, "SQL failed", details);
        ShowNotification("SQL failed", TruncateStatus(details), NotificationSeverity.Danger);
    }

    private void SetEditorContent(string sql, string statusMessage = "SQL template inserted.")
    {
        var editor = ActiveEditor;
        if (editor is null)
        {
            CreateQueryTab(isInitial: false);
            editor = ActiveEditor;
        }

        editor?.SetContent(sql);
        editor?.RequestFocus();
        UpdateStatus(statusMessage);
    }

    private IWindowControl BuildObjectDetailsControl(DatabaseObjectDetails details)
    {
        var tabs = new TabControlBuilder()
            .WithHeaderStyle(TabHeaderStyle.Classic)
            .AddTab(
                "Structure",
                BuildStringTable(
                    ["Column", "Type", "Nullable", "Ordinal"],
                    details
                        .Columns.Select(column =>
                            (IReadOnlyList<string>)
                                [
                                    column.Name,
                                    column.DataType ?? string.Empty,
                                    column.IsNullable ? "YES" : "NO",
                                    column.Ordinal.ToString(),
                                ]
                        )
                        .ToList()
                )
            )
            .AddTab(
                "Constraints",
                BuildStringTable(
                    ["Name", "Type", "Definition"],
                    details
                        .Constraints.Select(constraint =>
                            (IReadOnlyList<string>)
                                [
                                    constraint.Name,
                                    constraint.Type,
                                    constraint.Definition ?? string.Empty,
                                ]
                        )
                        .ToList()
                )
            )
            .AddTab(
                "Indexes",
                BuildStringTable(
                    ["Name", "Unique", "Definition"],
                    details
                        .Indexes.Select(index =>
                            (IReadOnlyList<string>)
                                [
                                    index.Name,
                                    index.IsUnique ? "YES" : "NO",
                                    index.Definition ?? string.Empty,
                                ]
                        )
                        .ToList()
                )
            )
            .AddTab(
                "Triggers",
                BuildStringTable(
                    ["Name", "Event", "Definition"],
                    details
                        .Triggers.Select(trigger =>
                            (IReadOnlyList<string>)
                                [
                                    trigger.Name,
                                    trigger.Event ?? string.Empty,
                                    trigger.Definition ?? string.Empty,
                                ]
                        )
                        .ToList()
                )
            )
            .AddTab("DDL", BuildReadOnlySql(details.Ddl))
            .Fill()
            .Build();
        tabs.HorizontalAlignment = HorizontalAlignment.Stretch;
        tabs.VerticalAlignment = VerticalAlignment.Fill;
        tabs.BackgroundColor = Color.Transparent;

        return Controls
            .HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .Column(column =>
            {
                column.Add(
                    Controls
                        .Markup(
                            $"[bold cyan]{MarkupParser.Escape(details.DatabaseObject.DisplayName)}[/] [grey50]{details.DatabaseObject.ObjectType}[/]"
                        )
                        .WithMargin(1, 0, 1, 0)
                        .Build()
                );
                column.Add(tabs);
            })
            .Build();
    }

    private static TableControl BuildStringTable(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<string>> rows
    )
    {
        return Controls
            .Table()
            .WithDataSource(new StringTableDataSource(headers, rows))
            .Interactive()
            .WithSorting()
            .WithColumnResize()
            .WithColumnSeparator('|', Color.Grey27)
            .WithBorderStyle(BorderStyle.None)
            .WithHeaderColors(Color.Grey70, new Color(25, 35, 55))
            .WithHorizontalScrollbar(ScrollbarVisibility.Auto)
            .WithVerticalScrollbar(ScrollbarVisibility.Auto)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithHorizontalAlignment(HorizontalAlignment.Stretch)
            .Build();
    }

    private static IWindowControl BuildDataTab(QueryResult? previewResult)
    {
        if (previewResult is null)
        {
            return Controls
                .Markup("[grey70]Data preview is available for tables and views.[/]")
                .WithMargin(1)
                .Build();
        }

        if (!previewResult.Success)
        {
            return Controls
                .Markup(
                    $"[red]{MarkupParser.Escape(previewResult.ErrorMessage ?? "Data preview failed.")}[/]"
                )
                .WithMargin(1)
                .Build();
        }

        var dataSource = new QueryResultDataSource();
        dataSource.SetResult(previewResult);
        return Controls
            .Table()
            .WithDataSource(dataSource)
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
    }

    private static MultilineEditControl BuildReadOnlySql(string sql)
    {
        var builder = Controls
            .MultilineEdit(sql)
            .AsReadOnly(true)
            .WithLineNumbers(true)
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
            builder.WithSyntaxHighlighter(syntaxHighlighter);
        }

        return builder.Build();
    }

    private string BuildSelectSql(DatabaseObject databaseObject)
    {
        var columns = GetKnownColumns(databaseObject);
        var columnList =
            columns.Count == 0
                ? "*"
                : string.Join(", ", columns.Select(column => QuoteIdentifier(column.Name)));
        return $"SELECT {columnList}\nFROM {GetObjectReference(databaseObject)};\n";
    }

    private string BuildInsertSql(DatabaseObject databaseObject)
    {
        var columns = GetKnownColumns(databaseObject);
        var columnList =
            columns.Count == 0
                ? "column_name"
                : string.Join(", ", columns.Select(column => QuoteIdentifier(column.Name)));
        var valueList =
            columns.Count == 0
                ? "value"
                : string.Join(", ", columns.Select(column => $"@{column.Name}"));
        return $"INSERT INTO {GetObjectReference(databaseObject)} ({columnList})\nVALUES ({valueList});\n";
    }

    private string BuildUpdateSql(DatabaseObject databaseObject)
    {
        var columns = GetKnownColumns(databaseObject);
        var assignments =
            columns.Count == 0
                ? "column_name = value"
                : string.Join(
                    ",\n    ",
                    columns.Select(column => $"{QuoteIdentifier(column.Name)} = @{column.Name}")
                );
        return $"UPDATE {GetObjectReference(databaseObject)}\nSET {assignments}\nWHERE condition;\n";
    }

    private string BuildDeleteSql(DatabaseObject databaseObject)
    {
        return $"DELETE FROM {GetObjectReference(databaseObject)}\nWHERE condition;\n";
    }

    private IReadOnlyList<DatabaseColumn> GetKnownColumns(DatabaseObject databaseObject)
    {
        return knownColumnsByObject.TryGetValue(GetObjectKey(databaseObject), out var columns)
            ? columns
            : [];
    }

    private string GetObjectReference(DatabaseObject databaseObject)
    {
        return string.IsNullOrWhiteSpace(databaseObject.Schema)
            ? QuoteIdentifier(databaseObject.Name)
            : $"{QuoteIdentifier(databaseObject.Schema)}.{QuoteIdentifier(databaseObject.Name)}";
    }

    private string QuoteIdentifier(string identifier)
    {
        if (activeConnection?.DatabaseType == DatabaseType.SqlServer)
        {
            return "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
        }

        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string GetObjectKey(DatabaseObject databaseObject)
    {
        return $"{databaseObject.Schema ?? string.Empty}.{databaseObject.Name}";
    }

    private static string GetObjectDetailsTabTitle(
        DatabaseConnection connection,
        DatabaseObject databaseObject
    )
    {
        return $"{databaseObject.ObjectType}: {connection.Name}.{databaseObject.DisplayName}";
    }

    private int FindEditorTabIndex(string title)
    {
        var index = 0;
        foreach (var tabTitle in editorTabs.TabTitles)
        {
            if (string.Equals(tabTitle, title, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    private void CopySelectedCell()
    {
        if (resultDataSource.QueryResult is null || resultTable.SelectedRowIndex < 0)
        {
            ShowNotification(
                "Copy Cell",
                "Select a result cell before copying.",
                NotificationSeverity.Warning
            );
            return;
        }

        var value = resultDataSource.GetPlainCellValue(
            resultTable.SelectedRowIndex,
            resultTable.SelectedColumnIndex
        );
        CopyToClipboard(value);
    }

    private void CopySelectedRow()
    {
        if (resultDataSource.QueryResult is null || resultTable.SelectedRowIndex < 0)
        {
            ShowNotification(
                "Copy Row",
                "Select a result row before copying.",
                NotificationSeverity.Warning
            );
            return;
        }

        var values = resultDataSource.GetPlainRowValues(resultTable.SelectedRowIndex);
        CopyToClipboard(string.Join('\t', values));
    }

    private void CopyAllResults()
    {
        if (resultDataSource.QueryResult is null || resultDataSource.ColumnCount == 0)
        {
            ShowNotification(
                "Copy All",
                "Run a query that returns rows first.",
                NotificationSeverity.Warning
            );
            return;
        }

        CopyToClipboard(resultDataSource.ToTabDelimitedText());
    }

    private void CopySafeConnectionString(DatabaseConnection connection)
    {
        var value = ConnectionInputMapper.ToSafeConnectionString(
            connection.DatabaseType,
            connection.ConnectionString
        );
        CopyToClipboard(value, "Copied connection string with password redacted.");
    }

    private void CopyToClipboard(string value, string statusMessage = "Copied to clipboard.")
    {
        ClipboardHelper.SetText(value);
        UpdateStatus(statusMessage);
    }

    private void ClearResultFilter()
    {
        resultDataSource.ClearFilter();
        activeResultFilter = null;
        UpdateEditorFromResultGridState("ResultGrid filter cleared.");
    }

    private void SortResultGridColumn(int columnIndex, SortDirection direction)
    {
        if (resultDataSource.QueryResult is null || !resultDataSource.CanSort(columnIndex))
        {
            ShowNotification(
                "ResultGrid",
                "Run a query that returns columns before sorting.",
                NotificationSeverity.Warning
            );
            return;
        }

        resultDataSource.Sort(columnIndex, direction);
        resultTable.SelectedColumnIndex = columnIndex;
        activeResultSort = new ResultGridSortRequest(
            resultDataSource.GetColumnHeader(columnIndex),
            direction
        );
        UpdateEditorFromResultGridState($"ResultGrid sorted by {activeResultSort.ColumnName}.");
    }

    private void ClearResultSort()
    {
        resultTable.ClearSort();
        resultDataSource.ClearSort();
        activeResultSort = null;
        UpdateEditorFromResultGridState("ResultGrid sort cleared.");
    }

    private async Task FilterResultGridColumnAsync(int columnIndex)
    {
        if (
            resultDataSource.QueryResult is null
            || columnIndex < 0
            || columnIndex >= resultDataSource.ColumnCount
        )
        {
            ShowNotification(
                "ResultGrid",
                "Run a query that returns columns before filtering.",
                NotificationSeverity.Warning
            );
            return;
        }

        var columnName = resultDataSource.GetColumnHeader(columnIndex);
        var request = await ResultGridFilterDialog.ShowAsync(windowSystem, mainWindow, columnName);
        if (request is null)
        {
            return;
        }

        resultDataSource.ApplyColumnFilter(columnIndex, request.Operator, request.Value);
        resultTable.SelectedColumnIndex = columnIndex;
        activeResultFilter = request;
        bottomTabs.ActiveTabIndex = ResultGridTabIndex;
        UpdateEditorFromResultGridState($"ResultGrid filter applied to {columnName}.");
    }

    private void UpdateEditorFromResultGridState(string statusMessage)
    {
        if (activeConnection is null || string.IsNullOrWhiteSpace(lastResultBaseSql))
        {
            UpdateStatus(statusMessage);
            return;
        }

        try
        {
            SetEditorContent(
                ResultGridSqlBuilder.Build(
                    activeConnection.DatabaseType,
                    lastResultBaseSql,
                    activeResultFilter,
                    activeResultSort
                ),
                statusMessage
            );
        }
        catch (Exception ex)
        {
            ShowError("Build ResultGrid SQL failed", ex);
        }
    }

    private bool TryGetResultHeaderColumn(
        SharpConsoleUI.Events.MouseEventArgs args,
        out int columnIndex
    )
    {
        columnIndex = -1;
        if (
            args.Position.Y != 0
            || resultDataSource.QueryResult is null
            || resultDataSource.ColumnCount == 0
        )
        {
            return false;
        }

        var x = args.Position.X + resultTable.HorizontalScrollOffset;
        var currentX = 0;
        for (var index = 0; index < resultDataSource.ColumnCount; index++)
        {
            var width =
                resultDataSource.GetColumnWidth(index)
                ?? Math.Max(8, resultDataSource.GetColumnHeader(index).Length + 2);
            if (x >= currentX && x < currentX + width)
            {
                columnIndex = index;
                return true;
            }

            currentX += width + (resultTable.ColumnSeparator.HasValue ? 1 : 0);
        }

        return false;
    }

    private void OnEditorContentChanged(MultilineEditControl editor, string content)
    {
        if (suppressCompletion || !ReferenceEquals(editor, ActiveEditor))
        {
            return;
        }

        var prefix = SqlCompletionService.GetCurrentPrefixAtCursor(
            content,
            editor.CurrentLine,
            editor.CurrentColumn
        );
        if (prefix.Length < 2)
        {
            completionController.Dismiss();
            lastCompletionEditor = null;
            lastCompletionPrefix = string.Empty;
            return;
        }

        if (ReferenceEquals(lastCompletionEditor, editor) && lastCompletionPrefix == prefix)
        {
            return;
        }

        var suggestions = completionService.GetSuggestions(
            string.Empty,
            currentObjects,
            knownColumnsByObject.Values.SelectMany(columns => columns),
            lastResult?.Columns ?? [],
            maxSuggestions: 300
        );
        completionController.ShowOrUpdate(editor, suggestions, prefix);
        if (completionController.IsOpen)
        {
            lastCompletionEditor = editor;
            lastCompletionPrefix = prefix;
        }
        else
        {
            lastCompletionEditor = null;
            lastCompletionPrefix = string.Empty;
        }
    }

    private void AcceptCompletion(SqlCompletionSuggestion suggestion, int filterLength)
    {
        var editor = ActiveEditor;
        if (editor is null)
        {
            return;
        }

        suppressCompletion = true;
        lastCompletionEditor = null;
        lastCompletionPrefix = string.Empty;
        try
        {
            if (filterLength > 0)
            {
                editor.DeleteCharsBefore(filterLength);
            }

            editor.InsertText(suggestion.ReplacementText);
            editor.RequestFocus();
            UpdateStatus($"Inserted completion: {suggestion.ReplacementText}.");
        }
        finally
        {
            suppressCompletion = false;
        }
    }

    private void OnPreviewKeyPressed(object? sender, KeyPressedEventArgs args)
    {
        if (contextMenuController.ProcessKey(args))
        {
            return;
        }

        completionController.ProcessKey(args);
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
            args.KeyInfo.Key == ConsoleKey.S
            && args.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control)
        )
        {
            _ = SaveSqlAsync();
            args.Handled = true;
            return;
        }

        if (
            args.KeyInfo.Key == ConsoleKey.Q
            && args.KeyInfo.Modifiers.HasFlag(ConsoleModifiers.Control)
        )
        {
            ExitApplication();
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
        AppendMessage(SqlExecutionMessageLevel.Error, title, exception.Message);
        ShowNotification(title, TruncateStatus(exception.Message), NotificationSeverity.Danger);
    }

    private void AppendMessage(SqlExecutionMessageLevel level, string title, string message)
    {
        var color = level switch
        {
            SqlExecutionMessageLevel.Error => "red",
            SqlExecutionMessageLevel.Warning => "yellow",
            _ => "grey70",
        };
        var line = $"{DateTime.Now:HH:mm:ss} {level}: {title}: {message}";
        foreach (var chunk in Wrap(line, 170))
        {
            errorMessageLines.Add($"[{color}]{MarkupParser.Escape(chunk)}[/]");
        }

        if (errorMessageLines.Count > MaxMessageLines)
        {
            errorMessageLines.RemoveRange(0, errorMessageLines.Count - MaxMessageLines);
        }

        messagesPanel?.SetContent(errorMessageLines);
        UpdateStatus($"{level}: {title}: {message}");

        if (
            level == SqlExecutionMessageLevel.Error
            && bottomTabs is not null
            && bottomTabs.ActiveTabIndex != MessagesTabIndex
        )
        {
            bottomTabs.ActiveTabIndex = MessagesTabIndex;
        }
    }

    private void ShowNotification(string title, string message, NotificationSeverity severity)
    {
        windowSystem.NotificationStateService.ShowNotification(
            title,
            TruncateStatus(message),
            severity
        );
    }

    private void UpdateStatus(string message)
    {
        statusLine?.SetContent([$"[grey70]{MarkupParser.Escape(TruncateStatus(message))}[/]"]);
    }

    private static string TruncateStatus(string message)
    {
        var singleLine = message.ReplaceLineEndings(" ");
        return singleLine.Length <= MaxStatusLength
            ? singleLine
            : string.Concat(singleLine.AsSpan(0, MaxStatusLength - 3), "...");
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        if (text.Length <= width)
        {
            yield return text;
            yield break;
        }

        for (var index = 0; index < text.Length; index += width)
        {
            yield return text.Substring(index, Math.Min(width, text.Length - index));
        }
    }

    private void ExitApplication()
    {
        windowSystem.RequestExit(0);
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
