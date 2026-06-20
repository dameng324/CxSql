using SharpConsoleUI.Controls;

namespace CxSql.UI;

public static class ConsoleExShell
{
    public const string FrameworkName = "SharpConsoleUI";

    public static IReadOnlyList<string> PlannedControls { get; } =
    [
        nameof(ToolbarControl),
        nameof(TreeControl),
        nameof(TabControl),
        nameof(MultilineEditControl),
        nameof(TableControl),
        nameof(SplitterControl),
    ];
}
