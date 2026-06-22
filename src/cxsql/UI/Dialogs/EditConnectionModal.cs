using CxSql.Application.Services;
using CxSql.Models;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;

namespace CxSql.UI.Dialogs;

public sealed class EditConnectionModal : ModalBase<NewConnectionRequest?>
{
    private readonly DatabaseConnection connection;
    private readonly Func<NewConnectionRequest, Task<ConnectionTestResult>> testConnectionAsync;
    private PromptControl? namePrompt;
    private PromptControl? connectionValuePrompt;
    private MarkupControl? testStatus;
    private bool testing;

    private EditConnectionModal(
        ConsoleWindowSystem windowSystem,
        Window? parentWindow,
        DatabaseConnection connection,
        Func<NewConnectionRequest, Task<ConnectionTestResult>> testConnectionAsync
    )
        : base(windowSystem, parentWindow)
    {
        this.connection = connection;
        this.testConnectionAsync = testConnectionAsync;
    }

    public static Task<NewConnectionRequest?> ShowAsync(
        ConsoleWindowSystem windowSystem,
        Window? parentWindow,
        DatabaseConnection connection,
        Func<NewConnectionRequest, Task<ConnectionTestResult>> testConnectionAsync
    )
    {
        return new EditConnectionModal(
            windowSystem,
            parentWindow,
            connection,
            testConnectionAsync
        ).ShowAsync();
    }

    protected override string GetTitle()
    {
        return "Edit Connection";
    }

    protected override int GetWidth()
    {
        return 76;
    }

    protected override int GetHeight()
    {
        return 16;
    }

    protected override NewConnectionRequest? GetDefaultResult()
    {
        return null;
    }

    protected override void BuildContent()
    {
        var databaseType = connection.DatabaseType;
        Modal!.AddControl(
            Controls
                .Markup(
                    $"[bold]Type:[/] {ConnectionInputMapper.GetDisplayType(databaseType)} [grey50](close the connection before editing)[/]"
                )
                .WithMargin(1, 1, 1, 0)
                .Build()
        );

        namePrompt = Controls
            .Prompt("Name: ")
            .WithInput(connection.Name)
            .WithInputWidth(54)
            .WithMargin(1, 1, 1, 0)
            .Build();

        connectionValuePrompt = Controls
            .Prompt(ConnectionInputMapper.GetInputLabel(databaseType))
            .WithInput(
                ConnectionInputMapper.ToDisplayValue(databaseType, connection.ConnectionString)
            )
            .WithInputWidth(databaseType == DatabaseType.Sqlite ? 52 : 42)
            .WithMargin(1, 0, 1, 0)
            .Build();

        testStatus = Controls
            .Markup($"[grey50]{Escape(ConnectionInputMapper.GetHelpText(databaseType))}[/]")
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
                column.Width(14).Add(CreateDialogButton("Save", DialogButtonKind.Primary, Submit))
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

        Modal.AddControl(namePrompt);
        Modal.AddControl(connectionValuePrompt);
        Modal.AddControl(testStatus);
        Modal.AddControl(buttons);
        Modal.AddControl(
            Controls
                .Markup(
                    "[grey70]Test validates the current value. Save writes the closed connection profile.[/]"
                )
                .StickyBottom()
                .WithMargin(1, 0, 1, 0)
                .Build()
        );

        connectionValuePrompt.RequestFocus();
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
            testStatus?.SetContent(
                result.Succeeded
                    ? ["[green]Connection test succeeded.[/]"]
                    :
                    [
                        $"[red]Connection test failed:[/] [grey70]{Escape(result.ErrorMessage ?? "Unknown error.")}[/]",
                    ]
            );
        }
        finally
        {
            testing = false;
        }
    }

    private bool TryBuildRequest(out NewConnectionRequest request)
    {
        var name = namePrompt?.Input.Trim() ?? string.Empty;
        var rawConnection = connectionValuePrompt?.Input.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(rawConnection))
        {
            testStatus?.SetContent([
                "[yellow]Name and connection value are required before testing or saving.[/]",
            ]);
            request = new NewConnectionRequest(string.Empty, connection.DatabaseType, string.Empty);
            return false;
        }

        request = new NewConnectionRequest(
            name,
            connection.DatabaseType,
            ConnectionInputMapper.ToConnectionString(connection.DatabaseType, rawConnection)
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
