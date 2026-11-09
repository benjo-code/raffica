using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Raffica.Shell.Controls;
using Raffica.Shell.Localization;

namespace Raffica.Shell.Frame;

/// <summary>
/// The cells of the status bar (section 4.4 of the design spec), left to right: save state, position, count, the focus cell (F6
/// pane navigation; the Alt hint takes the same slot and wins when both are set), the language and, on the right, the zoom cluster
/// (minus, slider, plus, value). Cells have a fixed width and change in place. The texts come from the app, the bar only lays
/// them out. <see cref="Zoom"/> is a percentage.
/// </summary>
public class StatusBar : TemplatedControl
{
    private const double ZoomMinimum = 10;
    private const double ZoomMaximum = 800;
    private const double ZoomStep = 10;

    public static readonly StyledProperty<string?> SaveStateProperty = AvaloniaProperty.Register<StatusBar, string?>(nameof(SaveState));
    public static readonly StyledProperty<string?> PositionProperty = AvaloniaProperty.Register<StatusBar, string?>(nameof(Position));
    public static readonly StyledProperty<string?> CountProperty = AvaloniaProperty.Register<StatusBar, string?>(nameof(Count));

    /// <summary>The focus cell, for example "Focus: Comments"; set only while pane navigation is active.</summary>
    public static readonly StyledProperty<string?> FocusPaneProperty = AvaloniaProperty.Register<StatusBar, string?>(nameof(FocusPane));

    /// <summary>The Alt field-code hint; shown in the slot of the focus cell, only while Alt mode is on.</summary>
    public static readonly StyledProperty<string?> HintProperty = AvaloniaProperty.Register<StatusBar, string?>(nameof(Hint));

    /// <summary>The language of the selection; set only when it differs from the language of the document.</summary>
    public static readonly StyledProperty<string?> LanguageProperty = AvaloniaProperty.Register<StatusBar, string?>(nameof(Language));

    /// <summary>The zoom value as text, for example "100%".</summary>
    public static readonly StyledProperty<string?> ZoomTextProperty = AvaloniaProperty.Register<StatusBar, string?>(nameof(ZoomText));

    /// <summary>The zoom in percent (10 to 800, the range of the viewer).</summary>
    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<StatusBar, double>(nameof(Zoom), 100, coerce: (_, v) => Math.Clamp(v, ZoomMinimum, ZoomMaximum));

    /// <summary>
    /// Whether the focus text is on show: <see cref="FocusPane"/> is set and <see cref="Hint"/> is not. Both share one cell, and
    /// when F6 navigation and the field-code hint coexist briefly the hint wins (it is the more transient and more urgent one).
    /// </summary>
    public static readonly DirectProperty<StatusBar, bool> IsFocusTextShownProperty =
        AvaloniaProperty.RegisterDirect<StatusBar, bool>(nameof(IsFocusTextShown), b => b._isFocusTextShown);

    private Slider? _slider;
    private bool _syncing;
    private bool _isFocusTextShown;

    static StatusBar()
    {
        ZoomProperty.Changed.AddClassHandler<StatusBar>((bar, _) => bar.SyncSlider());
        FocusPaneProperty.Changed.AddClassHandler<StatusBar>((bar, _) => bar.UpdateFocusText());
        HintProperty.Changed.AddClassHandler<StatusBar>((bar, _) => bar.UpdateFocusText());
    }

    /// <summary>Raised when the user changes the zoom with the slider or the buttons (not when the app sets <see cref="Zoom"/>).</summary>
    public event EventHandler<double>? ZoomChanged;

    public string? SaveState { get => GetValue(SaveStateProperty); set => SetValue(SaveStateProperty, value); }

    public string? Position { get => GetValue(PositionProperty); set => SetValue(PositionProperty, value); }

    public string? Count { get => GetValue(CountProperty); set => SetValue(CountProperty, value); }

    public string? FocusPane { get => GetValue(FocusPaneProperty); set => SetValue(FocusPaneProperty, value); }

    public string? Hint { get => GetValue(HintProperty); set => SetValue(HintProperty, value); }

    public bool IsFocusTextShown => _isFocusTextShown;

    public string? Language { get => GetValue(LanguageProperty); set => SetValue(LanguageProperty, value); }

    public string? ZoomText { get => GetValue(ZoomTextProperty); set => SetValue(ZoomTextProperty, value); }

    public double Zoom { get => GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_slider is not null)
        {
            _slider.ValueChanged -= OnSliderChanged;
        }

        _slider = e.NameScope.Find<Slider>("PART_ZoomSlider");
        if (_slider is not null)
        {
            _slider.Minimum = ZoomMinimum;
            _slider.Maximum = ZoomMaximum;
            _slider.TickFrequency = ZoomStep;
            _slider.IsSnapToTickEnabled = true;
            _slider.SmallChange = ZoomStep;
            _slider.LargeChange = ZoomStep;
            _slider.Value = Zoom;
            _slider.ValueChanged += OnSliderChanged;
            AutomationProperties.SetName(_slider, ShellStrings.ZoomLevel);
            ToolTip.SetTip(_slider, new TooltipContent { Title = ShellStrings.ZoomLevel });
        }

        Hook(e, "PART_ZoomOut", ShellStrings.ZoomOut, -ZoomStep);
        Hook(e, "PART_ZoomIn", ShellStrings.ZoomIn, ZoomStep);
    }

    private void Hook(TemplateAppliedEventArgs e, string name, string label, double delta)
    {
        if (e.NameScope.Find<Button>(name) is not { } button)
        {
            return;
        }

        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, new TooltipContent { Title = label });
        button.Click += (_, _) => UserZoom(Zoom + delta);
    }

    private void OnSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_syncing)
        {
            UserZoom(e.NewValue);
        }
    }

    private void UserZoom(double value)
    {
        // The user zooms in steps of 10: the slider snaps on a click or a key, a value set in code snaps here.
        var clamped = Math.Clamp(Math.Round(value / ZoomStep, MidpointRounding.AwayFromZero) * ZoomStep, ZoomMinimum, ZoomMaximum);
        SetCurrentValue(ZoomProperty, clamped);
        ZoomChanged?.Invoke(this, clamped);
    }

    private void UpdateFocusText() =>
        SetAndRaise(IsFocusTextShownProperty, ref _isFocusTextShown, !string.IsNullOrEmpty(FocusPane) && string.IsNullOrEmpty(Hint));

    private void SyncSlider()
    {
        if (_slider is null || _slider.Value == Zoom)
        {
            return;
        }

        _syncing = true;
        _slider.Value = Zoom;
        _syncing = false;
    }
}
