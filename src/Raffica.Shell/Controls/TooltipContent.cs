using Avalonia;
using Avalonia.Controls;

namespace Raffica.Shell.Controls;

/// <summary>
/// The rich content of a tooltip (section 6 of the design spec): the command name once (13 px, 600), its shortcut as plain
/// secondary text beside it, and a one-line description (12 px) below. Set it as the content of a <see cref="ToolTip"/>
/// (<c>ToolTip.Tip</c>); the tooltip itself brings the page fill, the 1 px control edge and the 3 px radius. At most 240 px wide.
/// </summary>
public class TooltipContent : ContentControl
{
    /// <summary>The name of the command, shown once.</summary>
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<TooltipContent, string?>(nameof(Title));

    /// <summary>The shortcut written with English names, for example <c>Ctrl+Shift+E</c>; it is shown in the UI language and the platform's style (<see cref="Raffica.Shell.Localization.KeyNames"/>). Empty hides it.</summary>
    public static readonly StyledProperty<string?> ShortcutProperty =
        AvaloniaProperty.Register<TooltipContent, string?>(nameof(Shortcut));

    /// <summary>A one-line description. Empty hides it.</summary>
    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<TooltipContent, string?>(nameof(Description));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string? Shortcut
    {
        get => GetValue(ShortcutProperty);
        set => SetValue(ShortcutProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
