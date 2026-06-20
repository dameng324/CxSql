namespace CxSql.UI.Components;

public static class ToolbarCatalog
{
    public static IReadOnlyList<ToolbarButton> Buttons { get; } =
    [
        new('1', ToolbarAction.NewConnection, "New Connection", null),
        new('2', ToolbarAction.OpenConnection, "Open Connection", null),
        new('3', ToolbarAction.NewQuery, "New Query", null),
        new('4', ToolbarAction.Execute, "Execute", "F5"),
        new('5', ToolbarAction.Stop, "Stop", null),
        new('6', ToolbarAction.SaveSql, "Save SQL", "Ctrl+S"),
        new('7', ToolbarAction.Export, "Export", null),
        new('8', ToolbarAction.Refresh, "Refresh", null),
        new('9', ToolbarAction.ListConnections, "Connections", null),
        new('0', ToolbarAction.Exit, "Exit", "Ctrl+Q"),
    ];

    public static bool TryGetAction(char buttonKey, out ToolbarAction action)
    {
        var button = Buttons.FirstOrDefault(item => item.ButtonKey == buttonKey);
        if (button is null)
        {
            action = default;
            return false;
        }

        action = button.Action;
        return true;
    }
}
