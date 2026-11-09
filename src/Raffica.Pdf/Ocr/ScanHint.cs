using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Raffica.Pdf.Ocr;

/// <summary>
/// The hint over the page area after the first sharp page of a document that looks like a scan: "This looks like a scan. Make it searchable?" with
/// Recognize…, Not now and Don't ask again for this file. It is asked once per tab and document, held in memory only (the answer is never stored),
/// shown only when the OCR library is there and never in a measurement run, and it never starts a run itself: Recognize… opens the options card. One per tab, in the
/// <c>Badge</c> slot of the page view through <see cref="Viewing.BadgeHost"/>. Shell tokens only.
/// </summary>
internal sealed class ScanHint : Border
{
    private readonly Button _recognize;
    private readonly Button _notNow;
    private readonly Button _never;

    public ScanHint(Action recognize)
    {
        ArgumentNullException.ThrowIfNull(recognize);
        var text = new TextBlock { Name = "PART_Text", Text = PdfStrings.OcrScanHintText, VerticalAlignment = VerticalAlignment.Center };
        _recognize = new Button { Name = "PART_Recognize", Content = PdfStrings.OcrScanHintRecognize };
        _recognize.Classes.Add("primary");
        _notNow = new Button { Name = "PART_NotNow", Content = PdfStrings.OcrScanHintNotNow };
        _notNow.Classes.Add("boxed");
        _never = new Button { Name = "PART_Never", Content = PdfStrings.OcrScanHintNever };
        _never.Classes.Add("boxed");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _recognize, _notNow, _never } };
        Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { text, buttons } };
        Padding = new Thickness(12, 4);
        BorderThickness = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Center;
        Bind(BackgroundProperty, this.GetResourceObservable("Raffica.Brush.Surface.Band"));
        Bind(BorderBrushProperty, this.GetResourceObservable("Raffica.Brush.Rule"));
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Raffica.Brush.Text.Primary"));
        AutomationProperties.SetName(this, PdfStrings.OcrScanHintName);
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        _recognize.Click += (_, _) =>
        {
            End();
            recognize();
        };
        _notNow.Click += (_, _) => End();
        _never.Click += (_, _) => End();
    }

    /// <summary>Raised when the hint ended: any of its buttons was used (the host hides it).</summary>
    public event Action? Ended;

    /// <summary>Whether the hint is on show.</summary>
    public bool Showing { get; private set; } = true;

    /// <summary>The Recognize… button (for tests).</summary>
    internal Button RecognizeButton => _recognize;

    /// <summary>The Not now button (for tests).</summary>
    internal Button NotNowButton => _notNow;

    /// <summary>The Don't ask again button (for tests).</summary>
    internal Button NeverButton => _never;

    /// <summary>Ends the hint (a button was used, or a run began).</summary>
    public void End()
    {
        if (Showing)
        {
            Showing = false;
            Ended?.Invoke();
        }
    }
}
