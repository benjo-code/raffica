using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Raffica.Core.Forms;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.Forms;

/// <summary>What a marker of the form overlay shows.</summary>
internal enum FormMarkerKind
{
    /// <summary>A star at the corner of a required field that has no value.</summary>
    Star,

    /// <summary>A lock at a field Raffica does not let the user change (read-only, a signature, a button).</summary>
    Lock,

    /// <summary>The ring around the focused widget.</summary>
    Focus,
}

/// <summary>The per-tab form pieces the window keeps: the overlay and the form focus.</summary>
internal sealed record FormParts(FormMarkers Markers, FormFocus Focus);

/// <summary>One marker as planned for the current view; <see cref="Bounds"/> is the glyph (star, lock) or the widget (focus ring), in view coordinates.</summary>
internal readonly record struct FormMarker(FormMarkerKind Kind, FormWidgetKey Key, Rect Bounds);

/// <summary>
/// The marks of the form over the pages: a star at the corner of every empty required field, a lock at every locked field and the ring around
/// the focused widget. State is never carried by colour alone: the star and the lock are shapes on a chip with an outline, the ring is a
/// 2 px line with a corner flag. Colours are the shell tokens. It reads the session's cache only and starts no job; nothing is planned before
/// the first sharp page or while an annotate tool is active. <see cref="Attach"/> makes the view ask the session for the widgets of its
/// visible pages, once per page. UI thread only.
/// </summary>
internal sealed class FormMarkers : IPageOverlay, IDisposable
{
    /// <summary>The side of the chip of a star or a lock, in DIPs.</summary>
    public const double GlyphSize = 16;

    private const double RingWidth = 2;
    private const double RingGap = 2;
    private readonly FormSession _session;
    private readonly FormFocus _focus;
    private readonly HashSet<int> _requested = [];
    private readonly HashSet<int> _seenLoaded = [];
    private PageView? _view;
    private bool _disposed;

    public FormMarkers(FormSession session, FormFocus focus)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(focus);
        _session = session;
        _focus = focus;
        _session.Changed += OnSessionChanged;
        _focus.Changed += Invalidate;
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>Tells whether nothing is to be drawn (the window says so while an annotate tool is active); default: never.</summary>
    public Func<bool> Suppressed { get; set; } = static () => false;

