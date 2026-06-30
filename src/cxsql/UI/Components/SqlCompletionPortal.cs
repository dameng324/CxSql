using System.Drawing;
using CxSql.Application.Services;
using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Events;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;
using Color = SharpConsoleUI.Color;

namespace CxSql.UI.Components;

public sealed class SqlCompletionPortal : PortalContentBase
{
    private const int CompletionMaxItems = 80;
    private const int CompletionVisibleItems = 12;
    private const int CompletionMaxWidth = 60;

    private static readonly Color Background = Color.Grey11;
    private static readonly Color Foreground = Color.Grey93;
    private static readonly Color BorderForeground = Color.Grey50;
    private static readonly Color HighlightBackground = Color.SteelBlue;
    private static readonly Color HighlightForeground = Color.White;

    private readonly List<SqlCompletionSuggestion> allItems;
    private List<SqlCompletionSuggestion> filteredItems;
    private readonly ListControl list;
    private Rectangle bounds;
    private string filterText = string.Empty;

    public SqlCompletionPortal(
        IReadOnlyList<SqlCompletionSuggestion> items,
        string initialFilter,
        int cursorX,
        int cursorY,
        int windowWidth,
        int windowHeight
    )
    {
        DismissOnOutsideClick = true;
        BorderStyle = BoxChars.Rounded;
        BorderColor = BorderForeground;
        BorderBackgroundColor = Background;

        allItems = items.Take(CompletionMaxItems).ToList();
        filteredItems = allItems;

        list = new ListControl
        {
            BackgroundColor = Background,
            ForegroundColor = Foreground,
            FocusedBackgroundColor = Background,
            FocusedForegroundColor = Foreground,
            HighlightBackgroundColor = HighlightBackground,
            HighlightForegroundColor = HighlightForeground,
            HoverHighlightsItems = false,
            AutoAdjustWidth = false,
        };

        PortalFocusedControl = list;
        SetFilter(initialFilter);

        var maxLabel =
            allItems.Count == 0
                ? 20
                : allItems
                    .Take(CompletionMaxItems)
                    .Max(item =>
                        MarkupParser.StripLength(
                            $"{GetKindIcon(item.Kind)} {item.ReplacementText} {item.Kind}"
                        )
                    );
        var popupWidth = Math.Min(CompletionMaxWidth, Math.Max(24, maxLabel + 6));
        var visibleItems = Math.Min(CompletionVisibleItems, Math.Max(1, filteredItems.Count));
        var popupHeight = visibleItems + 2;

        var position = PortalPositioner.CalculateFromPoint(
            new Point(cursorX, cursorY),
            new Size(popupWidth, popupHeight),
            new Rectangle(1, 1, windowWidth - 2, windowHeight - 2),
            PortalPlacement.BelowOrAbove,
            new Size(4, 3)
        );
        bounds = position.Bounds;
    }

    public string FilterText => filterText;

    public bool HasVisibleItems => filteredItems.Count > 0;

    public event EventHandler<SqlCompletionSuggestion>? ItemAccepted;

    public SqlCompletionSuggestion? GetSelected()
    {
        return list.SelectedItem?.Tag as SqlCompletionSuggestion;
    }

    public void SelectNext()
    {
        if (list.SelectedIndex < list.Items.Count - 1)
        {
            list.SelectedIndex++;
        }

        Invalidate(Invalidation.Repaint);
    }

    public void SelectPrevious()
    {
        if (list.SelectedIndex > 0)
        {
            list.SelectedIndex--;
        }

        Invalidate(Invalidation.Repaint);
    }

    public void SetFilter(string prefix)
    {
        filterText = prefix;
        filteredItems = string.IsNullOrEmpty(prefix)
            ? allItems
            : allItems
                .Where(item =>
                    item.ReplacementText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    || item.Text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
        list.Items = filteredItems.Select(BuildListItem).ToList();
        if (filteredItems.Count > 0)
        {
            list.SelectedIndex = 0;
        }

        Invalidate(Invalidation.Repaint);
    }

    public override Rectangle GetPortalBounds()
    {
        return bounds;
    }

    public override bool ProcessMouseEvent(MouseEventArgs args)
    {
        if (args.HasFlag(MouseFlags.WheeledUp))
        {
            SelectPrevious();
            return true;
        }

        if (args.HasFlag(MouseFlags.WheeledDown))
        {
            SelectNext();
            return true;
        }

        if (args.HasFlag(MouseFlags.Button1Clicked))
        {
            ((IMouseAwareControl)list).ProcessMouseEvent(args);
            var selected = GetSelected();
            if (selected is not null)
            {
                ItemAccepted?.Invoke(this, selected);
            }
            else
            {
                Invalidate(Invalidation.Repaint);
            }

            return true;
        }

        return false;
    }

    protected override void PaintPortalContent(
        CharacterBuffer buffer,
        LayoutRect bounds,
        LayoutRect clipRect,
        Color defaultForeground,
        Color defaultBackground
    )
    {
        ((IDOMPaintable)list).PaintDOM(buffer, bounds, clipRect, Foreground, Background);
    }

    private static ListItem BuildListItem(SqlCompletionSuggestion item)
    {
        var text =
            $"{GetKindIcon(item.Kind)} {MarkupParser.Escape(item.ReplacementText)}  [dim]{MarkupParser.Escape(item.Kind)}[/]";
        return new ListItem(text) { Tag = item };
    }

    private static string GetKindIcon(string kind)
    {
        return kind switch
        {
            "Keyword" => "kw ",
            "Column" => "col",
            "Result Column" => "col",
            "Table" => "tbl",
            "View" => "vw ",
            "Procedure" => "prc",
            "Function" => "fn ",
            _ => "sql",
        };
    }
}

public sealed class SqlCompletionController
{
    private readonly Window mainWindow;
    private readonly Action<SqlCompletionSuggestion, int> acceptCompletion;

