using CxSql.UI.Components;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;

namespace CxSql.UI.Dialogs;

public sealed class ResultGridFilterDialog : ModalBase<ResultGridFilterRequest?>
{
    private readonly string columnName;
    private ResultGridFilterOperator selectedOperator = ResultGridFilterOperator.Equal;
    private PromptControl? valuePrompt;
    private MarkupControl? status;

    private ResultGridFilterDialog(
        ConsoleWindowSystem windowSystem,
        Window? parentWindow,
        string columnName
    )
        : base(windowSystem, parentWindow)
    {
        this.columnName = columnName;
    }

    public static Task<ResultGridFilterRequest?> ShowAsync(
        ConsoleWindowSystem windowSystem,
        Window? parentWindow,
        string columnName
    )
    {
        return new ResultGridFilterDialog(windowSystem, parentWindow, columnName).ShowAsync();
    }

    protected override ResultGridFilterRequest? GetDefaultResult()
    {
        return null;
    }

    protected override string GetTitle()
    {
        return "ResultGrid Filter";
    }

    protected override int GetWidth()
    {
        return 68;
    }

    protected override int GetHeight()
    {
        return 13;
    }

    protected override void BuildContent()
    {
        Modal!.AddControl(
            Controls
                .Markup($"[grey70]Column:[/] [white]{MarkupParser.Escape(columnName)}[/]")
                .WithMargin(1, 1, 1, 0)
                .Build()
        );

        var operatorTabs = new TabControlBuilder()
            .WithHeaderStyle(TabHeaderStyle.Classic)
            .WithActiveTab(0)
            .WithHeight(3)
            .WithMargin(1, 0, 1, 0)
            .AddTab("Equal", Controls.Markup("[grey70]=[/]").WithMargin(1, 0, 1, 0).Build())
            .AddTab("Contain", Controls.Markup("[grey70]LIKE[/]").WithMargin(1, 0, 1, 0).Build())
            .AddTab(">", Controls.Markup("[grey70]>[/]").WithMargin(1, 0, 1, 0).Build())
            .AddTab("<", Controls.Markup("[grey70]<[/]").WithMargin(1, 0, 1, 0).Build())
            .AddTab(">=", Controls.Markup("[grey70]>=[/]").WithMargin(1, 0, 1, 0).Build())
            .AddTab("<=", Controls.Markup("[grey70]<=[/]").WithMargin(1, 0, 1, 0).Build())
            .Build();
        operatorTabs.ActiveFocusedBackgroundColor = new Color(24, 104, 64);
        operatorTabs.ActiveUnfocusedBackgroundColor = new Color(24, 104, 64);
        operatorTabs.ActiveFocusedForegroundColor = Color.White;
        operatorTabs.ActiveUnfocusedForegroundColor = Color.White;
        operatorTabs.TabChanged += (_, _) =>
        {
            selectedOperator = operatorTabs.ActiveTabIndex switch
            {
                1 => ResultGridFilterOperator.Contains,
                2 => ResultGridFilterOperator.GreaterThan,
                3 => ResultGridFilterOperator.LessThan,
                4 => ResultGridFilterOperator.GreaterThanOrEqual,
                5 => ResultGridFilterOperator.LessThanOrEqual,
                _ => ResultGridFilterOperator.Equal,
            };
        };

        valuePrompt = Controls
            .Prompt("Value: ")
            .WithInputWidth(46)
            .WithMargin(1, 1, 1, 0)
            .UnfocusOnEnter(false)
            .OnEntered((_, _) => Submit())
            .Build();

        status = Controls
            .Markup("[grey50]Choose an operator, enter a value, then apply.[/]")
            .WithMargin(1, 0, 1, 0)
            .Build();

        var buttons = Controls
            .HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Left)
            .Column(column =>
                column.Width(14).Add(CreateDialogButton("Apply", DialogButtonKind.Primary, Submit))
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

        Modal.AddControl(operatorTabs);
        Modal.AddControl(valuePrompt);
        Modal.AddControl(status);
        Modal.AddControl(buttons);

        valuePrompt.RequestFocus();
    }

    private void Submit()
    {
        var value = valuePrompt?.Input.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            status?.SetContent(["[yellow]Filter value is required.[/]"]);
            return;
        }

        CloseWithResult(new ResultGridFilterRequest(columnName, selectedOperator, value));
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

    private enum DialogButtonKind
    {
        Primary,
        Cancel,
    }
}
