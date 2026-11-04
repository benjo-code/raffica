using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Raffica.Shell.Frame;

/// <summary>
/// The container of the app frame (section 6 of the design spec): title bar 36, command tab row 32, ribbon band 82 (40 when
/// <see cref="IsCompact"/>), the document row with an optional pane on each side, an optional notification and the status bar 30.
/// It only places the slots; what goes into them is the job of the app and of the later frame controls. A slot that is null
/// collapses (tab row, band, panes, notification and status bar); only the title bar row keeps its height.
/// </summary>
public class AppFrame : TemplatedControl
{
    public static readonly StyledProperty<Control?> TitleBarProperty = AvaloniaProperty.Register<AppFrame, Control?>(nameof(TitleBar));
    public static readonly StyledProperty<Control?> TabRowProperty = AvaloniaProperty.Register<AppFrame, Control?>(nameof(TabRow));
    public static readonly StyledProperty<Control?> BandProperty = AvaloniaProperty.Register<AppFrame, Control?>(nameof(Band));
    public static readonly StyledProperty<Control?> LeftPaneProperty = AvaloniaProperty.Register<AppFrame, Control?>(nameof(LeftPane));
    public static readonly StyledProperty<Control?> RightPaneProperty = AvaloniaProperty.Register<AppFrame, Control?>(nameof(RightPane));
    public static readonly StyledProperty<Control?> DocumentProperty = AvaloniaProperty.Register<AppFrame, Control?>(nameof(Document));
    public static readonly StyledProperty<Control?> NotificationProperty = AvaloniaProperty.Register<AppFrame, Control?>(nameof(Notification));
    public static readonly StyledProperty<Control?> StatusBarProperty = AvaloniaProperty.Register<AppFrame, Control?>(nameof(StatusBar));

    /// <summary>Compact ribbon: the band row is 40 px instead of 82.</summary>
    public static readonly StyledProperty<bool> IsCompactProperty = AvaloniaProperty.Register<AppFrame, bool>(nameof(IsCompact));

    static AppFrame()
    {
        IsCompactProperty.Changed.AddClassHandler<AppFrame>((frame, e) => frame.PseudoClasses.Set(":compact", e.GetNewValue<bool>()));
    }

    public Control? TitleBar { get => GetValue(TitleBarProperty); set => SetValue(TitleBarProperty, value); }

    public Control? TabRow { get => GetValue(TabRowProperty); set => SetValue(TabRowProperty, value); }

    public Control? Band { get => GetValue(BandProperty); set => SetValue(BandProperty, value); }

    public Control? LeftPane { get => GetValue(LeftPaneProperty); set => SetValue(LeftPaneProperty, value); }

    public Control? RightPane { get => GetValue(RightPaneProperty); set => SetValue(RightPaneProperty, value); }

    public Control? Document { get => GetValue(DocumentProperty); set => SetValue(DocumentProperty, value); }

    public Control? Notification { get => GetValue(NotificationProperty); set => SetValue(NotificationProperty, value); }

    public Control? StatusBar { get => GetValue(StatusBarProperty); set => SetValue(StatusBarProperty, value); }

    public bool IsCompact { get => GetValue(IsCompactProperty); set => SetValue(IsCompactProperty, value); }
}
