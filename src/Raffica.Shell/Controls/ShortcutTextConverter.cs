using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Raffica.Shell.Localization;

namespace Raffica.Shell.Controls;

/// <summary>
/// Turns a <see cref="KeyGesture"/> or a shortcut written as text (<c>Ctrl+Shift+E</c>) into what the UI language and the platform
/// show (see <see cref="KeyNames"/>). The templates of the menu item and the tooltip use it for their shortcut text.
/// </summary>
public sealed class ShortcutTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        KeyGesture gesture => KeyNames.Format(gesture, ShellStrings.Culture),
        string { Length: > 0 } text => KeyNames.Format(text, ShellStrings.Culture),
        _ => string.Empty,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
