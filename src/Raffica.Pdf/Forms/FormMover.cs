using Avalonia;
using Raffica.Core.Forms;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.Forms;

/// <summary>What <see cref="FormMover.Step"/> found.</summary>
internal enum FormStep
{
    /// <summary>The form focus moved to the next widget.</summary>
    Moved,

    /// <summary>The walk stopped at a page that is not read yet (<c>Page</c>): read it and ask again.</summary>
    NeedsPage,

    /// <summary>There is no further widget in that direction: a true end.</summary>
    End,
}

/// <summary>
/// The one way the form focus walks the widgets of a tab: Tab and Shift+Tab, the Enter of a single-line editor, and the commands Next field,
/// Previous field and Next required field. It reads the pages the walk needs (the UI thread never waits for it), enters a form from the page
/// that is on screen, and scrolls the target into view. UI thread only.
/// </summary>
internal sealed class FormMover(PageView view, FormSession forms, FormFocus focus)
{
    /// <summary>
    /// One step of the walk that never waits: moves the focus when the next widget is known, else names the page to read first. With no
    /// focus yet the walk begins on the first (last) page on screen; if that page has no widget it goes on from the start (end) of the document.
    /// </summary>
    public (FormStep Step, int Page) Step(bool forward)
    {
        if (focus.Current is null && forms.PageCount > 0)
        {
            var entry = Math.Clamp(EntryPage(forward), 0, forms.PageCount - 1);
            if (!forms.IsLoaded(entry))
            {
                return (FormStep.NeedsPage, entry);
            }

            if (FormNavigation.Next(forms.FieldsOn(entry), null, forward) is { } key)
            {
                focus.Set(key);
                Reveal();
                return (FormStep.Moved, entry);
            }
        }

        if (focus.Move(forms, forward))
        {
            Reveal();
            return (FormStep.Moved, focus.Current!.Value.Page);
        }

        return focus.PageToLoad(forms, forward) is { } page ? (FormStep.NeedsPage, page) : (FormStep.End, -1);
    }

    // The first (last) page that really is on screen: a sliver of the next page at the bottom edge is no page to start on. A page counts when a
    // quarter of the view (or the whole page, if it is smaller) shows; if none does, the first (last) page the view reports.
    private int EntryPage(bool forward)
    {
        var (first, last) = view.VisiblePages;
        var area = new Rect(view.Bounds.Size);
        for (var page = forward ? first : last; page >= first && page <= last; page += forward ? 1 : -1)
        {
            if (view.PageBounds(page) is { } rect && rect.Intersect(area) is { } shown && shown.Height >= Math.Min(rect.Height, area.Height) * 0.25)
            {
                return page;
            }
        }

        return forward ? first : last;
    }

    /// <summary>
    /// Moves the focus to the next (previous) widget, reading the pages on the way: where the walk stops at a page that is not read, that page is
    /// read and the step is made again (once per page, so a run of unread pages is crossed). Returns whether the focus moved; <see langword="false"/>
    /// means a true end, or a page that could not be read.
    /// </summary>
    public async Task<bool> MoveAsync(bool forward)
    {
        for (var attempt = 0; attempt <= forms.PageCount + 1; attempt++)
        {
            var (step, page) = Step(forward);
            switch (step)
            {
                case FormStep.Moved:
                    return true;
                case FormStep.End:
                    return false;
            }

            await forms.LoadPageAsync(page);
            if (!forms.IsLoaded(page))
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Moves the focus to the next required field that is still empty, starting after the focused widget and wrapping around the document once;
    /// the pages are read as the walk needs them. The empty required fields are asked of the document first (their names), so a field filled in
    /// the last moments is not offered.
    /// </summary>
    /// <returns>Whether the focus moved (false: no required field is empty).</returns>
    public async Task<bool> MoveToNextRequiredAsync()
    {
        await forms.RefreshRequiredAsync();
        var names = forms.EmptyRequired;
        if (names.Count == 0 || forms.PageCount == 0)
        {
            return false;
        }

        var start = focus.Current;
        var count = forms.PageCount;
        var first = start?.Page ?? 0;

        // Every page once from the focused one, then the focused one again for the widgets before the focus.
        for (var i = 0; i <= count; i++)
        {
            var page = (first + i) % count;
            if (!forms.IsLoaded(page))
            {
                await forms.LoadPageAsync(page);
                if (!forms.IsLoaded(page))
                {
                    continue;
                }
            }

            var fields = forms.FieldsOn(page);
            var from = i == 0 && start is { } here ? fields.ToList().FindIndex(w => w.Key == here) : -1;
            var until = i == count && start is { } stop ? fields.ToList().FindIndex(w => w.Key == stop) : fields.Count;
            for (var k = from + 1; k < until; k++)
            {
                var field = fields[k];
                if (field.Lock == FormLock.None && names.Contains(field.Key.FieldName) && FormMarkers.IsMarkedRequired(field))
                {
                    focus.Set(field.Key);
                    Reveal();
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Scrolls the focused widget into view (nothing moves while it is in view).</summary>
    public void Reveal()
    {
        if (focus.CurrentField(forms) is { } field)
        {
            view.Reveal(field.Key.Page, field.Bounds);
        }
    }
}
