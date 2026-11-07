using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Raffica.Pdf.Ocr;

/// <summary>
/// What hangs over the top middle of the page area while a text recognition runs: the sentence of the run ("Recognizing text: 3 of 12 pages") and Cancel.
/// The run is not modal, so the page stays readable under it. The sentence is a polite live region: the start, the steps and the end are read out by a screen reader,
/// and the mode is stated in words, never by colour alone. One per tab, in the <c>Badge</c> slot of the page view through <see cref="Viewing.BadgeHost"/>. Shell tokens only.
/// </summary>
internal sealed class OcrRunBadge : Border, IDisposable
{
    private readonly OcrSession _session;
    private readonly TextBlock _text;
    private readonly Button _cancel;
    private bool _active;

    public OcrRunBadge(OcrSession session, Action cancel)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(cancel);
        _session = session;
        _text = new TextBlock { Name = "PART_Text", Text = PdfStrings.OcrBadgeText, VerticalAlignment = VerticalAlignment.Center };
        _cancel = new Button { Name = "PART_Cancel", Content = PdfStrings.CmdOcrCancel };
        _cancel.Classes.Add("boxed");
        _cancel.Click += (_, _) => cancel();
        Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { _text, _cancel } };
        Padding = new Thickness(12, 4);
        BorderThickness = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Center;
        Bind(BackgroundProperty, this.GetResourceObservable("Raffica.Brush.Surface.Band"));
        Bind(BorderBrushProperty, this.GetResourceObservable("Raffica.Brush.Rule"));
        _text.Bind(TextBlock.ForegroundProperty, _text.GetResourceObservable("Raffica.Brush.Text.Primary"));
        AutomationProperties.SetName(this, PdfStrings.OcrBadgeText);
        AutomationProperties.SetLiveSetting(_text, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(_text, PdfStrings.OcrBadgeText);
        session.Changed += Update;
        Update();
    }

    /// <summary>Raised when <see cref="Active"/> changed (the host shows or hides the badge).</summary>
    public event Action? ActiveChanged;

    /// <summary>Whether a run is on.</summary>
    public bool Active => _active;

    /// <summary>The sentence shown (and announced).</summary>
    public string Message => _text.Text ?? string.Empty;

    /// <summary>The Cancel button (for tests).</summary>
    internal Button CancelButton => _cancel;

    public void Dispose() => _session.Changed -= Update;

    private void Update()
    {
        if (_session.Message is { } message)
        {
            _text.Text = message;
            AutomationProperties.SetName(_text, message);
        }

        if (_active != _session.IsActive)
        {
            _active = _session.IsActive;
            ActiveChanged?.Invoke();
        }
    }
}
