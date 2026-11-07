using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Raffica.Core.TextEdit;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.TextEditing;

/// <summary>
/// The outlines of the lines of the pages in view while the text edit mode is on, drawn in the one pass of the page view (never part of the file, never
/// printed). An editable line has a solid edge, a locked line a dashed one with a lock icon next to it, so a lock is never shown by colour alone; the
/// reason is the tooltip of the page view over the line (<see cref="TextEditInteraction"/>) and the note of the hint bar. The focused line gets a heavier
/// edge and a frame. A page with more lines than <see cref="OutlineLimit"/> outlines only the focused one (all of its lines are locked anyway). The shell
/// tokens only (<c>Raffica.Brush.Ink</c>, <c>Raffica.Brush.Surface.Band</c>). Used on the UI thread only.
/// </summary>
internal sealed class RunOutlines : IPageOverlay, IDisposable
{
    /// <summary>The most lines of a page that are outlined one by one.</summary>
    public const int OutlineLimit = 1000;

    private readonly TextEditSession _session;

    public RunOutlines(TextEditSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        session.Changed += OnChanged;
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>The rectangle of a run in the view, or <see langword="null"/> while its page is not shown (the one place the page mapping is applied, also for the field).</summary>
    public static Rect? RectOf(PageView view, TextRun run)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(run);
        return view.ToView(run.Page, run.Bounds);
    }

    /// <inheritdoc />
    public void Render(DrawingContext context, PageView view)
    {
        if (!_session.IsActive)
        {
            return;
        }

        var (first, last) = view.VisiblePages;
        if (last < first)
        {
            return;
        }

        var ink = BrushOf(view, "Raffica.Brush.Ink", Colors.Black);
        var band = BrushOf(view, "Raffica.Brush.Surface.Band", Colors.White);
        var edgeUnder = new Pen(band, 3);
        var edgeOver = new Pen(ink, 1);
        var dashedUnder = new Pen(band, 3);
        var dashedOver = new Pen(ink, 1.25, new ImmutableDashStyle([3, 3], 0));
        var focusedUnder = new Pen(band, 5);
        var focusedOver = new Pen(ink, 2.5);
        var focusedDashed = new Pen(ink, 2.5, new ImmutableDashStyle([4, 3], 0));
        var focused = _session.Focused;
        var drafted = _session.DraftRun;
        for (var page = first; page <= last; page++)
        {
            var runs = _session.RunsOf(page);
            if (runs.Count == 0)
            {
                continue;
            }

            foreach (var run in runs.Count > OutlineLimit ? runs.Where(r => IsFocused(focused, r)) : runs)
            {
                if (RectOf(view, run) is not { } rect || (drafted is not null && drafted.Page == run.Page && drafted.ObjectIndex == run.ObjectIndex))
                {
                    continue;
                }

                rect = rect.Inflate(1);
                var isFocused = IsFocused(focused, run);
                if (run.IsLocked)
                {
                    context.DrawRectangle(null, dashedUnder, rect);
                    context.DrawRectangle(null, dashedOver, rect);
                    DrawLock(context, rect, ink, band);
                }
                else
                {
                    context.DrawRectangle(null, edgeUnder, rect);
                    context.DrawRectangle(null, edgeOver, rect);
                }

                if (isFocused)
                {
                    var frame = rect.Inflate(3);
                    context.DrawRectangle(null, focusedUnder, frame);
                    context.DrawRectangle(null, run.IsLocked ? focusedDashed : focusedOver, frame);
                }
            }
        }
    }

    public void Dispose() => _session.Changed -= OnChanged;

    private static bool IsFocused(TextRun? focused, TextRun run) => focused is not null && focused.Page == run.Page && focused.ObjectIndex == run.ObjectIndex;

    // A small padlock above the left end of the line: a body and a shackle, 9 by 15 DIPs.
    private static void DrawLock(DrawingContext context, Rect rect, IBrush ink, IBrush band)
    {
        var x = rect.Left;
        var y = Math.Max(0, rect.Top - 15);
        var shackle = new StreamGeometry();
        using (var g = shackle.Open())
        {
            g.BeginFigure(new Point(x + 2, y + 6), false);
            g.LineTo(new Point(x + 2, y + 3.5));
            g.ArcTo(new Point(x + 7, y + 3.5), new Size(2.5, 2.5), 0, false, SweepDirection.Clockwise);
            g.LineTo(new Point(x + 7, y + 6));
            g.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(band, 3.5), shackle);
        context.DrawGeometry(null, new Pen(ink, 1.5), shackle);
        context.DrawRectangle(band, new Pen(ink, 1.25), new Rect(x, y + 6, 9, 7));
    }

    private static IBrush BrushOf(PageView view, string key, Color fallback) =>
        view.TryFindResource(key, view.ActualThemeVariant, out var value) && value is IBrush brush ? brush : new ImmutableSolidColorBrush(fallback);

    private void OnChanged() => Changed?.Invoke();
}