    /// <summary>Makes the overlay draw again (the window calls it when <see cref="Suppressed"/> may have changed its answer).</summary>
    public void Invalidate()
    {
        if (!_disposed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Lets <paramref name="view"/> ask the session for the widgets of its visible pages: from the first sharp page on, on every change of the
    /// view, each page once. For a document without a form nothing is asked and nothing is started.
    /// </summary>
    public void Attach(PageView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        Detach();
        _view = view;
        view.FirstSharpPage += OnFirstSharpPage;
        view.ViewChanged += OnViewChanged;
        if (view.FirstSharpPageShown)
        {
            OnFirstSharpPage();
        }
    }

    /// <summary>
    /// Turns the form highlight of the main view on or off and renders the visible pages again (the session drops what it rendered with the
    /// old setting). The one entry point for the first switch-on and the toggle; does nothing for a document without a form or when nothing changes.
    /// </summary>
    public void ApplyHighlight(bool on)
    {
        if (_disposed || _session.Highlight == on || !_session.HasForm)
        {
            return;
        }

        _session.Highlight = on;
        _view?.RestartRendering();
    }

    /// <summary>
    /// Whether a widget gets the star (required and empty). Why a widget is marked is told by the field itself: <see cref="FormField.LockReason"/>
    /// for a lock, this for the star; the hover text belongs to the form interaction.
    /// </summary>
    public static bool IsMarkedRequired(FormField field) => IsEmptyRequired(field);

    /// <summary>The markers of the visible pages as they are drawn, in the order lock, star, ring of each widget.</summary>
    public IReadOnlyList<FormMarker> Plan(PageView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (_disposed || !view.FirstSharpPageShown || Suppressed())
        {
            return [];
        }

        List<FormMarker> plan = [];
        var (first, last) = view.VisiblePages;
        for (var page = first; page <= last; page++)
        {
            foreach (var field in _session.FieldsOn(page))
            {
                if (view.ToView(page, field.Bounds) is not { } rect)
                {
                    continue;
                }

                var star = IsEmptyRequired(field);
                if (field.Lock != FormLock.None)
                {
                    plan.Add(new FormMarker(FormMarkerKind.Lock, field.Key, GlyphAt(rect, right: false)));
                }

                if (star)
                {
                    plan.Add(new FormMarker(FormMarkerKind.Star, field.Key, GlyphAt(rect, right: true)));
                }

                if (_focus.Current == field.Key)
                {
                    plan.Add(new FormMarker(FormMarkerKind.Focus, field.Key, rect));
                }
            }
        }

        return plan;
    }

    /// <inheritdoc />
    public void Render(DrawingContext context, PageView view)
    {
        var plan = Plan(view);
        if (plan.Count == 0)
        {
            return;
        }

        var ink = BrushOf(view, "Raffica.Brush.Ink", Colors.Black);
        var band = BrushOf(view, "Raffica.Brush.Surface.Band", Colors.White);
        foreach (var marker in plan)
        {
            switch (marker.Kind)
            {
                case FormMarkerKind.Star:
                    DrawChip(context, marker.Bounds, ink, band);
                    DrawStar(context, marker.Bounds, ink);
                    break;
                case FormMarkerKind.Lock:
                    DrawChip(context, marker.Bounds, ink, band);
                    DrawLock(context, marker.Bounds, ink);
                    break;
                default:
                    DrawRing(context, marker.Bounds, ink, band);
                    break;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Changed -= OnSessionChanged;
        _focus.Changed -= Invalidate;
        Detach();
    }

    // A required field with no value. Check boxes and radio buttons are empty while nothing is chosen.
    private static bool IsEmptyRequired(FormField field) =>
        field.Traits.HasFlag(FormFieldTraits.Required)
        && string.IsNullOrEmpty(field.State.Text)
        && field.State.SelectedOptions.Count == 0
        && field.State.Checked != true;

    // The chip sits inside the widget at the top corner; a widget too small for it keeps the chip at its corner anyway.
    private static Rect GlyphAt(Rect widget, bool right) =>
        new(right ? widget.Right - GlyphSize : widget.Left, widget.Top, GlyphSize, GlyphSize);

    private static IBrush BrushOf(PageView view, string key, Color fallback) =>
        view.TryFindResource(key, view.ActualThemeVariant, out var value) && value is IBrush brush ? brush : new ImmutableSolidColorBrush(fallback);

    private static void DrawChip(DrawingContext context, Rect box, IBrush ink, IBrush band) =>
        context.DrawRectangle(band, new Pen(ink, 1), box, 3, 3);

    private static void DrawStar(DrawingContext context, Rect box, IBrush ink)
    {
        var centre = box.Center;
        var outer = (GlyphSize / 2) - 3;
        var inner = outer * 0.45;
        var star = new StreamGeometry();
        using (var g = star.Open())
        {
            for (var i = 0; i < 10; i++)
            {
                var radius = i % 2 == 0 ? outer : inner;
                var angle = (-Math.PI / 2) + (i * Math.PI / 5);
                var point = new Point(centre.X + (radius * Math.Cos(angle)), centre.Y + (radius * Math.Sin(angle)));
                if (i == 0)
                {
                    g.BeginFigure(point, true);
                }
                else
                {
                    g.LineTo(point);
                }
            }

            g.EndFigure(true);
        }

        context.DrawGeometry(ink, null, star);
    }

    private static void DrawLock(DrawingContext context, Rect box, IBrush ink)
    {
        var x = box.Left + 4;
        var y = box.Top + 3;
        var shackle = new StreamGeometry();
        using (var g = shackle.Open())
        {
            g.BeginFigure(new Point(x + 1.5, y + 5), false);
            g.LineTo(new Point(x + 1.5, y + 3));
            g.ArcTo(new Point(x + 6.5, y + 3), new Size(2.5, 2.5), 0, false, SweepDirection.Clockwise);
            g.LineTo(new Point(x + 6.5, y + 5));
            g.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(ink, 1.25), shackle);
        context.DrawRectangle(ink, null, new Rect(x, y + 5, 8, 6), 1, 1);
    }

    // A light line under a dark one keeps the ring visible on a dark page and on a light one; the flag in the corner is the shape that
    // tells the ring from a border of the form itself.
    private static void DrawRing(DrawingContext context, Rect widget, IBrush ink, IBrush band)
    {
        var frame = widget.Inflate(RingGap + (RingWidth / 2));
        context.DrawRectangle(null, new Pen(band, RingWidth + 2), frame);
        context.DrawRectangle(null, new Pen(ink, RingWidth), frame);
        var flag = new StreamGeometry();
        using (var g = flag.Open())
        {
            g.BeginFigure(new Point(frame.Left - 1, frame.Top - 1), true);
            g.LineTo(new Point(frame.Left + 9, frame.Top - 1));
            g.LineTo(new Point(frame.Left - 1, frame.Top + 9));
            g.EndFigure(true);
        }

        context.DrawGeometry(ink, null, flag);
    }

    private void Detach()
    {
        if (_view is { } view)
        {
            view.FirstSharpPage -= OnFirstSharpPage;
            view.ViewChanged -= OnViewChanged;
            _view = null;
        }

        _requested.Clear();
        _seenLoaded.Clear();
    }

    private void OnSessionChanged()
    {
        // A page that was read and is gone from the cache (a saved document replaced the old one) is asked for again.
        Request();
        Invalidate();
    }

    private void OnFirstSharpPage()
    {
        if (_disposed || !_session.HasForm)
        {
            return;
        }

        // Applied once, after the first sharp page and only for a document with a form.
        ApplyHighlight(true);
        Request();
    }

    private void OnViewChanged() => Request();

    private void Request()
    {
        if (_disposed || _view is not { FirstSharpPageShown: true } view || !_session.HasForm)
        {
            return;
        }

        var (first, last) = view.VisiblePages;
        for (var page = first; page <= last; page++)
        {
            if (_session.IsLoaded(page))
            {
                _seenLoaded.Add(page);
            }
            else if (_requested.Add(page) || _seenLoaded.Remove(page))
            {
                _ = _session.LoadPageAsync(page);
            }
        }
    }
}
