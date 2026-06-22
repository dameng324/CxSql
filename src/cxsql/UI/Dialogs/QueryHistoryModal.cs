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
    private readonly List<QueryHistory> filteredEntries = [];
    private ListControl? list;
    private MarkupControl? preview;

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
        return 76;
    }

    protected override int GetHeight()
    {
        return 20;
    }

    protected override QueryHistory? GetDefaultResult()
    {
        return null;
    }

    protected override void BuildContent()
    {
        var search = Controls
            .Prompt("Search: ")
            .WithInputWidth(58)
            .WithMargin(1, 1, 1, 0)
            .UnfocusOnEnter(false)
            .OnInputChanged((_, value) => ApplyFilter(value))
            .OnEntered(
                (_, _) =>
                {
                    if (filteredEntries.Count == 1)
                    {
                        CloseWithResult(filteredEntries[0]);
                    }
                    else
                    {
                        list?.RequestFocus();
                    }
                }
            )
            .Build();

        list = Controls
            .List("History")
            .WithMargin(1, 0, 1, 0)
            .WithAlignment(HorizontalAlignment.Stretch)
            .WithVerticalAlignment(VerticalAlignment.Fill)
            .WithScrollbarVisibility(ScrollbarVisibility.Auto)
            .WithDoubleClickActivation(true, 350)
            .WithHighlightColors(Color.White, new Color(40, 60, 100))
            .OnSelectedItemChanged(
                (_, item) =>
                {
                    UpdatePreview(item?.Tag as QueryHistory);
                }
            )
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

        preview = Controls
            .Markup("[grey50]Select a history row to preview the SQL.[/]")
            .WithMargin(1, 0, 1, 0)
            .Build();

        Modal!.AddControl(search);
        Modal.AddControl(list);
        Modal.AddControl(preview);
        Modal.AddControl(
            Controls
                .Markup(
                    "[grey70]Type to filter. Double-click a history row, or press Enter. Esc closes this dialog.[/]"
                )
                .StickyBottom()
                .WithMargin(1, 0, 1, 0)
                .Build()
        );

        ApplyFilter(string.Empty);
        search.RequestFocus();
    }

    private static string FormatEntry(QueryHistory entry)
    {
        var status = entry.Succeeded ? "[green]OK[/]" : "[red]ERR[/]";
        var time = DateTimeOffset
            .FromUnixTimeMilliseconds(entry.TimestampUnixMs)
            .ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm:ss");
        var sql = entry.SqlText.ReplaceLineEndings(" ").Trim();
        if (sql.Length > 58)
        {
            sql = string.Concat(sql.AsSpan(0, 55), "...");
        }

        return $"{status} [grey50]{time}[/] [cyan]{MarkupParser.Escape(entry.ConnectionName)}[/] {MarkupParser.Escape(sql)}";
    }

    private void ApplyFilter(string filterText)
    {
        filteredEntries.Clear();
        filteredEntries.AddRange(entries.Where(entry => Matches(entry, filterText)));
        RebuildList();
    }

    private void RebuildList()
    {
        if (list is null)
        {
            return;
        }

        list.ClearItems();
        foreach (var entry in filteredEntries)
        {
            list.AddItem(new ListItem(FormatEntry(entry)) { Tag = entry });
        }

        if (filteredEntries.Count == 0)
        {
            UpdatePreview(null);
            return;
        }

        list.SelectedIndex = 0;
        UpdatePreview(filteredEntries[0]);
    }

    private void UpdatePreview(QueryHistory? entry)
    {
        if (preview is null)
        {
            return;
        }

        if (entry is null)
        {
            preview.SetContent(["[grey50]No matching history entries.[/]"]);
            return;
        }

        var time = DateTimeOffset
            .FromUnixTimeMilliseconds(entry.TimestampUnixMs)
            .ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm:ss");
        var status = entry.Succeeded ? "[green]OK[/]" : "[red]ERR[/]";
        var lines = new List<string>
        {
            $"{status} [grey50]{time}[/] [cyan]{MarkupParser.Escape(entry.ConnectionName)}[/] [grey50]{entry.ExecutionElapsedMilliseconds} ms[/]",
        };

        lines.AddRange(
            entry
                .SqlText.ReplaceLineEndings("\n")
                .Split('\n')
                .Select(line => line.TrimEnd())
                .Where(line => line.Length > 0)
                .Take(4)
                .Select(line => $"[grey70]{MarkupParser.Escape(TruncatePreviewLine(line))}[/]")
        );

        if (!entry.Succeeded && !string.IsNullOrWhiteSpace(entry.ErrorMessage))
        {
            lines.Add($"[red]{MarkupParser.Escape(TruncatePreviewLine(entry.ErrorMessage))}[/]");
        }

        preview.SetContent(lines);
    }

    private static bool Matches(QueryHistory entry, string filterText)
    {
        if (string.IsNullOrWhiteSpace(filterText))
        {
            return true;
        }

        return entry.SqlText.Contains(filterText, StringComparison.OrdinalIgnoreCase)
            || entry.ConnectionName.Contains(filterText, StringComparison.OrdinalIgnoreCase)
            || (
                entry.ErrorMessage?.Contains(filterText, StringComparison.OrdinalIgnoreCase) == true
            );
    }

    private static string TruncatePreviewLine(string value)
    {
        return value.Length <= 70 ? value : string.Concat(value.AsSpan(0, 67), "...");
    }
}
