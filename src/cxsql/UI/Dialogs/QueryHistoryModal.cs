using CxSql.Models;
using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;

namespace CxSql.UI.Dialogs;

public sealed class QueryHistoryModal : ModalBase<QueryHistory?>
{
    private readonly IReadOnlyList<QueryHistory> entries;
    private ListControl? list;

    private QueryHistoryModal(
        ConsoleWindowSystem windowSystem,
        Window? parentWindow,
        IReadOnlyList<QueryHistory> entries
    )
        : base(windowSystem, parentWindow)
    {
        this.entries = entries;
    }

    public static Task<QueryHistory?> ShowAsync(
        ConsoleWindowSystem windowSystem,
        Window? parentWindow,
        IReadOnlyList<QueryHistory> entries
    )
    {
        return new QueryHistoryModal(windowSystem, parentWindow, entries).ShowAsync();
    }

    protected override string GetTitle()
    {
        return "Query History";
    }

    protected override int GetWidth()
    {
        return 92;
    }

    protected override int GetHeight()
    {
        return 22;
    }

    protected override QueryHistory? GetDefaultResult()
    {
        return null;
    }

    protected override void BuildContent()
    {
        var builder = Controls
            .List("History")
            .WithMargin(1, 1, 1, 0)
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithScrollbarVisibility(ScrollbarVisibility.Auto)
            .WithDoubleClickActivation(true, 350)
            .WithHighlightColors(Color.White, new Color(40, 60, 100));

        foreach (var entry in entries)
        {
            builder.AddItem(FormatEntry(entry), entry);
        }

        list = builder
            .OnItemActivated(
                (_, item) =>
                {
                    if (item.Tag is QueryHistory entry)
                    {
                        CloseWithResult(entry);
                    }
                }
            )
            .Build();

        Modal!.AddControl(list);
        Modal.AddControl(
            Controls
                .Markup(
                    "[grey70]Double-click a history row, or press Enter. Esc closes this dialog.[/]"
                )
                .StickyBottom()
                .WithMargin(1, 0, 1, 0)
                .Build()
        );

        list.RequestFocus();
    }

    private static string FormatEntry(QueryHistory entry)
    {
        var status = entry.Succeeded ? "[green]OK[/]" : "[red]ERR[/]";
        var time = DateTimeOffset
            .FromUnixTimeMilliseconds(entry.TimestampUnixMs)
            .ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm:ss");
        var sql = entry.SqlText.ReplaceLineEndings(" ").Trim();
        if (sql.Length > 80)
        {
            sql = string.Concat(sql.AsSpan(0, 77), "...");
        }

        return $"{status} [grey50]{time}[/] [cyan]{MarkupParser.Escape(entry.ConnectionName)}[/] {MarkupParser.Escape(sql)}";
    }
}
