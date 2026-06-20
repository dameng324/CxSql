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
    private PromptControl? namePrompt;
    private PromptControl? connectionStringPrompt;
    private TabControl? typeTabs;
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
        return 19;
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

        connectionStringPrompt = Controls
            .Prompt(ConnectionInputMapper.GetInputLabel(selectedType))
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
        Modal.AddControl(connectionStringPrompt);
        Modal.AddControl(connectionHelp);
        Modal.AddControl(testStatus);
        Modal.AddControl(buttons);
        Modal.AddControl(
            Controls
                .Markup()
                .AddLine("[grey70]Test/Create/Cancel are buttons. Esc closes the dialog.[/]")
                .AddLine(
                    "[grey50]For SQLite, enter a database file path; cxsql builds the connection string.[/]"
                )
                .StickyBottom()
                .WithMargin(1, 0, 1, 0)
                .Build()
        );

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
        if (connectionStringPrompt is not null)
        {
            connectionStringPrompt.Prompt = ConnectionInputMapper.GetInputLabel(selectedType);
            connectionStringPrompt.InputWidth = selectedType == DatabaseType.Sqlite ? 58 : 48;
        }

        connectionStringPrompt?.SetInput(string.Empty);
        connectionHelp?.SetContent([
            $"[grey50]{Escape(ConnectionInputMapper.GetHelpText(selectedType))}[/]",
        ]);
        testStatus?.SetContent([
            "[grey50]Test validates the current connection settings before Create.[/]",
        ]);
        connectionStringPrompt?.RequestFocus();
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
        var rawConnection = connectionStringPrompt?.Input.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(rawConnection))
        {
            testStatus?.SetContent([
                "[yellow]Name and connection value are required before testing or creating.[/]",
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
}
