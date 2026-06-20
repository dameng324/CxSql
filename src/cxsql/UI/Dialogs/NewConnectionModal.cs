using CxSql.Application.Services;
using CxSql.Models;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;

namespace CxSql.UI.Dialogs;

public sealed record NewConnectionRequest(
    string Name,
    DatabaseType DatabaseType,
    string ConnectionString
);

public sealed class NewConnectionModal : ModalBase<NewConnectionRequest?>
{
    private readonly Func<NewConnectionRequest, Task<ConnectionTestResult>> testConnectionAsync;
    private DatabaseType selectedType = DatabaseType.Sqlite;
    private NewConnectionInputMode selectedMode = NewConnectionInputMode.ConnectionString;
    private PromptControl? namePrompt;
    private PromptControl? connectionStringPrompt;
    private PromptControl? hostPrompt;
    private PromptControl? portPrompt;
    private PromptControl? databasePrompt;
    private PromptControl? usernamePrompt;
    private PromptControl? passwordPrompt;
    private TabControl? typeTabs;
    private TabControl? modeTabs;
    private MarkupControl? connectionHelp;
    private MarkupControl? testStatus;
    private bool testing;

    private NewConnectionModal(
        ConsoleWindowSystem windowSystem,
        Window? parentWindow,
        Func<NewConnectionRequest, Task<ConnectionTestResult>> testConnectionAsync
    )
        : base(windowSystem, parentWindow)
    {
        this.testConnectionAsync = testConnectionAsync;
    }

    public static Task<NewConnectionRequest?> ShowAsync(
        ConsoleWindowSystem windowSystem,
        Window? parentWindow,
        Func<NewConnectionRequest, Task<ConnectionTestResult>> testConnectionAsync
    )
    {
        return new NewConnectionModal(windowSystem, parentWindow, testConnectionAsync).ShowAsync();
    }

    protected override string GetTitle()
    {
        return "New Connection";
    }

    protected override int GetWidth()
    {
        return 78;
    }

    protected override int GetHeight()
    {
        return 25;
    }

    protected override NewConnectionRequest? GetDefaultResult()
    {
        return null;
    }

