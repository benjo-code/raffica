using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Raffica.Core.Redaction;
using Raffica.Pdf.Redacting;

namespace Raffica.Pdf.Panes;

/// <summary>
/// The Look rows of the redact pane: the fill colour (presets and a <c>#RRGGBB</c> field), what happens to pictures under a mark (with the caution
/// for blurring and pixelating), the text on the selected marks with its style, the free choice of pages that become images with their
/// resolution, and the statement that the preview shows the fills only. Every change is one step of the session; a refusal ends in the note of the
/// pane with its reason and no change. The text fields commit with Enter or when they lose the focus, never while typing, and their keys are
/// theirs (the handler of the page view skips text boxes). Names for assistive technology never carry the overlay text.
/// </summary>
internal sealed partial class RedactPane
{
    private readonly List<Button> _fillPresets = [];
    private ToggleButton _lookToggle = null!;
    private ScrollViewer _lookScroll = null!;
    private TextBlock _contrastWarning = null!;
    private TextBlock _caution = null!;
    private TextBlock _rasterCount = null!;
    private TextBlock _previewNote = null!;
    private bool _lookSyncing;
    // What the three text fields showed when they were last brought in line with the plan: a field whose text differs is being edited.
    private string _fillShown = string.Empty;
    private string _overlayShown = string.Empty;
    private string _colorShown = string.Empty;

    // Set while a colour field is taken over by the plan whatever it holds (a commit, a preset): what was typed there is dealt with.
    // The marks the text of the overlay field is for: the selection when the field was last in line with the plan, so a click on another row
    // between typing and committing does not move the text to other marks.
    private HashSet<Guid> _overlayFor = [];
    private bool _forceFill;
    private bool _forceColor;

    /// <summary>The field of the fill colour (<c>#RRGGBB</c>).</summary>
    internal TextBox FillBox { get; private set; } = null!;

    /// <summary>The buttons of the preset fills, in order: black, white, then the five presets.</summary>
    internal IReadOnlyList<Button> FillPresets => _fillPresets;

    /// <summary>The choice of the treatment of pictures under a mark (solid, pixelate, blur).</summary>
    internal ComboBox ImageBox { get; private set; } = null!;

    /// <summary>The choice of the strength of pixelating and blurring; shown for those two only.</summary>
    internal ComboBox StrengthBox { get; private set; } = null!;

    /// <summary>The field of the text on the selected marks.</summary>
    internal TextBox OverlayBox { get; private set; } = null!;

    internal CheckBox RepeatBox { get; private set; } = null!;

    internal CheckBox AutoSizeBox { get; private set; } = null!;

    /// <summary>The field of the colour of the text; empty means automatic.</summary>
    internal TextBox TextColorBox { get; private set; } = null!;

    /// <summary>The box of the page the view shows: tick it and the page becomes an image.</summary>
    internal CheckBox RasterPageBox { get; private set; } = null!;

    /// <summary>The choice of the resolution of the images (150, 200 or 300 dpi).</summary>
    internal ComboBox DpiBox { get; private set; } = null!;

    /// <summary>The toggle that shows or hides the Look rows.</summary>
    internal ToggleButton LookToggle => _lookToggle;

    /// <summary>The caution for blurred and pixelated pictures; empty when it is not shown.</summary>
    public string CautionText => _caution.IsVisible ? _caution.Text ?? string.Empty : string.Empty;

    /// <summary>The warning that a chosen text colour has a low contrast with the fill; empty when it is not shown.</summary>
    public string ContrastWarningText => _contrastWarning.IsVisible ? _contrastWarning.Text ?? string.Empty : string.Empty;

    /// <summary>The statement that the preview shows the fills only.</summary>
    public string PreviewNoteText => _previewNote.Text ?? string.Empty;

    private bool FillDirty => (FillBox.Text ?? string.Empty) != _fillShown;

    private bool OverlayDirty => (OverlayBox.Text ?? string.Empty) != _overlayShown;

    private bool ColorDirty => (TextColorBox.Text ?? string.Empty) != _colorShown;

