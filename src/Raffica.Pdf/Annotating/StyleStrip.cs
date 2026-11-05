using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Core.Annotations;

namespace Raffica.Pdf.Annotating;

/// <summary>
/// The colour and width choice of the annotate mode: a strip of swatches (and, for a drawing tool, three widths; for the text box tool,
/// three sizes) over the page area, under the sign of the mode. It is shown while a tool is active, and with the Select tool while a mark
/// is selected. With a mark selected it changes that mark (<see cref="SelectController.SetSelectionStyleAsync"/>, one history step per
/// click); otherwise it sets what the active tool remembers (<see cref="ToolState.SetColor"/> and <see cref="ToolState.SetWidth"/>, kept in
/// the settings). The colours are those of Core's palettes (data, not style literals); the choice in force is shown by the filled frame of
/// the button (the shell's "on" state) and read out as the toggle state, and every button has a name. A mark that cannot be changed
/// disables the strip, with the reason in the tooltip. Used on the UI thread only; shell tokens only.
/// </summary>
internal sealed class StyleStrip : Border, IDisposable
{
    private readonly ToolState _tools;
    private readonly SelectController _select;
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly List<(ToggleButton Button, AnnotationColor Color)> _swatches = [];
    private readonly List<(ToggleButton Button, StrokeWidth Width)> _widths = [];
    private Layout? _layout;
    private Plan? _plan;
    private bool _disposed;

    public StyleStrip(ToolState tools, SelectController select)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(select);
        _tools = tools;
        _select = select;
        Child = _row;
        Padding = new Thickness(4);
        BorderThickness = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Center;
        Cursor = new Cursor(StandardCursorType.Arrow);
        Bind(BackgroundProperty, this.GetResourceObservable("Raffica.Brush.Surface.Band"));
        Bind(BorderBrushProperty, this.GetResourceObservable("Raffica.Brush.Rule"));

