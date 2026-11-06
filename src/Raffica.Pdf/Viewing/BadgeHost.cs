using Avalonia.Controls;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Forms;
using Raffica.Pdf.Ocr;
using Raffica.Pdf.Redacting;

namespace Raffica.Pdf.Viewing;

/// <summary>
/// The one control in the <c>Badge</c> slot of a page view: it shows the annotate badge while an annotate tool is active, the redact badge while
/// the redact mode is on, the form badge otherwise, and nothing for a document without a form (no form badge exists then). While a text recognition runs its
/// badge is the one shown (it names the run and holds Cancel), and the scan hint, while it is on, comes before the others. It is a panel, so the page view measures and arranges
/// it like the annotate badge before, and <see cref="PageView.IsInBadge"/> finds the controls of every badge below it.
/// </summary>
internal sealed class BadgeHost : Panel, IDisposable
{
    private readonly AnnotateBadge _annotate;
    private readonly ToolState _tools;
    private FormBadge? _forms;
    private RedactModeBadge? _redact;
    private OcrRunBadge? _ocr;
    private ScanHint? _hint;

    public BadgeHost(AnnotateBadge annotate, FormBadge? forms, ToolState tools)
    {
        ArgumentNullException.ThrowIfNull(annotate);
        ArgumentNullException.ThrowIfNull(tools);
        _annotate = annotate;
        _tools = tools;
        Children.Add(annotate);
        _tools.Changed += Update;
        Forms = forms;
        Update();
    }

    /// <summary>The form badge; <see langword="null"/> for a document without a form, or before the first sharp page. Set it once the badge is built.</summary>
    public FormBadge? Forms
    {
        get => _forms;
        set
        {
            if (ReferenceEquals(_forms, value))
            {
                return;
            }

            if (_forms is not null)
            {
                Children.Remove(_forms);
            }

            _forms = value;
            if (value is not null)
            {
                Children.Add(value);
            }

            Update();
        }
    }

    /// <summary>The badge of the redact mode; <see langword="null"/> while the mode is off (it is made when the mode starts and goes with it).</summary>
    public RedactModeBadge? Redact
    {
        get => _redact;
        set
        {
            if (ReferenceEquals(_redact, value))
            {
                return;
            }

            if (_redact is not null)
            {
                _redact.ActiveChanged -= Update;
                Children.Remove(_redact);
            }

            _redact = value;
            if (value is not null)
            {
                Children.Add(value);
                value.ActiveChanged += Update;
            }

            Update();
        }
    }

    /// <summary>The badge of a text recognition; <see langword="null"/> until the first run of the tab (it is made then and kept until the tab closes).</summary>
    public OcrRunBadge? Ocr
    {
        get => _ocr;
        set
        {
            if (ReferenceEquals(_ocr, value))
            {
                return;
            }

            if (_ocr is not null)
            {
                _ocr.ActiveChanged -= Update;
                Children.Remove(_ocr);
            }

            _ocr = value;
            if (value is not null)
            {
                Children.Add(value);
                value.ActiveChanged += Update;
            }

            Update();
        }
    }

    /// <summary>The scan hint; <see langword="null"/> when none is on show (it is made when the hint is asked and goes when it ends).</summary>
    public ScanHint? Hint
    {
        get => _hint;
        set
        {
            if (ReferenceEquals(_hint, value))
            {
                return;
            }

            if (_hint is not null)
            {
                _hint.Ended -= Update;
                Children.Remove(_hint);
            }

            _hint = value;
            if (value is not null)
            {
                Children.Add(value);
                value.Ended += Update;
            }

            Update();
        }
    }

    /// <summary>Whether the annotate badge is the one shown.</summary>
    public bool AnnotateVisible => _annotate.IsVisible;

    public void Dispose() => _tools.Changed -= Update;

    private void Update()
    {
        // The run names itself first; a mode the user is in comes before the hint, which waits (and shows again) until the slot is free.
        var recognizing = _ocr?.Active == true;
        var annotating = _tools.Tool != AnnotateTool.None && !recognizing;
        var redacting = _redact?.Active == true && !annotating && !recognizing;
        var hinting = _hint is { Showing: true } && !recognizing && !annotating && !redacting;
        var others = !recognizing && !hinting;
        _annotate.IsVisible = annotating;
        if (_ocr is not null)
        {
            _ocr.IsVisible = recognizing;
        }

        if (_hint is not null)
        {
            _hint.IsVisible = hinting;
        }

        if (_redact is not null)
        {
            _redact.IsVisible = redacting;
        }

        if (_forms is not null)
        {
            _forms.IsVisible = others && !annotating && !redacting;
        }

        IsVisible = annotating || redacting || recognizing || hinting || _forms is not null;
        InvalidateMeasure();
    }
}
