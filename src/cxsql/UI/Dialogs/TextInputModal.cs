using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Extensions;

namespace CxSql.UI.Dialogs;

public sealed class TextInputModal : ModalBase<string?>
{
    private readonly string title;
    private readonly string prompt;
    private readonly string initialValue;
    private readonly string hint;

    private TextInputModal(
        ConsoleWindowSystem windowSystem,
        Window? parentWindow,
        string title,
        string prompt,
        string initialValue,
        string hint
    )
        : base(windowSystem, parentWindow)
    {
        this.title = title;
        this.prompt = prompt;
        this.initialValue = initialValue;
        this.hint = hint;
    }

    public static Task<string?> ShowAsync(
        ConsoleWindowSystem windowSystem,
        Window? parentWindow,
        string title,
        string prompt,
        string initialValue = "",
        string hint = "Enter: Confirm  Esc: Cancel"
    )
    {
        return new TextInputModal(
            windowSystem,
            parentWindow,
            title,
            prompt,
            initialValue,
            hint
        ).ShowAsync();
    }

    protected override string? GetDefaultResult()
    {
        return null;
    }

    protected override string GetTitle()
    {
        return title;
    }

    protected override int GetHeight()
    {
        return 8;
    }

    protected override void BuildContent()
    {
        var input = Controls
            .Prompt(prompt)
            .WithInput(initialValue)
            .WithInputWidth(38)
            .UnfocusOnEnter(false)
            .WithMargin(1, 1, 1, 0)
            .OnEntered(
                (_, value) =>
                {
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        CloseWithResult(value.Trim());
                    }
                }
            )
            .Build();

        Modal!.AddControl(input);
        Modal.AddControl(
            Controls
                .Markup()
                .AddLine($"[grey70]{hint}[/]")
                .StickyBottom()
                .WithMargin(1, 0, 1, 0)
                .Build()
        );
        input.RequestFocus();
    }
}
