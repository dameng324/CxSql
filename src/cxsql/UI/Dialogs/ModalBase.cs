using SharpConsoleUI;
using SharpConsoleUI.Builders;

namespace CxSql.UI.Dialogs;

public abstract class ModalBase<TResult>
{
    private TaskCompletionSource<TResult>? completion;
    private bool closedWithResult;

    protected ModalBase(ConsoleWindowSystem windowSystem, Window? parentWindow)
    {
        WindowSystem = windowSystem;
        ParentWindow = parentWindow;
    }

    protected ConsoleWindowSystem WindowSystem { get; }

    protected Window? ParentWindow { get; }

    protected Window? Modal { get; private set; }

    public Task<TResult> ShowAsync()
    {
        completion = new TaskCompletionSource<TResult>();
        closedWithResult = false;
        Modal = CreateWindow();
        BuildContent();

        Modal.OnClosed += (_, _) =>
        {
            if (!closedWithResult)
            {
                completion.TrySetResult(GetDefaultResult());
            }
        };

        WindowSystem.AddWindow(Modal);
        WindowSystem.SetActiveWindow(Modal);
        return completion.Task;
    }

    protected void CloseWithResult(TResult result)
    {
        closedWithResult = true;
        var modal = Modal;
        Modal = null;

        if (modal is not null)
        {
            WindowSystem.CloseWindow(modal);
        }

        completion?.TrySetResult(result);
    }

    protected abstract void BuildContent();

    protected abstract TResult GetDefaultResult();

    protected virtual Window CreateWindow()
    {
        return new WindowBuilder(WindowSystem)
            .WithTitle(GetTitle())
            .WithSize(GetWidth(), GetHeight())
            .Centered()
            .AsModal()
            .Minimizable(false)
            .Maximizable(false)
            .WithBorderStyle(BorderStyle.Rounded)
            .WithBorderColor(Color.Grey50)
            .OnKeyPressed(
                (_, args) =>
                {
                    if (args.KeyInfo.Key == ConsoleKey.Escape)
                    {
                        CloseWithResult(GetDefaultResult());
                        args.Handled = true;
                    }
                }
            )
            .Build();
    }

    protected virtual string GetTitle()
    {
        return string.Empty;
    }

    protected virtual int GetWidth()
    {
        return 56;
    }

    protected virtual int GetHeight()
    {
        return 12;
    }
}