    private void BuildLook()
    {
        _lookToggle = new ToggleButton { Name = "PART_LookToggle", Content = PdfStrings.RedactLookToggle, HorizontalAlignment = HorizontalAlignment.Left };
        _lookToggle.IsCheckedChanged += (_, _) => _lookScroll.IsVisible = _lookToggle.IsChecked == true;

        FillBox = new TextBox { Name = "PART_FillBox", MaxLength = 16 };
        AutomationProperties.SetName(FillBox, PdfStrings.RedactFillName);
        FillBox.AddHandler(KeyDownEvent, (_, e) => OnFieldKey(e, CommitFill), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        FillBox.LostFocus += (_, _) => CommitFillIfDirty();
        var presets = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var fill in new[] { RedactionFill.Black, RedactionFill.White }.Concat(RedactionFill.Presets))
        {
            var hex = fill.ToHex();
            var swatch = new Button
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromRgb(fill.Red, fill.Green, fill.Blue)),
            };
            swatch.Name = "PART_FillPreset" + hex[1..];
            AutomationProperties.SetName(swatch, PdfStrings.RedactFillPresetFormat.Replace("{0}", hex, StringComparison.Ordinal));
            ToolTip.SetTip(swatch, hex);
            swatch.Click += (_, _) => ApplyFill(() => _session.SetFill(fill));
            _fillPresets.Add(swatch);
            presets.Children.Add(swatch);
        }

        ImageBox = new ComboBox
        {
            Name = "PART_ImageBox",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { PdfStrings.RedactImageSolid, PdfStrings.RedactImagePixelate, PdfStrings.RedactImageBlur },
            SelectedIndex = 0,
        };
        AutomationProperties.SetName(ImageBox, PdfStrings.RedactImageLabel);
        ImageBox.SelectionChanged += (_, _) => OnImageChanged();
        StrengthBox = new ComboBox
        {
            Name = "PART_StrengthBox",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = new[] { PdfStrings.RedactStrengthCoarse, PdfStrings.RedactStrengthVeryCoarse },
            SelectedIndex = 0,
            IsVisible = false,
        };
        AutomationProperties.SetName(StrengthBox, PdfStrings.RedactStrengthLabel);
        StrengthBox.SelectionChanged += (_, _) => OnImageChanged();
        _caution = new TextBlock { Name = "PART_ImageCaution", Text = PdfStrings.RedactImageCaution, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        _caution.Classes.Add("nav-hint");

        OverlayBox = new TextBox { Name = "PART_OverlayBox", PlaceholderText = PdfStrings.RedactOverlayPlaceholder, IsEnabled = false };
        AutomationProperties.SetName(OverlayBox, PdfStrings.RedactOverlayName);
        OverlayBox.AddHandler(KeyDownEvent, (_, e) => OnFieldKey(e, CommitOverlay), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        OverlayBox.LostFocus += (_, _) => CommitOverlayIfDirty();
        RepeatBox = new CheckBox { Name = "PART_OverlayRepeat", Content = PdfStrings.RedactOverlayRepeat };
        RepeatBox.IsCheckedChanged += (_, _) => OnStyleBoxChanged();
        AutoSizeBox = new CheckBox { Name = "PART_OverlayAutoSize", Content = PdfStrings.RedactOverlayAutoSize, IsChecked = true };
        AutoSizeBox.IsCheckedChanged += (_, _) => OnStyleBoxChanged();
        TextColorBox = new TextBox { Name = "PART_TextColorBox", MaxLength = 16, PlaceholderText = "#RRGGBB" };
        AutomationProperties.SetName(TextColorBox, PdfStrings.RedactTextColorName);
        TextColorBox.AddHandler(KeyDownEvent, (_, e) => OnFieldKey(e, CommitTextColor), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        TextColorBox.LostFocus += (_, _) => CommitTextColorIfDirty();
        _contrastWarning = new TextBlock { Name = "PART_ContrastWarning", TextWrapping = TextWrapping.Wrap, IsVisible = false };
        AutomationProperties.SetLiveSetting(_contrastWarning, AutomationLiveSetting.Polite);

        RasterPageBox = new CheckBox { Name = "PART_RasterPage" };
        RasterPageBox.IsCheckedChanged += (_, _) => OnRasterPageChanged();
        _rasterCount = new TextBlock { Name = "PART_RasterCount", TextWrapping = TextWrapping.Wrap, IsVisible = false };
        DpiBox = new ComboBox
        {
            Name = "PART_DpiBox",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = RedactAppearance.Dpis.Select(d => PdfStrings.RedactRasterDpiFormat.Replace("{0}", d.ToString(PdfStrings.Culture), StringComparison.Ordinal)).ToArray(),
            SelectedIndex = RedactAppearance.DpiIndex(RedactAppearance.DefaultDpi),
            IsVisible = false,
        };
        AutomationProperties.SetName(DpiBox, PdfStrings.RedactRasterDpiLabel);
        DpiBox.SelectionChanged += (_, _) => OnDpiChanged();
        var rasterHint = new TextBlock { Text = PdfStrings.RedactRasterHint, TextWrapping = TextWrapping.Wrap };
        rasterHint.Classes.Add("nav-hint");

        _previewNote = new TextBlock { Name = "PART_PreviewNote", Text = PdfStrings.RedactPreviewNote, TextWrapping = TextWrapping.Wrap };
        _previewNote.Classes.Add("nav-hint");

        var host = new StackPanel
        {
            Name = "PART_Look",
            Spacing = 6,
            Margin = new Thickness(12, 0, 12, 8),
            Children =
            {
                Label(PdfStrings.RedactFillLabel), FillBox, presets,
                Label(PdfStrings.RedactImageLabel), ImageBox, StrengthBox, _caution,
                Label(PdfStrings.RedactOverlayLabel), OverlayBox, RepeatBox, AutoSizeBox, Label(PdfStrings.RedactTextColorLabel), TextColorBox, _contrastWarning,
                Label(PdfStrings.RedactRasterLabel), RasterPageBox, _rasterCount, DpiBox, rasterHint,
                _previewNote,
            },
        };
        AutomationProperties.SetName(host, PdfStrings.RedactLookName);
        _lookScroll = new ScrollViewer { Name = "PART_LookHost", MaxHeight = 420, IsVisible = false, Content = host, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    private static TextBlock Label(string text)
    {
        var label = new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        return label;
    }

    private static void OnFieldKey(KeyEventArgs e, Action commit)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            commit();
            e.Handled = true;
        }
    }

    private void CommitFillIfDirty()
    {
        if (FillDirty && !_disposed)
        {
            CommitFill();
        }
    }

    private void CommitFill() =>
        ApplyFill(() => RedactAppearance.TryParseFill(FillBox.Text, out var fill) ? _session.SetFill(fill) : RedactResult.Refused(RedactProblem.FillNotAColour));

    private void ApplyFill(Func<RedactResult> change)
    {
        _forceFill = true;
        try
        {
            Report(change());
            SyncLook();
        }
        finally
        {
            _forceFill = false;
        }
    }

    private void CommitTextColorIfDirty()
    {
        if (ColorDirty && !_disposed)
        {
            CommitTextColor();
        }
    }

    private void CommitTextColor()
    {
        var text = TextColorBox.Text?.Trim();
        _forceColor = true;
        try
        {
            if (string.IsNullOrEmpty(text))
            {
                Report(_session.SetOverlayStyle(_session.Plan.Overlay with { TextColor = null }));
            }
            else if (RedactAppearance.TryParseFill(text, out var color))
            {
                Report(_session.SetOverlayStyle(_session.Plan.Overlay with { TextColor = color }));
            }
            else
            {
                Report(RedactResult.Refused(RedactProblem.FillNotAColour));
            }

            SyncLook();
        }
        finally
        {
            _forceColor = false;
        }
    }

    private void CommitOverlayIfDirty()
    {
        if (OverlayDirty && !_disposed)
        {
            CommitOverlay();
        }
    }

    private void CommitOverlay()
    {
        if (_overlayFor.Count == 0)
        {
            return;
        }

        var text = string.IsNullOrEmpty(OverlayBox.Text) ? null : OverlayBox.Text;
        var result = _session.SetOverlay(_overlayFor, text, _session.Plan.Overlay);
        Report(result);
        SyncLook();
    }

    private void OnStyleBoxChanged()
    {
        if (_lookSyncing || _disposed)
        {
            return;
        }

        Report(_session.SetOverlayStyle(_session.Plan.Overlay with { Repeat = RepeatBox.IsChecked == true, AutoSize = AutoSizeBox.IsChecked == true }));
        SyncLook();
    }

    private void OnImageChanged()
    {
        if (_lookSyncing || _disposed)
        {
            return;
        }

        var treatment = (RedactionImageTreatment)Math.Max(0, ImageBox.SelectedIndex);
        var strength = (RedactionImageStrength)Math.Max(0, StrengthBox.SelectedIndex);
        Report(_session.SetImageTreatment(treatment, strength));
        SyncLook();
    }

    private void OnRasterPageChanged()
    {
        if (_lookSyncing || _disposed)
        {
            return;
        }

        Report(_session.SetRasterPage(_view.CurrentPage, RasterPageBox.IsChecked == true));
        SyncLook();
    }

    private void OnDpiChanged()
    {
        if (_lookSyncing || _disposed || DpiBox.SelectedIndex < 0)
        {
            return;
        }

        Report(_session.SetRasterDpi(RedactAppearance.Dpis[DpiBox.SelectedIndex]));
        SyncLook();
    }

    // Brings the Look rows in line with the plan (also after an undo) without making a step; a field the user is typing in keeps its text.
    private void SyncLook()
    {
        if (_disposed)
        {
            return;
        }

        var plan = _session.Plan;
        _lookSyncing = true;
        try
        {
            if (!FillDirty || _forceFill)
            {
                _fillShown = plan.Fill.ToHex();
                FillBox.Text = _fillShown;
            }

            ImageBox.SelectedIndex = (int)plan.ImageTreatment;
            StrengthBox.SelectedIndex = (int)plan.ImageStrength;
            StrengthBox.IsVisible = RedactAppearance.NeedsCaution(plan.ImageTreatment);
            _caution.IsVisible = StrengthBox.IsVisible;

            OverlayBox.IsEnabled = _session.IsActive && _session.Selection.Count > 0;
            if (!OverlayDirty)
            {
                var primary = _session.Selection.Primary is { } id ? plan.Marks.FirstOrDefault(m => m.Id == id) : null;
                _overlayShown = primary?.OverlayText ?? string.Empty;
                OverlayBox.Text = _overlayShown;
                _overlayFor = new HashSet<Guid>(_session.Selection.Ids);
            }

            RepeatBox.IsChecked = plan.Overlay.Repeat;
            AutoSizeBox.IsChecked = plan.Overlay.AutoSize;
            if (!ColorDirty || _forceColor)
            {
                _colorShown = plan.Overlay.TextColor?.ToHex() ?? string.Empty;
                TextColorBox.Text = _colorShown;
            }

            if (RedactAppearance.LowContrast(plan) is { } contrast)
            {
                _contrastWarning.Text = PdfStrings.RedactContrastWarningFormat.Replace("{0}", contrast.ToString("0.0", PdfStrings.Culture), StringComparison.Ordinal);
                _contrastWarning.IsVisible = true;
            }
            else
            {
                _contrastWarning.IsVisible = false;
            }

            var page = _view.CurrentPage;
            RasterPageBox.Content = PdfStrings.RedactRasterPageFormat.Replace("{0}", (page + 1).ToString(PdfStrings.Culture), StringComparison.Ordinal);
            RasterPageBox.IsChecked = plan.RasterPages.Contains(page);
            var count = plan.RasterPages.Count;
            _rasterCount.IsVisible = count > 0;
            _rasterCount.Text = count == 1 ? PdfStrings.RedactRasterCountOne : PdfStrings.RedactRasterCountManyFormat.Replace("{0}", count.ToString(PdfStrings.Culture), StringComparison.Ordinal);
            DpiBox.IsVisible = count > 0;
            DpiBox.SelectedIndex = Math.Max(0, RedactAppearance.DpiIndex(plan.RasterDpi));
        }
        finally
        {
            _lookSyncing = false;
        }
    }
}