    protected override void BuildContent()
    {
        typeTabs = new TabControlBuilder()
            .WithHeaderStyle(TabHeaderStyle.Classic)
            .WithActiveTab(0)
            .WithHeight(4)
            .WithMargin(1, 1, 1, 0)
            .AddTab(
                "SQLite",
                Controls
                    .Markup("[grey70]Local database file. Use a file path below.[/]")
                    .WithMargin(1, 0, 1, 0)
                    .Build()
            )
            .AddTab(
                "PostgreSQL",
                Controls
                    .Markup(
                        "[grey70]Network database. Use a PostgreSQL connection string below.[/]"
                    )
                    .WithMargin(1, 0, 1, 0)
                    .Build()
            )
            .AddTab(
                "SQL Server",
                Controls
                    .Markup(
                        "[grey70]Network database. Use a SQL Server connection string below.[/]"
                    )
                    .WithMargin(1, 0, 1, 0)
                    .Build()
            )
            .Build();
        typeTabs.ActiveFocusedBackgroundColor = new Color(24, 104, 64);
        typeTabs.ActiveUnfocusedBackgroundColor = new Color(24, 104, 64);
        typeTabs.ActiveFocusedForegroundColor = Color.White;
        typeTabs.ActiveUnfocusedForegroundColor = Color.White;
        typeTabs.InactiveFocusedForegroundColor = Color.Grey70;
        typeTabs.InactiveUnfocusedForegroundColor = Color.Grey50;
        typeTabs.TabChanged += (_, _) => SetTypeFromTab();

        namePrompt = Controls.Prompt("Name: ").WithInputWidth(58).WithMargin(1, 1, 1, 0).Build();

        modeTabs = new TabControlBuilder()
            .WithHeaderStyle(TabHeaderStyle.Classic)
            .WithActiveTab(0)
            .WithHeight(4)
            .WithMargin(1, 0, 1, 0)
            .AddTab(
                "Server",
                Controls
                    .Markup(
                        "[grey70]Build a connection string from IP/host, port, username and password.[/]"
                    )
                    .WithMargin(1, 0, 1, 0)
                    .Build()
            )
            .AddTab(
                "Connection String",
                Controls
                    .Markup("[grey70]Paste the provider connection string directly.[/]")
                    .WithMargin(1, 0, 1, 0)
                    .Build()
            )
            .Build();
        modeTabs.ActiveFocusedBackgroundColor = new Color(28, 76, 130);
        modeTabs.ActiveUnfocusedBackgroundColor = new Color(28, 76, 130);
        modeTabs.ActiveFocusedForegroundColor = Color.White;
        modeTabs.ActiveUnfocusedForegroundColor = Color.White;
        modeTabs.InactiveFocusedForegroundColor = Color.Grey70;
        modeTabs.InactiveUnfocusedForegroundColor = Color.Grey50;
        modeTabs.TabChanged += (_, _) => SetModeFromTab();

        connectionStringPrompt = Controls
            .Prompt(ConnectionInputMapper.GetInputLabel(selectedType))
            .WithInputWidth(58)
            .WithMargin(1, 0, 1, 0)
            .Build();

        hostPrompt = Controls.Prompt("IP/Host: ").WithInputWidth(58).WithMargin(1, 0, 1, 0).Build();
        portPrompt = Controls
            .Prompt("Port: ")
            .WithInput(ConnectionInputMapper.GetDefaultPort(DatabaseType.PostgreSql).ToString())
            .WithInputWidth(12)
            .WithMargin(1, 0, 1, 0)
            .Build();
        databasePrompt = Controls
            .Prompt("Database (optional): ")
            .WithInputWidth(46)
            .WithMargin(1, 0, 1, 0)
            .Build();
        usernamePrompt = Controls
            .Prompt("Username: ")
            .WithInputWidth(58)
            .WithMargin(1, 0, 1, 0)
            .Build();
        passwordPrompt = Controls
            .Prompt("Password: ")
            .WithMaskCharacter('*')
            .WithInputWidth(58)
            .WithMargin(1, 0, 1, 0)
            .Build();

        connectionHelp = Controls
            .Markup($"[grey50]{Escape(ConnectionInputMapper.GetHelpText(selectedType))}[/]")
            .WithMargin(1, 0, 1, 0)
            .Build();

        testStatus = Controls
            .Markup("[grey50]Test validates the current connection settings before Create.[/]")
            .WithMargin(1, 0, 1, 0)
            .Build();

        var buttons = Controls
            .HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Left)
            .Column(column =>
                column
                    .Width(12)
                    .Add(CreateDialogButton("Test", DialogButtonKind.Test, () => _ = TestAsync()))
            )
            .Column(column =>
                column.Width(14).Add(CreateDialogButton("Create", DialogButtonKind.Primary, Submit))
            )
            .Column(column =>
                column
                    .Width(14)
                    .Add(
                        CreateDialogButton(
                            "Cancel",
                            DialogButtonKind.Cancel,
                            () => CloseWithResult(null)
                        )
                    )
            )
            .WithMargin(1, 1, 1, 0)
            .Build();

        Modal!.AddControl(typeTabs);
        Modal.AddControl(namePrompt);
        Modal.AddControl(modeTabs);
        Modal.AddControl(connectionStringPrompt);
        Modal.AddControl(hostPrompt);
        Modal.AddControl(portPrompt);
        Modal.AddControl(databasePrompt);
        Modal.AddControl(usernamePrompt);
        Modal.AddControl(passwordPrompt);
        Modal.AddControl(connectionHelp);
        Modal.AddControl(testStatus);
        Modal.AddControl(buttons);
        Modal.AddControl(
            Controls
                .Markup()
                .AddLine("[grey70]Test/Create/Cancel are buttons. Esc closes the dialog.[/]")
                .AddLine(
                    "[grey50]SQLite uses a file path. PostgreSQL and SQL Server can use Server fields or a direct connection string.[/]"
                )
                .StickyBottom()
                .WithMargin(1, 0, 1, 0)
                .Build()
        );