    private SqlCompletionPortal? portal;
    private LayoutNode? portalNode;
    private MultilineEditControl? editor;

    public SqlCompletionController(
        Window mainWindow,
        Action<SqlCompletionSuggestion, int> acceptCompletion
    )
    {
        this.mainWindow = mainWindow;
        this.acceptCompletion = acceptCompletion;
    }

    public bool IsOpen => portal is not null;

    public void ShowOrUpdate(
        MultilineEditControl targetEditor,
        IReadOnlyList<SqlCompletionSuggestion> suggestions,
        string prefix
    )
    {
        if (suggestions.Count == 0)
        {
            Dismiss();
            return;
        }

        if (portal is not null && ReferenceEquals(editor, targetEditor))
        {
            if (string.Equals(portal.FilterText, prefix, StringComparison.Ordinal))
            {
                return;
            }

            portal.SetFilter(prefix);
            if (!portal.HasVisibleItems)
            {
                Dismiss();
            }
            else
            {
                mainWindow.Invalidate(Invalidation.Repaint, targetEditor);
            }

            return;
        }

        Dismiss();
        editor = targetEditor;
        var x = Math.Max(
            0,
            targetEditor.ActualX
                + targetEditor.GutterWidth
                + targetEditor.CurrentColumn
                - 1
                - targetEditor.HorizontalScrollOffset
        );
        var y =
            targetEditor.ActualY
            + Math.Max(0, targetEditor.CurrentLine - 1 - targetEditor.VerticalScrollOffset);

        portal = new SqlCompletionPortal(
            suggestions,
            prefix,
            x,
            y,
            mainWindow.Width,
            mainWindow.Height
        )
        {
            Container = mainWindow,
        };
        portalNode = mainWindow.CreatePortal(targetEditor, portal);
        portal.ItemAccepted += (_, item) => Accept(item);
        portal.DismissRequested += (_, _) => Dismiss();
    }

    public bool ProcessKey(KeyPressedEventArgs args)
    {
        if (portal is null)
        {
            return false;
        }

        var key = args.KeyInfo.Key;
        var modifiers = args.KeyInfo.Modifiers;
        if (modifiers == 0)
        {
            if (key == ConsoleKey.UpArrow)
            {
                portal.SelectPrevious();
                mainWindow.Invalidate(Invalidation.Repaint, editor!);
                args.Handled = true;
                return true;
            }

            if (key == ConsoleKey.DownArrow)
            {
                portal.SelectNext();
                mainWindow.Invalidate(Invalidation.Repaint, editor!);
                args.Handled = true;
                return true;
            }

            if (key is ConsoleKey.Enter or ConsoleKey.Tab)
            {
                var selected = portal.GetSelected();
                var filterLength = portal.FilterText.Length;
                Dismiss();
                if (selected is not null)
                {
                    acceptCompletion(selected, filterLength);
                }

                args.Handled = true;
                return true;
            }

            if (key == ConsoleKey.Escape)
            {
                Dismiss();
                args.Handled = true;
                return true;
            }

            var character = args.KeyInfo.KeyChar;
            var isTypingKey =
                (character != '\0' && !char.IsControl(character)) || key == ConsoleKey.Backspace;
            if (isTypingKey)
            {
                return false;
            }
        }

        Dismiss();
        return false;
    }

    public void Dismiss()
    {
        if (portalNode is not null && editor is not null)
        {
            mainWindow.RemovePortal(editor, portalNode);
        }

        portal = null;
        portalNode = null;
        editor = null;
    }

    private void Accept(SqlCompletionSuggestion suggestion)
    {
        var filterLength = portal?.FilterText.Length ?? 0;
        Dismiss();
        acceptCompletion(suggestion, filterLength);
    }
}
