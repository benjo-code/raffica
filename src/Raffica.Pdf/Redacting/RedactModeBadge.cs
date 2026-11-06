using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Raffica.Pdf.Redacting;

/// <summary>
/// What hangs over the top middle of the page area while the redact mode is on: the sign that names the mode and how to leave it
/// ("Redact – Esc to finish", the mode is stated in words, never by colour alone) and the choice of the marking tool (Area: drag on the
/// page; Text: select text and mark it). One per tab, in the <c>Badge</c> slot of the page view through <see cref="Viewing.BadgeHost"/>, so
/// pointer and key events from inside it are not the tool's. Shell tokens only.
/// </summary>
internal sealed class RedactModeBadge : Border, IDisposable
{
    private readonly RedactSession _session;
    private readonly TextBlock _text;
    private readonly RadioButton _area;
    private readonly RadioButton _textTool;
    private bool _syncing;
    private bool _active;

    public RedactModeBadge(RedactSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _text = new TextBlock { Name = "PART_Text", Text = PdfStrings.RedactBadgeText, VerticalAlignment = VerticalAlignment.Center };
        _area = new RadioButton { Name = "PART_Area", GroupName = "RedactTool", Content = PdfStrings.RedactToolArea };
        _textTool = new RadioButton { Name = "PART_Text_Tool", GroupName = "RedactTool", Content = PdfStrings.RedactToolText };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _area, _textTool } };
        AutomationProperties.SetName(tools, PdfStrings.RedactToolsName);
        Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { _text, tools } };
        Padding = new Thickness(12, 4);
        BorderThickness = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Center;
        Bind(BackgroundProperty, this.GetResourceObservable("Raffica.Brush.Surface.Band"));
        Bind(BorderBrushProperty, this.GetResourceObservable("Raffica.Brush.Rule"));
        _text.Bind(TextBlock.ForegroundProperty, _text.GetResourceObservable("Raffica.Brush.Text.Primary"));
        AutomationProperties.SetName(this, PdfStrings.RedactBadgeText);
        AutomationProperties.SetLiveSetting(this, AutomationLiveSetting.Polite);
        _area.IsCheckedChanged += (_, _) => Choose(_area, RedactTool.Area);
        _textTool.IsCheckedChanged += (_, _) => Choose(_textTool, RedactTool.Text);
        session.Changed += Update;
        Update();
    }

    /// <summary>Raised when <see cref="Active"/> changed (the host shows or hides the badge).</summary>
    public event Action? ActiveChanged;

    /// <summary>Whether the mode is on.</summary>
    public bool Active => _active;

    /// <summary>The sign shown.</summary>
    public string Message => _text.Text ?? string.Empty;

    /// <summary>Chooses the area tool.</summary>
    public void ChooseArea() => _session.Tool = RedactTool.Area;

    /// <summary>Chooses the text tool.</summary>
    public void ChooseText() => _session.Tool = RedactTool.Text;

    public void Dispose() => _session.Changed -= Update;

    private void Choose(RadioButton button, RedactTool tool)
    {
        if (!_syncing && button.IsChecked == true)
        {
            _session.Tool = tool;
        }
    }

    private void Update()
    {
        _syncing = true;
        try
        {
            _area.IsChecked = _session.Tool == RedactTool.Area;
            _textTool.IsChecked = _session.Tool == RedactTool.Text;
        }
        finally
        {
            _syncing = false;
        }

        if (_active != _session.IsActive)
        {
            _active = _session.IsActive;
            ActiveChanged?.Invoke();
        }
    }
}
