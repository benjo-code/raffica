using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace Raffica.Pdf.Annotating;

/// <summary>
/// The sign that the annotate mode is on: a small band over the top of the page area that names the active tool and says how to leave
/// ("Highlight – Esc to finish"). The mode is stated in words, never by colour alone; the band takes no pointer input. Shell tokens only.
/// </summary>
internal sealed class ModeIndicator : Border, IDisposable
{
    private readonly ToolState _tools;
    private readonly TextBlock _text;

    public ModeIndicator(ToolState tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        _tools = tools;
        _text = new TextBlock { Name = "PART_Text" };
        Child = _text;
        Padding = new Thickness(12, 4);
        BorderThickness = new Thickness(1);
        IsHitTestVisible = false;
        Focusable = false;
        Bind(BackgroundProperty, this.GetResourceObservable("Raffica.Brush.Surface.Band"));
        Bind(BorderBrushProperty, this.GetResourceObservable("Raffica.Brush.Rule"));
        _text.Bind(TextBlock.ForegroundProperty, _text.GetResourceObservable("Raffica.Brush.Text.Primary"));
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        _tools.Changed += Update;
        Update();
    }

    /// <summary>The text shown (empty while the mode is off).</summary>
    public string Message => _text.Text ?? string.Empty;

    /// <summary>The name of a tool as the ribbon shows it.</summary>
    public static string NameOf(AnnotateTool tool) => tool switch
    {
        AnnotateTool.Select => PdfStrings.CmdSelectMark,
        AnnotateTool.Highlight => PdfStrings.CmdHighlight,
        AnnotateTool.Underline => PdfStrings.CmdUnderline,
        AnnotateTool.StrikeOut => PdfStrings.CmdStrikeOut,
        AnnotateTool.Freehand => PdfStrings.CmdFreehand,
        AnnotateTool.Rectangle => PdfStrings.CmdRectangle,
        AnnotateTool.Ellipse => PdfStrings.CmdEllipse,
        AnnotateTool.Line => PdfStrings.CmdLine,
        AnnotateTool.Arrow => PdfStrings.CmdArrow,
        AnnotateTool.TextBox => PdfStrings.CmdTextBox,
        _ => string.Empty,
    };

    public void Dispose() => _tools.Changed -= Update;

    private void Update()
    {
        var on = _tools.Tool != AnnotateTool.None;
        _text.Text = on ? PdfStrings.AnnotateModeFormat.Replace("{0}", NameOf(_tools.Tool), StringComparison.Ordinal) : string.Empty;
        AutomationProperties.SetName(this, _text.Text);
        IsVisible = on;
    }
}
