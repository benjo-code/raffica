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

    /// <summary>
    /// Set by the <see cref="RibbonView"/> whose band sits in the frame while its band is compact because the window is too narrow for
    /// the full band (the manual <see cref="IsCompact"/> is the user's setting and stays as it is). Either one makes the band row 40 px.
    /// </summary>
    public static readonly StyledProperty<bool> IsAutoCompactProperty = AvaloniaProperty.Register<AppFrame, bool>(nameof(IsAutoCompact));

    /// <summary>Whether the left pane slot shows: a pane is set and it is visible. A closed pane takes its slot, and the rule of the slot, with it.</summary>
    public static readonly DirectProperty<AppFrame, bool> IsLeftPaneShownProperty =
        AvaloniaProperty.RegisterDirect<AppFrame, bool>(nameof(IsLeftPaneShown), f => f._isLeftPaneShown);

    /// <summary>Whether the right pane slot shows: a pane is set and it is visible.</summary>
    public static readonly DirectProperty<AppFrame, bool> IsRightPaneShownProperty =
        AvaloniaProperty.RegisterDirect<AppFrame, bool>(nameof(IsRightPaneShown), f => f._isRightPaneShown);

    private bool _isLeftPaneShown;
    private bool _isRightPaneShown;

    static AppFrame()
    {
        IsCompactProperty.Changed.AddClassHandler<AppFrame>((frame, _) => frame.UpdateCompactClass());
        IsAutoCompactProperty.Changed.AddClassHandler<AppFrame>((frame, _) => frame.UpdateCompactClass());
        LeftPaneProperty.Changed.AddClassHandler<AppFrame>((frame, e) => Follow(e, frame.OnLeftPaneVisibility, frame.UpdateLeftPane));
        RightPaneProperty.Changed.AddClassHandler<AppFrame>((frame, e) => Follow(e, frame.OnRightPaneVisibility, frame.UpdateRightPane));
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

    public bool IsAutoCompact { get => GetValue(IsAutoCompactProperty); set => SetValue(IsAutoCompactProperty, value); }

    public bool IsLeftPaneShown => _isLeftPaneShown;

    public bool IsRightPaneShown => _isRightPaneShown;

    private void UpdateCompactClass() => PseudoClasses.Set(":compact", IsCompact || IsAutoCompact);

    // The slot follows the visibility of its pane: the handler sits on the pane while it is in the slot.
    private static void Follow(AvaloniaPropertyChangedEventArgs e, EventHandler<AvaloniaPropertyChangedEventArgs> handler, Action update)
    {
        if (e.OldValue is Control old)
        {
            old.PropertyChanged -= handler;
        }

        if (e.NewValue is Control added)
        {
            added.PropertyChanged += handler;
        }

        update();
    }

    private void OnLeftPaneVisibility(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty)
        {
            UpdateLeftPane();
        }
    }

    private void OnRightPaneVisibility(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty)
        {
            UpdateRightPane();
        }
    }

    private void UpdateLeftPane() => SetAndRaise(IsLeftPaneShownProperty, ref _isLeftPaneShown, LeftPane is { IsVisible: true });

    private void UpdateRightPane() => SetAndRaise(IsRightPaneShownProperty, ref _isRightPaneShown, RightPane is { IsVisible: true });
}
