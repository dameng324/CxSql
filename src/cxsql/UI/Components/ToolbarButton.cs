namespace CxSql.UI.Components;

public sealed record ToolbarButton(
    char ButtonKey,
    ToolbarAction Action,
    string Label,
    string? ShortcutText
)
{
    public string Caption => ShortcutText is null ? Label : $"{Label} ({ShortcutText})";
}