        // A press on the strip (also between the buttons) is not a press on the page under it.
        AddHandler(PointerPressedEvent, (_, e) => e.Handled = true, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, e) => e.Handled = true, RoutingStrategies.Bubble, handledEventsToo: true);

        _tools.Changed += Refresh;
        _tools.StyleChanged += Refresh;
        _select.SelectionChanged += Refresh;
        _select.Changed += Refresh;
        Refresh();
    }

    /// <summary>The colour buttons in the order they are offered (empty while the strip is hidden; for tests).</summary>
    internal IReadOnlyList<ToggleButton> Swatches => [.. _swatches.Select(s => s.Button)];

    /// <summary>The width buttons of a drawing tool, or the size buttons of the text box tool (empty when the strip offers none; for tests).</summary>
    internal IReadOnlyList<ToggleButton> WidthButtons => [.. _widths.Select(w => w.Button)];

    /// <summary>Whether the strip acts on the selected mark (else on the active tool).</summary>
    internal bool ActsOnSelection => _plan?.OnSelection == true;

    /// <summary>The reason the strip is disabled, or <see langword="null"/>.</summary>
    internal string? DisabledReason => _plan?.Reason;

    /// <summary>The colour of a swatch.</summary>
    internal AnnotationColor ColorOf(ToggleButton swatch) => _swatches.First(s => ReferenceEquals(s.Button, swatch)).Color;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tools.Changed -= Refresh;
        _tools.StyleChanged -= Refresh;
        _select.SelectionChanged -= Refresh;
        _select.Changed -= Refresh;
    }

    private static string NameOf(MarkupColor color) => color switch
    {
        MarkupColor.Yellow => PdfStrings.MarkupColorYellow,
        MarkupColor.Green => PdfStrings.MarkupColorGreen,
        MarkupColor.Blue => PdfStrings.MarkupColorBlue,
        MarkupColor.Pink => PdfStrings.MarkupColorPink,
        _ => PdfStrings.MarkupColorOrange,
    };

    private static string NameOf(DrawColor color) => color switch
    {
        DrawColor.Red => PdfStrings.DrawColorRed,
        DrawColor.Black => PdfStrings.DrawColorBlack,
        DrawColor.Blue => PdfStrings.DrawColorBlue,
        DrawColor.Green => PdfStrings.DrawColorGreen,
        _ => PdfStrings.DrawColorOrange,
    };

    private static string NameOfWidth(StrokeWidth width) => width switch
    {
        StrokeWidth.Thin => PdfStrings.StyleWidthThin,
        StrokeWidth.Medium => PdfStrings.StyleWidthMedium,
        _ => PdfStrings.StyleWidthThick,
    };

    private static string NameOfSize(StrokeWidth step) => step switch
    {
        StrokeWidth.Thin => PdfStrings.StyleSizeSmall,
        StrokeWidth.Medium => PdfStrings.StyleSizeMedium,
        _ => PdfStrings.StyleSizeLarge,
    };

    private static bool IsMarkup(AnnotationKind kind) => kind is AnnotationKind.Highlight or AnnotationKind.Underline or AnnotationKind.StrikeOut;

    // What the strip offers now, or null when it is hidden.
    private Plan? Decide()
    {
        var tool = _tools.Tool;
        if (tool == AnnotateTool.None)
        {
            return null;
        }

        if (tool != AnnotateTool.Select)
        {
            return tool switch
            {
                AnnotateTool.Highlight or AnnotateTool.Underline or AnnotateTool.StrikeOut =>
                    new Plan(Layout.Markup, false, _tools.MarkupColor(tool), null, true, null),
                AnnotateTool.TextBox =>
                    new Plan(Layout.Text, false, DrawPalette.ColorOf(_tools.StyleOf(tool).Color), _tools.StyleOf(tool).Width, true, null),
                _ =>
                    new Plan(Layout.Draw, false, DrawPalette.ColorOf(_tools.StyleOf(tool).Color), _tools.StyleOf(tool).Width, true, null),
            };
        }

        if (_select.Selected is not { } selected)
        {
            return null;
        }

        var enabled = _select.CanChangeSelectionStyle;
        var reason = enabled ? null : _select.LockReason ?? PdfStrings.SelectLockedReason;
        if (IsMarkup(selected.Kind))
        {
            return new Plan(Layout.Markup, true, selected.Color, null, enabled, reason);
        }

        if (selected.TextBox is { } box)
        {
            // A text box takes a colour; its size is changed in its text, not here.
            return new Plan(Layout.Colors, true, box.Color, null, enabled, reason);
        }

        if (selected.Drawing is { } drawing)
        {
            StrokeWidth? current = null;
            foreach (var step in Enum.GetValues<StrokeWidth>())
            {
                if (Math.Abs(StrokeWidths.PointsOf(step) - drawing.Width) < 0.001)
                {
                    current = step;
                }
            }

            return new Plan(Layout.Draw, true, selected.Color, current, enabled, reason);
        }

        return null;
    }

    private void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        var plan = Decide();
        _plan = plan;
        if (plan is null)
        {
            IsVisible = false;
            return;
        }

        IsVisible = true;
        if (_layout != plan.Layout || _swatches.Count == 0)
        {
            Build(plan);
        }

        AutomationProperties.SetName(this, plan.Layout switch
        {
            Layout.Draw => PdfStrings.StyleStripColorWidthName,
            Layout.Text => PdfStrings.StyleStripColorSizeName,
            _ => PdfStrings.StyleStripColorName,
        });

        foreach (var (button, color) in _swatches)
        {
            Show(button, plan.Color == color, plan, AutomationProperties.GetName(button) ?? string.Empty);
        }

        foreach (var (button, width) in _widths)
        {
            Show(button, plan.Width == width, plan, AutomationProperties.GetName(button) ?? string.Empty);
        }
    }

    private static void Show(ToggleButton button, bool chosen, Plan plan, string name)
    {
        button.IsChecked = chosen;
        button.IsEnabled = plan.Enabled;
        var tip = plan.Reason is { } reason ? name + ". " + reason : name;
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetHelpText(button, plan.Reason ?? string.Empty);
    }

    private void Build(Plan plan)
    {
        _layout = plan.Layout;
        _swatches.Clear();
        _widths.Clear();
        _row.Children.Clear();

        if (plan.Layout == Layout.Markup)
        {
            foreach (var color in AnnotationPalette.All)
            {
                AddSwatch(AnnotationPalette.ColorOf(color), NameOf(color));
            }
        }
        else
        {
            foreach (var color in DrawPalette.All)
            {
                AddSwatch(DrawPalette.ColorOf(color), NameOf(color));
            }
        }

        if (plan.Layout is Layout.Draw or Layout.Text)
        {
            _row.Children.Add(new Border { Width = 1, Margin = new Thickness(4, 4), Classes = { "style-separator" } });
            foreach (var step in Enum.GetValues<StrokeWidth>())
            {
                AddWidth(step, plan.Layout == Layout.Text);
            }
        }
    }

    private void AddSwatch(AnnotationColor color, string name)
    {
        var swatch = new Border
        {
            Width = 16,
            Height = 16,
            Background = new SolidColorBrush(Avalonia.Media.Color.FromRgb(color.R, color.G, color.B)),
            Classes = { "mark-swatch" },
        };
        var button = Button(swatch, name);
        button.Click += (_, _) => ChooseColor(color, button);
        _swatches.Add((button, color));
        _row.Children.Add(button);
    }

    private void AddWidth(StrokeWidth step, bool isTextSize)
    {
        Control content;
        string name;
        if (isTextSize)
        {
            var points = TextBoxSpec.FontSizeOf(step);
            name = PdfStrings.Fill(PdfStrings.StylePointsFormat, NameOfSize(step), points);
            content = new TextBlock { Text = "A", FontSize = 10 + (points / 3), VerticalAlignment = VerticalAlignment.Center };
        }
        else
        {
            var points = StrokeWidths.PointsOf(step);
            name = PdfStrings.Fill(PdfStrings.StylePointsFormat, NameOfWidth(step), points);
            content = new Border { Width = 16, Height = points, VerticalAlignment = VerticalAlignment.Center, Classes = { "width-bar" } };
        }

        var button = Button(content, name);
        button.Click += (_, _) => ChooseWidth(step, button);
        _widths.Add((button, step));
        _row.Children.Add(button);
    }

    private static ToggleButton Button(Control content, string name)
    {
        var button = new ToggleButton { Content = content, Padding = new Thickness(0), Width = 26 };
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
        ToolTip.SetShowOnDisabled(button, true);
        return button;
    }

    private void ChooseColor(AnnotationColor color, ToggleButton clicked)
    {
        if (_plan is not { Enabled: true } plan)
        {
            return;
        }

        // The choice shows at once; the next refresh shows what the document or the settings really have.
        foreach (var (button, _) in _swatches)
        {
            button.IsChecked = ReferenceEquals(button, clicked);
        }

        if (plan.OnSelection)
        {
            _ = _select.SetSelectionStyleAsync(color, null);
        }
        else
        {
            _tools.SetColor(_tools.Tool, color);
        }
    }

    private void ChooseWidth(StrokeWidth step, ToggleButton clicked)
    {
        if (_plan is not { Enabled: true } plan)
        {
            return;
        }

        foreach (var (button, _) in _widths)
        {
            button.IsChecked = ReferenceEquals(button, clicked);
        }

        if (plan.OnSelection)
        {
            _ = _select.SetSelectionStyleAsync(null, StrokeWidths.PointsOf(step));
        }
        else
        {
            _tools.SetWidth(_tools.Tool, step);
        }
    }

    private enum Layout
    {
        Markup,
        Draw,
        Text,
        Colors,
    }

    private sealed record Plan(Layout Layout, bool OnSelection, AnnotationColor? Color, StrokeWidth? Width, bool Enabled, string? Reason);
}
