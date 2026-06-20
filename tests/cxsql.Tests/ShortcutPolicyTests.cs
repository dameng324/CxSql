using CxSql.UI.Components;
using TUnit.Core;

namespace CxSql.Tests;

public sealed class ShortcutPolicyTests
{
    [Test]
    public void OnlyExplicitMinimalShortcutsAreMapped()
    {
        var shortcutTexts = ShortcutPolicy
            .AllowedShortcuts.Select(shortcut => shortcut.DisplayText)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var expected = new[] { "Ctrl+N", "Ctrl+S", "Esc", "F5" };
        if (!shortcutTexts.SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                $"Unexpected shortcuts: {string.Join(", ", shortcutTexts)}"
            );
        }

        var hiddenQ = new ConsoleKeyInfo(
            'q',
            ConsoleKey.Q,
            shift: false,
            alt: false,
            control: false
        );
        if (ShortcutPolicy.TryGetToolbarAction(hiddenQ, out _))
        {
            throw new InvalidOperationException("Q must not be an implicit shortcut.");
        }
    }

    [Test]
    public void ToolbarButtonsShowMappedShortcutText()
    {
        var buttonShortcuts = ToolbarCatalog
            .Buttons.Where(button => button.ShortcutText is not null)
            .ToDictionary(button => button.Action, button => button.ShortcutText);

        foreach (
            var shortcut in ShortcutPolicy.AllowedShortcuts.Where(item => item.Action is not null)
        )
        {
            if (!buttonShortcuts.TryGetValue(shortcut.Action!.Value, out var buttonText))
            {
                throw new InvalidOperationException(
                    $"Shortcut {shortcut.DisplayText} is not shown next to a toolbar button."
                );
            }

            if (buttonText != shortcut.DisplayText)
            {
                throw new InvalidOperationException(
                    $"Toolbar shortcut text mismatch for {shortcut.Action}."
                );
            }
        }
    }
}
