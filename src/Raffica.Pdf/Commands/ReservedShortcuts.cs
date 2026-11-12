using Avalonia.Input;

namespace Raffica.Pdf.Commands;

/// <summary>A shortcut that macOS keeps for itself, with what it does there.</summary>
internal sealed record ReservedShortcut(Key Key, KeyModifiers Modifiers, string Meaning);

/// <summary>
/// The shortcuts macOS reserves (the app never gets them, or taking them would surprise the user). The catalog is audited against this list so a
/// command never lands on one; <see cref="ClashesIn"/> is the audit.
/// </summary>
internal static class ReservedShortcuts
{
    private const KeyModifiers Cmd = KeyModifiers.Meta;
    private const KeyModifiers Shift = KeyModifiers.Shift;
    private const KeyModifiers Alt = KeyModifiers.Alt;
    private const KeyModifiers Ctrl = KeyModifiers.Control;

    /// <summary>The reserved shortcuts on macOS.</summary>
    public static IReadOnlyList<ReservedShortcut> MacOs { get; } =
    [
        new(Key.H, Cmd, "Hide the app"),
        new(Key.H, Alt | Cmd, "Hide the other apps"),
        new(Key.M, Cmd, "Minimize the window"),
        new(Key.Q, Cmd, "Quit the app"),
        new(Key.Q, Shift | Cmd, "Log out"),
        new(Key.Tab, Cmd, "Switch app"),
        new(Key.Space, Cmd, "Spotlight"),
        new(Key.OemTilde, Cmd, "Switch window of the app"),
        new(Key.F, Ctrl | Cmd, "Full screen"),
        new(Key.D3, Shift | Cmd, "Screenshot of the screen"),
        new(Key.D4, Shift | Cmd, "Screenshot of a selection"),
        new(Key.D5, Shift | Cmd, "Screenshot and recording options"),
    ];

    /// <summary>The commands whose shortcut on the given system is one of the reserved ones (empty on any system but macOS).</summary>
    public static IReadOnlyList<CatalogCommand> ClashesIn(bool macOs)
    {
        if (!macOs)
        {
            return [];
        }

        return PdfCommandCatalog.Commands
            .Where(c => c.ShortcutFor(true) is { } shortcut
                && MacOs.Any(r => shortcut.Matches(r.Key, r.Modifiers, Cmd)))
            .ToList();
    }
}
