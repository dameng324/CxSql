namespace CxSql.UI.Components;

public sealed record AppShortcut(
    string DisplayText,
    ConsoleKey Key,
    ConsoleModifiers Modifiers,
    ToolbarAction? Action
);

public static class ShortcutPolicy
{
    public static IReadOnlyList<AppShortcut> AllowedShortcuts { get; } =
    [
        new("F5", ConsoleKey.F5, 0, ToolbarAction.Execute),
        new("Ctrl+S", ConsoleKey.S, ConsoleModifiers.Control, ToolbarAction.SaveSql),
        new("Ctrl+Q", ConsoleKey.Q, ConsoleModifiers.Control, ToolbarAction.Exit),
        new("Esc", ConsoleKey.Escape, 0, null),
    ];

    public static bool TryGetToolbarAction(ConsoleKeyInfo keyInfo, out ToolbarAction action)
    {
        foreach (var shortcut in AllowedShortcuts)
        {
            if (
                shortcut.Action is not null
                && shortcut.Key == keyInfo.Key
                && shortcut.Modifiers == keyInfo.Modifiers
            )
            {
                action = shortcut.Action.Value;
                return true;
            }
        }

        action = default;
        return false;
    }
}
