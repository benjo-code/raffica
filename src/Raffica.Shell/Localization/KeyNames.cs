using System.Globalization;
using System.Text;
using Avalonia.Input;

namespace Raffica.Shell.Localization;

/// <summary>
/// Writes shortcuts the way the UI language and the platform read them: <c>Ctrl+K</c> in English, <c>Strg+K</c> in German
/// (<c>Umschalt</c>, <c>Entf</c>, <c>Eingabe</c>), and on macOS symbols without a separator in both languages
/// (<c>⌃⌥⇧⌘K</c>: Ctrl, Option, Shift, Command). A name that is not known passes through unchanged. The names come from
/// the string resources (<c>KeyCtrl</c>, <c>KeyShift</c>, <c>KeyAlt</c>, <c>KeyCmd</c>, <c>KeyOption</c>, <c>KeyEsc</c>,
/// <c>KeyDelete</c>, <c>KeyEnter</c>); no reflection.
/// </summary>
public static class KeyNames
{
    private const string MacCtrl = "⌃";
    private const string MacShift = "⇧";

    /// <summary>The modifier an app uses for its everyday commands: Command on macOS, Ctrl elsewhere.</summary>
    public static KeyModifiers CommandModifier => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    /// <summary>The gesture as text for the platform of the process.</summary>
    public static string Format(KeyGesture gesture, CultureInfo ui) => Format(gesture, ui, OperatingSystem.IsMacOS());

    /// <summary>A shortcut written with English names (<c>Ctrl+Shift+E</c>) as text for the platform of the process.</summary>
    public static string Format(string shortcut, CultureInfo ui) => Format(shortcut, ui, OperatingSystem.IsMacOS());

    internal static string Format(KeyGesture gesture, CultureInfo ui, bool macOs)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        var parts = new List<string>(5);
        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            parts.Add("Meta");
        }

        if (gesture.Key != Key.None)
        {
            parts.Add(KeyToken(gesture.Key));
        }

        return Join(parts, ui, macOs);
    }

    public static string Format(string shortcut, CultureInfo ui, bool macOs)
    {
        ArgumentNullException.ThrowIfNull(shortcut);
        if (shortcut.Length == 0)
        {
            return string.Empty;
        }

        // "Ctrl++" ends in the plus key itself.
        var plusKey = shortcut.Length > 1 && shortcut.EndsWith('+');
        var parts = (plusKey ? shortcut[..^1] : shortcut).Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        if (plusKey)
        {
            parts.Add("+");
        }

        return Join(parts, ui, macOs);
    }

    private static string Join(List<string> parts, CultureInfo ui, bool macOs)
    {
        var text = new StringBuilder();
        for (var i = 0; i < parts.Count; i++)
        {
            if (i > 0 && !macOs)
            {
                text.Append('+');
            }

            text.Append(Name(parts[i], ui, macOs));
        }

        return text.ToString();
    }

    private static string Name(string token, CultureInfo ui, bool macOs) => token.ToLowerInvariant() switch
    {
        "ctrl" or "control" => macOs ? MacCtrl : ShellStrings.GetIn("KeyCtrl", ui),
        "shift" => macOs ? MacShift : ShellStrings.GetIn("KeyShift", ui),
        "alt" or "option" => ShellStrings.GetIn(macOs ? "KeyOption" : "KeyAlt", ui),
        "meta" or "cmd" or "command" => macOs ? ShellStrings.GetIn("KeyCmd", ui) : token,
        "esc" or "escape" => ShellStrings.GetIn("KeyEsc", ui),
        "delete" or "del" => ShellStrings.GetIn("KeyDelete", ui),
        "enter" or "return" => ShellStrings.GetIn("KeyEnter", ui),
        _ => token,
    };

    private static string KeyToken(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        Key.OemPlus => "+",
        Key.OemMinus => "-",
        _ => key.ToString(),
    };
}
