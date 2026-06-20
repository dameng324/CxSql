using System.Drawing;
using SharpConsoleUI;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Drawing;
using SharpConsoleUI.Drivers;
using SharpConsoleUI.Events;
using SharpConsoleUI.Layout;
using Color = SharpConsoleUI.Color;

namespace CxSql.UI.Components;

public sealed record SqlContextMenuItem(
    string Label,
    string? Shortcut = null,
    Func<Task>? ExecuteAsync = null,
    bool Enabled = true
)
{
    public bool IsSeparator => Label == "-";

    public static SqlContextMenuItem Separator()
    {
        return new SqlContextMenuItem("-");
    }

    public static SqlContextMenuItem Create(
        string label,
        Action action,
        string? shortcut = null,
        bool enabled = true
    )
    {
        return new SqlContextMenuItem(
            label,
            shortcut,
            () =>
            {
                action();
                return Task.CompletedTask;
            },
            enabled
        );
    }
}

public sealed class SqlContextMenuPortal : PortalContentContainer
{
    private const int MenuMaxWidth = 56;
    private const int MenuMinWidth = 18;

    private static readonly Color MenuBackground = Color.Grey11;
    private static readonly Color MenuForeground = Color.Grey93;
    private static readonly Color HighlightBackground = Color.SteelBlue;
    private static readonly Color HighlightForeground = Color.White;

    private readonly MenuControl menu;
    private readonly Dictionary<MenuItem, SqlContextMenuItem> menuItemMap = new();

    public SqlContextMenuPortal(
        IReadOnlyList<SqlContextMenuItem> items,
        int anchorX,
        int anchorY,
        int windowWidth,
        int windowHeight
    )
    {
        menu = new MenuControl
        {
            Orientation = MenuOrientation.Vertical,
            DropdownBackgroundColor = MenuBackground,
            DropdownForegroundColor = MenuForeground,
            DropdownHighlightBackgroundColor = HighlightBackground,
            DropdownHighlightForegroundColor = HighlightForeground,
            MenuBarBackgroundColor = MenuBackground,
            MenuBarForegroundColor = MenuForeground,
            MenuBarHighlightBackgroundColor = HighlightBackground,
            MenuBarHighlightForegroundColor = HighlightForeground,
        };

        BackgroundColor = MenuBackground;
        ForegroundColor = MenuForeground;
        DismissOnOutsideClick = true;
        BorderStyle = BoxChars.Rounded;
        BorderColor = Color.Grey50;
        BorderBackgroundColor = MenuBackground;

        foreach (var item in items)
        {
            var menuItem = new MenuItem
            {
                Text = item.Label,
                Shortcut = item.Shortcut,
                IsSeparator = item.IsSeparator,
                IsEnabled = item.Enabled && !item.IsSeparator,
            };
            menu.AddItem(menuItem);
            if (!item.IsSeparator)
            {
                menuItemMap[menuItem] = item;
            }
        }

        PortalFocusedControl = menu;
        menu.ItemSelected += (_, menuItem) =>
        {
            if (menuItemMap.TryGetValue(menuItem, out var contextItem))
            {
                ItemSelected?.Invoke(this, contextItem);
            }
        };

        AddChild(menu);
        SetFocusOnFirstChild();

        var maxLabelWidth = 0;
        var maxShortcutWidth = 0;
        foreach (var item in items)
        {
            if (item.IsSeparator)
            {
                continue;
            }

            maxLabelWidth = Math.Max(maxLabelWidth, item.Label.Length);
            if (item.Shortcut is not null)
            {
                maxShortcutWidth = Math.Max(maxShortcutWidth, item.Shortcut.Length);
            }
        }

        var contentWidth = maxLabelWidth + (maxShortcutWidth > 0 ? maxShortcutWidth + 2 : 0) + 4;
        var popupWidth = Math.Clamp(contentWidth + 2, MenuMinWidth, MenuMaxWidth);
        var popupHeight = items.Count + 2;
        var position = PortalPositioner.CalculateFromPoint(
            new Point(anchorX, anchorY),
            new Size(popupWidth, popupHeight),
            new Rectangle(1, 1, windowWidth - 2, windowHeight - 2),
            PortalPlacement.BelowOrAbove,
            new Size(16, 3)
        );
        PortalBounds = position.Bounds;
    }

    public event EventHandler<SqlContextMenuItem>? ItemSelected;

    public event EventHandler? Dismissed;

    public override bool ProcessMouseEvent(MouseEventArgs args)
    {
        if (args.HasAnyFlag(MouseFlags.ReportMousePosition))
        {
            if (menu is IMouseAwareControl mouseAwareControl && mouseAwareControl.WantsMouseEvents)
            {
                mouseAwareControl.ProcessMouseEvent(args);
            }

            return true;
        }

        return base.ProcessMouseEvent(args);
    }

    public bool ProcessPortalKey(ConsoleKeyInfo key)
    {
        if (key.Key == ConsoleKey.Escape)
        {
            Dismissed?.Invoke(this, EventArgs.Empty);
            return true;
        }

        if (base.ProcessKey(key))
        {
            return true;
        }

        return true;
    }
}

public sealed class SqlContextMenuController
{
    private readonly Window mainWindow;
    private SqlContextMenuPortal? portal;
    private LayoutNode? portalNode;
    private IWindowControl? portalOwner;

    public SqlContextMenuController(Window mainWindow)
    {
        this.mainWindow = mainWindow;
    }

    public bool IsOpen => portal is not null;

    public void Show(
        IEnumerable<SqlContextMenuItem> items,
        int anchorX,
        int anchorY,
        IWindowControl owner
    )
    {
        Dismiss();

        var itemList = items.ToList();
        if (itemList.Count == 0)
        {
            return;
        }

        portal = new SqlContextMenuPortal(
            itemList,
            anchorX,
            anchorY,
            mainWindow.Width,
            mainWindow.Height
        )
        {
            Container = mainWindow,
        };
        portalOwner = owner;
        portalNode = mainWindow.CreatePortal(owner, portal);

        portal.ItemSelected += (_, item) =>
        {
            Dismiss();
            if (item.ExecuteAsync is not null)
            {
                _ = item.ExecuteAsync();
            }
        };
        portal.Dismissed += (_, _) => Dismiss();
        portal.DismissRequested += (_, _) =>
        {
            portal = null;
            portalNode = null;
            portalOwner = null;
        };
    }

    public bool ProcessKey(KeyPressedEventArgs args)
    {
        if (portal is null)
        {
            return false;
        }

        portal.ProcessPortalKey(args.KeyInfo);
        args.Handled = true;
        return true;
    }

    public void Dismiss()
    {
        if (portalNode is not null && portalOwner is not null)
        {
            mainWindow.RemovePortal(portalOwner, portalNode);
        }

        portal = null;
        portalNode = null;
        portalOwner = null;
    }
}