        UpdateInputControls(resetValues: false);
        namePrompt.RequestFocus();
    }

    private void SetTypeFromTab()
    {
        selectedType = typeTabs?.ActiveTabIndex switch
        {
            1 => DatabaseType.PostgreSql,
            2 => DatabaseType.SqlServer,
            _ => DatabaseType.Sqlite,
        };
        selectedMode =
            selectedType == DatabaseType.Sqlite
                ? NewConnectionInputMode.ConnectionString
                : NewConnectionInputMode.ServerFields;
        if (modeTabs is not null)
        {
            modeTabs.ActiveTabIndex = 0;
        }

        UpdateInputControls(resetValues: true);
        GetPrimaryInput()?.RequestFocus();
    }

    private void SetModeFromTab()
    {
        if (selectedType == DatabaseType.Sqlite)
        {
            selectedMode = NewConnectionInputMode.ConnectionString;
        }
        else
        {
            selectedMode =
                modeTabs?.ActiveTabIndex == 1
                    ? NewConnectionInputMode.ConnectionString
                    : NewConnectionInputMode.ServerFields;
        }

        UpdateInputControls(resetValues: false);
        GetPrimaryInput()?.RequestFocus();
    }

    private void UpdateInputControls(bool resetValues)
    {
        var isSqlite = selectedType == DatabaseType.Sqlite;
        var isServerFields = !isSqlite && selectedMode == NewConnectionInputMode.ServerFields;
        var isConnectionString =
            isSqlite || selectedMode == NewConnectionInputMode.ConnectionString;

        if (connectionStringPrompt is not null)
        {
            connectionStringPrompt.Prompt = ConnectionInputMapper.GetInputLabel(selectedType);
            connectionStringPrompt.InputWidth = isSqlite ? 58 : 48;
            connectionStringPrompt.Visible = isConnectionString;
            if (resetValues)
            {
                connectionStringPrompt.SetInput(string.Empty);
            }
        }

        if (modeTabs is not null)
        {
            modeTabs.Visible = !isSqlite;
        }

        SetServerFieldVisibility(isServerFields);
        if (resetValues)
        {
            hostPrompt?.SetInput(string.Empty);
            portPrompt?.SetInput(ConnectionInputMapper.GetDefaultPort(selectedType).ToString());
            databasePrompt?.SetInput(string.Empty);
            usernamePrompt?.SetInput(string.Empty);
            passwordPrompt?.SetInput(string.Empty);
        }

        connectionHelp?.SetContent([$"[grey50]{Escape(BuildHelpText())}[/]"]);
        testStatus?.SetContent([
            "[grey50]Test validates the current connection settings before Create.[/]",
        ]);
    }

    private void SetServerFieldVisibility(bool visible)
    {
        if (hostPrompt is not null)
        {
            hostPrompt.Visible = visible;
        }

        if (portPrompt is not null)
        {
            portPrompt.Visible = visible;
        }

        if (databasePrompt is not null)
        {
            databasePrompt.Visible = visible;
        }

        if (usernamePrompt is not null)
        {
            usernamePrompt.Visible = visible;
        }

        if (passwordPrompt is not null)
        {
            passwordPrompt.Visible = visible;
        }
    }

    private PromptControl? GetPrimaryInput()
    {
        return
            selectedMode == NewConnectionInputMode.ServerFields
            && selectedType != DatabaseType.Sqlite
            ? hostPrompt
            : connectionStringPrompt;
    }

    private string BuildHelpText()
    {
        if (selectedType == DatabaseType.Sqlite)
        {
            return ConnectionInputMapper.GetHelpText(selectedType);
        }

        if (selectedMode == NewConnectionInputMode.ConnectionString)
        {
            return ConnectionInputMapper.GetHelpText(selectedType);
        }

        var port = ConnectionInputMapper.GetDefaultPort(selectedType);
        return selectedType == DatabaseType.SqlServer
            ? $"Server mode builds a SQL Server connection string. Default port is {port}; TrustServerCertificate=True is applied for terminal setup."
            : $"Server mode builds a PostgreSQL connection string. Default port is {port}.";
    }

    private void Submit()
    {
        if (!TryBuildRequest(out var request))
        {
            return;
        }

        CloseWithResult(request);
    }

    private async Task TestAsync()
    {
        if (testing || !TryBuildRequest(out var request))
        {
            return;
        }

        testing = true;
        testStatus?.SetContent(["[yellow]Testing connection...[/]"]);
        try
        {
            var result = await testConnectionAsync(request);
            if (result.Succeeded)
            {
                testStatus?.SetContent(["[green]Connection test succeeded.[/]"]);
                return;
            }

            testStatus?.SetContent([
                $"[red]Connection test failed:[/] [grey70]{Escape(result.ErrorMessage ?? "Unknown error.")}[/]",
            ]);
        }
        finally
        {
            testing = false;
        }
    }

    private bool TryBuildRequest(out NewConnectionRequest request)
    {
        var name = namePrompt?.Input.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            testStatus?.SetContent(["[yellow]Name is required before testing or creating.[/]"]);
            request = new NewConnectionRequest(string.Empty, selectedType, string.Empty);
            return false;
        }

        if (
            selectedMode == NewConnectionInputMode.ServerFields
            && selectedType != DatabaseType.Sqlite
        )
        {
            return TryBuildServerRequest(name, out request);
        }

        var rawConnection = connectionStringPrompt?.Input.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawConnection))
        {
            testStatus?.SetContent([
                "[yellow]Connection value is required before testing or creating.[/]",
            ]);
            request = new NewConnectionRequest(string.Empty, selectedType, string.Empty);
            return false;
        }

        request = new NewConnectionRequest(
            name,
            selectedType,
            ConnectionInputMapper.ToConnectionString(selectedType, rawConnection)
        );
        return true;
    }

    private bool TryBuildServerRequest(string name, out NewConnectionRequest request)
    {
        var host = hostPrompt?.Input.Trim() ?? string.Empty;
        var portText = portPrompt?.Input.Trim() ?? string.Empty;
        var database = databasePrompt?.Input.Trim();
        var username = usernamePrompt?.Input.Trim() ?? string.Empty;
        var password = passwordPrompt?.Input ?? string.Empty;
        if (
            string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password)
        )
        {
            testStatus?.SetContent([
                "[yellow]IP/Host, username and password are required in Server mode.[/]",
            ]);
            request = new NewConnectionRequest(string.Empty, selectedType, string.Empty);
            return false;
        }

        if (!int.TryParse(portText, out var port) || port <= 0 || port > 65535)
        {
            testStatus?.SetContent(["[yellow]Port must be a number between 1 and 65535.[/]"]);
            request = new NewConnectionRequest(string.Empty, selectedType, string.Empty);
            return false;
        }

        request = new NewConnectionRequest(
            name,
            selectedType,
            ConnectionInputMapper.ToServerConnectionString(
                selectedType,
                new ServerConnectionFields(host, port, database, username, password)
            )
        );
        return true;
    }

    private static ButtonControl CreateDialogButton(
        string text,
        DialogButtonKind kind,
        Action action
    )
    {
        var (background, focusedBackground, foreground) = kind switch
        {
            DialogButtonKind.Primary => (
                new Color(24, 104, 64),
                new Color(34, 139, 86),
                Color.White
            ),
            DialogButtonKind.Test => (new Color(28, 76, 130), new Color(38, 103, 175), Color.White),
            _ => (Color.Grey23, Color.Grey35, Color.Grey93),
        };

        return Controls
            .Button(text)
            .WithBackgroundColor(background)
            .WithForegroundColor(foreground)
            .WithFocusedBackgroundColor(focusedBackground)
            .WithFocusedForegroundColor(Color.White)
            .OnClick((_, _) => action())
            .Build();
    }

    private static string Escape(string value)
    {
        return MarkupParser.Escape(value);
    }

    private enum DialogButtonKind
    {
        Test,
        Primary,
        Cancel,
    }

    private enum NewConnectionInputMode
    {
        ServerFields,
        ConnectionString,
    }
}
