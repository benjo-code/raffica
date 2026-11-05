using Raffica.Core.Forms;

namespace Raffica.Pdf.Forms;

/// <summary>
/// The widget of a tab's form that has the form focus (the ring of the overlay, the target of the form keys). Per tab, UI thread only.
/// The tab order is Core's <see cref="FormNavigation"/> over the widgets of the pages read so far (<see cref="FormSession.LoadedFields"/>);
/// widgets Raffica cannot change are skipped.
/// </summary>
internal sealed class FormFocus
{
    /// <summary>Raised when <see cref="Current"/> changed.</summary>
    public event Action? Changed;

    /// <summary>The focused widget, or <see langword="null"/> for none.</summary>
    public FormWidgetKey? Current { get; private set; }

    /// <summary>The focused widget as the session last read it; <see langword="null"/> when none is focused or its page is not (or no longer) read.</summary>
    public FormField? CurrentField(FormSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Current is { } key ? session.FieldsOn(key.Page).FirstOrDefault(f => f.Key == key) : null;
    }

    /// <summary>Moves the focus to the next (or previous) widget that can be changed.</summary>
    /// <returns><see langword="false"/> past the last or before the first widget: the focus stays and the caller lets the key pass.</returns>
    public bool Move(FormSession session, bool forward)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (FormNavigation.Next(session.LoadedFields(), Current, forward) is not { } next)
        {
            return false;
        }

        Set(next);
        return true;
    }

    /// <summary>Focuses a widget, or none.</summary>
    public void Set(FormWidgetKey? key)
    {
        if (Current == key)
        {
            return;
        }

        Current = key;
        Changed?.Invoke();
    }
}
