using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Raffica.Pdf.TextEditing;

/// <summary>
/// What hangs over the top middle of the page area while the text edit mode is on: the sign that names the mode and how to leave it, the fixed line that
/// states the limit ("Edit lines, not paragraphs. Characters the font lacks are set in a replacement font."), the name of the focused line (its place, never
/// its text), the live note of the draft (missing characters, a refusal with its reason, what the longer line may run into, the reason of a locked line)
/// and the switch "Whole line in replacement font" (shown only when the replacement font has every character, never on by itself). There is no way to
/// dismiss it while the mode is on. One per tab, in the <c>Badge</c> slot of the page view through <see cref="Viewing.BadgeHost"/>, so pointer and key
/// events from inside it are not the page's. Shell tokens only.
/// </summary>
internal sealed class TextEditHintBar : Border, IDisposable
{
    private readonly TextEditSession _session;
    private readonly TextBlock _sign;
    private readonly TextBlock _limit;
    private readonly TextBlock _line;
    private readonly TextBlock _note;
    private readonly TextBlock _old;
    private readonly CheckBox _whole;
    private bool _syncing;
    private bool _active;

    public TextEditHintBar(TextEditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _sign = new TextBlock { Name = "PART_Sign", Text = PdfStrings.TextEditBadgeText, FontWeight = FontWeight.SemiBold };
        _limit = new TextBlock { Name = "PART_Limit", Text = PdfStrings.TextEditHintLine, TextWrapping = TextWrapping.Wrap };
        _line = new TextBlock { Name = "PART_Line", TextWrapping = TextWrapping.Wrap, IsVisible = false };
        _note = new TextBlock { Name = "PART_Note", TextWrapping = TextWrapping.Wrap, IsVisible = false };
        _old = new TextBlock { Name = "PART_OldText", Text = PdfStrings.TextEditOldTextNote, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        _whole = new CheckBox { Name = "PART_Whole", Content = PdfStrings.TextEditWholeRunSwitch, IsVisible = false, IsChecked = false };
        Child = new StackPanel { Spacing = 4, Children = { _sign, _limit, _line, _note, _whole, _old } };
        Padding = new Thickness(12, 6);
        BorderThickness = new Thickness(1);
        MaxWidth = 560;
        HorizontalAlignment = HorizontalAlignment.Center;
        Bind(BackgroundProperty, this.GetResourceObservable("Raffica.Brush.Surface.Band"));
        Bind(BorderBrushProperty, this.GetResourceObservable("Raffica.Brush.Rule"));
        foreach (var text in new[] { _sign, _limit, _line, _note, _old })
        {
            text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("Raffica.Brush.Text.Primary"));
        }

        AutomationProperties.SetName(this, PdfStrings.TextEditBarName);
        AutomationProperties.SetLiveSetting(_note, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(_line, AutomationLiveSetting.Polite);
        _whole.IsCheckedChanged += OnWholeChanged;
        session.Changed += Update;
        Update();
    }

    /// <summary>Raised when <see cref="Active"/> changed (the host shows or hides the bar).</summary>
    public event Action? ActiveChanged;

    /// <summary>Whether the mode is on.</summary>
    public bool Active => _active;

    /// <summary>The fixed line about the limits.</summary>
    public string Limit => _limit.Text ?? string.Empty;

    /// <summary>The name of the focused line (its place), empty while no line has the focus.</summary>
    public string LineName => _line.IsVisible ? _line.Text ?? string.Empty : string.Empty;

    /// <summary>The live note, empty while there is none.</summary>
    public string Note => _note.IsVisible ? _note.Text ?? string.Empty : string.Empty;

    /// <summary>Whether the switch for the whole line in the replacement font is shown.</summary>
    public bool WholeSwitchVisible => _whole.IsVisible;

    /// <summary>Whether the switch is on.</summary>
    public bool WholeSwitchOn => _whole.IsChecked == true;

    /// <summary>Whether the note about the old text in earlier versions is shown.</summary>
    public bool OldTextShown => _old.IsVisible;

    /// <summary>Turns the switch on or off the way a click does.</summary>
    public void SetWholeSwitch(bool on) => _whole.IsChecked = on;

    public void Dispose() => _session.Changed -= Update;

    private void OnWholeChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_syncing)
        {
            _session.SetWholeRun(_whole.IsChecked == true);
        }
    }

    private void Update()
    {
        _syncing = true;
        try
        {
            var focused = _session.Focused;
            if (focused is null)
            {
                _line.IsVisible = false;
            }
            else
            {
                var line = _session.RunsOf(focused.Page).ToList().FindIndex(r => r.ObjectIndex == focused.ObjectIndex) + 1;
                _line.Text = TextEditWording.RunName(Math.Max(1, line), focused.Page + 1, focused.IsLocked);
                _line.IsVisible = true;
            }

            var note = _session.Note;
            _note.Text = note ?? string.Empty;
            _note.IsVisible = !string.IsNullOrEmpty(note);
            _whole.IsVisible = _session.HasDraft && (_session.Preview?.WholeRunOffered == true || _session.WholeRun);
            _whole.IsChecked = _session.WholeRun;
            _old.IsVisible = _session.OldTextNoteShown;
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
