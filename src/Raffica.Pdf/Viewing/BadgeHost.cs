using Avalonia.Controls;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Forms;

namespace Raffica.Pdf.Viewing;

/// <summary>
/// The one control in the <c>Badge</c> slot of a page view: it shows the annotate badge while an annotate tool is active and the form badge
/// otherwise, and nothing for a document without a form (no form badge exists then). It is a panel, so the page view measures and arranges
/// it like the annotate badge before, and <see cref="PageView.IsInBadge"/> finds the controls of both badges below it.
/// </summary>
internal sealed class BadgeHost : Panel, IDisposable
{
    private readonly AnnotateBadge _annotate;
    private readonly ToolState _tools;
    private FormBadge? _forms;

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

    /// <summary>Whether the annotate badge is the one shown.</summary>
    public bool AnnotateVisible => _annotate.IsVisible;

    public void Dispose() => _tools.Changed -= Update;

    private void Update()
    {
        var annotating = _tools.Tool != AnnotateTool.None;
        _annotate.IsVisible = annotating;
        if (_forms is not null)
        {
            _forms.IsVisible = !annotating;
        }

        IsVisible = annotating || _forms is not null;
        InvalidateMeasure();
    }
}
