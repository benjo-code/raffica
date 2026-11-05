using Raffica.Core.Forms;

namespace Raffica.Pdf.Forms;

/// <summary>
/// The widget of a tab's form that has the form focus (the ring of the overlay, the target of the form keys). Per tab, UI thread only.
/// The tab order is Core's <see cref="FormNavigation"/> over the widgets of the pages;
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

    /// <summary>
    /// Moves the focus to the next (or previous) widget that can be changed, walking the pages in order and never over a page that is not
    /// read: the walk steps over pages that are read and have no changeable widget.
    /// </summary>
    /// <returns>
    /// <see langword="false"/> and no move when there is no further widget in that direction, or when the next page in that direction is not
    /// read yet. Ask <see cref="PageToLoad"/>: a page means "read it (<c>LoadPageAsync</c>) and try again"; <see langword="null"/> means the
    /// end is a true end and the caller lets the key pass.
    /// </returns>
    public bool Move(FormSession session, bool forward)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (Step(session, forward).Key is not { } next)
        {
            return false;
        }

        Set(next);
        return true;
    }

    /// <summary>
    /// The page that blocks <see cref="Move"/> in that direction: the first page after (before) the focused widget that is not read, and
    /// only when no widget is found before it; <see langword="null"/> when <see cref="Move"/> would move or the walk reaches the end of
    /// the document with every page read.
    /// </summary>
    public int? PageToLoad(FormSession session, bool forward)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Step(session, forward).PageToLoad;
    }

    private (FormWidgetKey? Key, int? PageToLoad) Step(FormSession session, bool forward)
    {
        var count = session.PageCount;
        var step = forward ? 1 : -1;
        int page;
        if (Current is { } current)
        {
            if (session.IsLoaded(current.Page) && FormNavigation.Next(session.FieldsOn(current.Page), current, forward) is { } inPage)
            {
                return (inPage, null);
            }

            page = current.Page + step;
        }
        else
        {
            page = forward ? 0 : count - 1;
        }

        for (; page >= 0 && page < count; page += step)
        {
            if (!session.IsLoaded(page))
            {
                return (null, page);
            }

            if (FormNavigation.Next(session.FieldsOn(page), null, forward) is { } found)
            {
                return (found, null);
            }
        }

        return (null, null);
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
