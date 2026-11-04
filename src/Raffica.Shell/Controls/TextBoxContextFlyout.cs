using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Raffica.Shell.Localization;

namespace Raffica.Shell.Controls;

/// <summary>
/// The context menu of the shell's text box: Cut, Copy, Paste, Select all, Undo and Redo, with texts in the UI language and each
/// shortcut written by <see cref="KeyNames"/>. It is built each time it opens, for the text box it opens on, so the items match the
/// language and are disabled when there is nothing to do (Cut and Copy without a selection, Cut and Paste in a read-only box,
/// Undo and Redo without history). The menu theme styles it like every other menu.
/// </summary>
public sealed class TextBoxContextFlyout : MenuFlyout
{
    protected override bool ShowAtCore(Control placementTarget, bool showAtPointer = false)
    {
        Rebuild(placementTarget as TextBox);
        return base.ShowAtCore(placementTarget, showAtPointer);
    }

    private void Rebuild(TextBox? box)
    {
        Items.Clear();
        var command = KeyNames.CommandModifier;
        var redo = OperatingSystem.IsMacOS() ? new KeyGesture(Key.Z, command | KeyModifiers.Shift) : new KeyGesture(Key.Y, command);

        Items.Add(Item(ShellStrings.ContextCut, new KeyGesture(Key.X, command), box is { CanCut: true }, box is null ? null : box.Cut));
        Items.Add(Item(ShellStrings.ContextCopy, new KeyGesture(Key.C, command), box is { CanCopy: true }, box is null ? null : box.Copy));
        Items.Add(Item(ShellStrings.ContextPaste, new KeyGesture(Key.V, command), box is { CanPaste: true }, box is null ? null : box.Paste));
        Items.Add(Item(ShellStrings.ContextSelectAll, new KeyGesture(Key.A, command), box is not null, box is null ? null : box.SelectAll));
        Items.Add(new Separator());
        Items.Add(Item(ShellStrings.ContextUndo, new KeyGesture(Key.Z, command), box is { CanUndo: true }, box is null ? null : box.Undo));
        Items.Add(Item(ShellStrings.ContextRedo, redo, box is { CanRedo: true }, box is null ? null : box.Redo));
    }

    private static MenuItem Item(string header, KeyGesture gesture, bool enabled, Action? run)
    {
        var item = new MenuItem { Header = header, InputGesture = gesture, IsEnabled = enabled };
        if (run is not null)
        {
            item.Click += (_, _) => run();
        }

        return item;
    }
}
