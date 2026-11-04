using Avalonia.Input;

namespace Raffica.Pdf.Commands;

/// <summary>Which modifier a shortcut starts with: the command key of the platform (Ctrl, Cmd on macOS), the Control key itself, or none.</summary>
internal enum ShortcutBase
{
    None,

    /// <summary>The modifier the platform uses for everyday commands (<c>HotkeyConfiguration.CommandModifiers</c>): Ctrl, Cmd on macOS.</summary>
    Command,

    /// <summary>The Control key on every platform (Ctrl+Tab keeps its meaning on macOS).</summary>
    Control,
}

/// <summary>
/// A shortcut of the catalog: its main key (and the keys that mean the same, such as the number pad), the modifiers and the text the user
/// reads. Keys are matched as keys, not as text, so a German keyboard (Ctrl and the plus key, Ctrl+Shift+1 although Shift+1 types "!")
/// works like an English one.
/// </summary>
/// <param name="Key">The main key.</param>
/// <param name="Token">How the key is written in the shortcut text (<c>O</c>, <c>Tab</c>, <c>F3</c>, <c>+</c>).</param>
/// <param name="Base">The modifier the shortcut starts with.</param>
/// <param name="Shift">Whether Shift is part of the shortcut.</param>
/// <param name="AnyShift">Whether Shift is accepted but not required (Ctrl and the plus key is Ctrl+Shift+= on an English keyboard).</param>
/// <param name="Alternates">Keys that do the same as <paramref name="Key"/> (number pad keys).</param>
/// <param name="AllowsRepeat">Whether holding the key repeats the command (zoom, next hit); every other shortcut runs once per press.</param>
/// <param name="Also">A second shortcut that does the same (Ctrl+Y beside Ctrl+Shift+Z); it has its own modifiers and shows in no text.</param>
internal sealed record PdfShortcut(Key Key, string Token, ShortcutBase Base, bool Shift = false, bool AnyShift = false, Key[]? Alternates = null, bool AllowsRepeat = false, PdfShortcut? Also = null)
{
    private const KeyModifiers Relevant = KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta;

    /// <summary>The main key and its alternates.</summary>
    public IEnumerable<Key> Keys => Alternates is null ? [Key] : [Key, .. Alternates];

    /// <summary>The shortcut written with English names, the way the shell reads them (<c>Ctrl+Shift+D</c>, <c>Meta+O</c> on macOS, <c>Ctrl++</c>).</summary>
    public string Text(bool macOs)
    {
        var parts = new List<string>(3);
        switch (Base)
        {
            case ShortcutBase.Command:
                parts.Add(macOs ? "Meta" : "Ctrl");
                break;
            case ShortcutBase.Control:
                parts.Add("Ctrl");
                break;
        }

        if (Shift)
        {
            parts.Add("Shift");
        }

        parts.Add(Token);
        return string.Join('+', parts);
    }

    /// <summary>Whether a key press is this shortcut. <paramref name="command"/> is the command modifier of the platform. Alt, or any modifier the shortcut does not name, makes it no match.</summary>
    public bool Matches(Key key, KeyModifiers modifiers, KeyModifiers command) =>
        MatchesOwn(key, modifiers, command) || Also?.Matches(key, modifiers, command) == true;

    private bool MatchesOwn(Key key, KeyModifiers modifiers, KeyModifiers command)
    {
        if (!Keys.Contains(key))
        {
            return false;
        }

        var wanted = Base switch
        {
            ShortcutBase.Command => command,
            ShortcutBase.Control => KeyModifiers.Control,
            _ => KeyModifiers.None,
        };
        if (Shift)
        {
            wanted |= KeyModifiers.Shift;
        }

        var actual = modifiers & Relevant;
        if (AnyShift)
        {
            actual &= ~KeyModifiers.Shift;
        }

        return actual == wanted;
    }
}
