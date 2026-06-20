using CxSql.Models;
using Microsoft.Data.Sqlite;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;

namespace CxSql.UI.Dialogs;

public sealed record NewConnectionRequest(
    string Name,
    DatabaseType DatabaseType,
    string ConnectionString
);

public sealed class NewConnectionModal : ModalBase<NewConnectionRequest?>
{
    private DatabaseType selectedType = DatabaseType.Sqlite;
    private PromptControl? namePrompt;
    private PromptControl? connectionStringPrompt;
    private MarkupControl? typeStatus;

    private NewConnectionModal(ConsoleWindowSystem windowSystem, Window? parentWindow)
        : base(windowSystem, parentWindow) { }

    public static Task<NewConnectionRequest?> ShowAsync(
        ConsoleWindowSystem windowSystem,
        Window? parentWindow
    )
    {
        return new NewConnectionModal(windowSystem, parentWindow).ShowAsync();
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
        return 16;
    }

    protected override NewConnectionRequest? GetDefaultResult()
    {
        return null;
    }

    protected override void BuildContent()
    {
        typeStatus = Controls.Markup("[bold]Type:[/] SQLite").WithMargin(1, 1, 1, 0).Build();

        var typeButtons = Controls
            .Toolbar()
            .WithSpacing(1)
            .WithMargin(1, 0, 1, 0)
            .AddButton("SQLite", (_, _) => SetType(DatabaseType.Sqlite))
            .AddButton("PostgreSQL", (_, _) => SetType(DatabaseType.PostgreSql))
            .AddButton("SQL Server", (_, _) => SetType(DatabaseType.SqlServer))
            .Build();

        namePrompt = Controls.Prompt("Name: ").WithInputWidth(58).WithMargin(1, 1, 1, 0).Build();

        connectionStringPrompt = Controls
            .Prompt("Path/connection string: ")
            .WithInputWidth(46)
            .WithMargin(1, 0, 1, 0)
            .Build();

        var buttons = Controls
            .HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Left)
            .Column(column =>
                column.Width(14).Add(Controls.Button("Create").OnClick((_, _) => Submit()).Build())
            )
            .Column(column =>
                column
                    .Width(14)
                    .Add(Controls.Button("Cancel").OnClick((_, _) => CloseWithResult(null)).Build())
            )
            .WithMargin(1, 1, 1, 0)
            .Build();

        Modal!.AddControl(typeStatus);
        Modal.AddControl(typeButtons);
        Modal.AddControl(namePrompt);
        Modal.AddControl(connectionStringPrompt);
        Modal.AddControl(buttons);
        Modal.AddControl(
            Controls
                .Markup()
                .AddLine("[grey70]Create/Cancel are buttons. Esc closes the dialog.[/]")
                .AddLine(
                    "[grey50]For SQLite, enter a database file path; cxsql builds the connection string.[/]"
                )
                .StickyBottom()
                .WithMargin(1, 0, 1, 0)
                .Build()
        );

        namePrompt.RequestFocus();
    }

    private void SetType(DatabaseType databaseType)
    {
        selectedType = databaseType;
        typeStatus?.SetContent([$"[bold]Type:[/] {databaseType}"]);
        connectionStringPrompt?.SetInput(string.Empty);
        connectionStringPrompt?.RequestFocus();
    }

    private void Submit()
    {
        var name = namePrompt?.Input.Trim() ?? string.Empty;
        var rawConnection = connectionStringPrompt?.Input.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(rawConnection))
        {
            WindowSystem.NotificationStateService.ShowNotification(
                "New Connection",
                "Name and path/connection string are required.",
                SharpConsoleUI.Core.NotificationSeverity.Warning
            );
            return;
        }

        var connectionString =
            selectedType == DatabaseType.Sqlite
                ? new SqliteConnectionStringBuilder
                {
                    DataSource = Path.GetFullPath(rawConnection),
                    Mode = SqliteOpenMode.ReadWriteCreate,
                }.ToString()
                : rawConnection;

        CloseWithResult(new NewConnectionRequest(name, selectedType, connectionString));
    }
}
