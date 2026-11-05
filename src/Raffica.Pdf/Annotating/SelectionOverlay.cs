using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Raffica.Core.Annotations;
using Raffica.Core.Text;
using Raffica.Pdf.Viewing;

namespace Raffica.Pdf.Annotating;

/// <summary>
/// The frame and the handles of the selected mark, drawn over the pages in the pass of the page view (the mark itself is drawn by PDFium).
/// The frame is a dashed line under a solid one and the handles are squares with an outline, so the selection reads without colour; a mark
/// that cannot be changed (another program's drawing) gets a lock sign with the reason instead of handles. While a drag or a nudge runs the
/// new shape is shown as a ghost. Colours are the shell tokens of the app. Used on the UI thread only.
/// </summary>
internal sealed class SelectionOverlay : IPageOverlay
{
    /// <summary>The side of a drawn handle, in DIPs.</summary>
    public const double HandleDraw = 8;

    /// <summary>The side of the area of a handle that takes the pointer, in DIPs.</summary>
    public const double HandleHit = 24;

    private const double FramePadding = 3;
    private const double LockBandWidth = 360;
    private readonly StrokePreview _ghost = new();
    private int _page;
    private PageRect _frame;
    private IReadOnlyList<Handle> _handles = [];
    private string? _lockReason;
    private FormattedText? _lockText;

    public SelectionOverlay()
    {
        _ghost.Changed += OnGhostChanged;
    }

    /// <inheritdoc />
    public event Action? Changed;

    /// <summary>Whether a selection is shown.</summary>
    public bool IsActive { get; private set; }

    /// <summary>The handles shown (none for a markup and for a locked mark).</summary>
    public IReadOnlyList<Handle> Handles => IsActive ? _handles : [];

    /// <summary>The reason a mark cannot be changed, or <see langword="null"/>.</summary>
    public string? LockReason => IsActive ? _lockReason : null;

    /// <summary>Shows a selection.</summary>
    /// <param name="page">The page.</param>
    /// <param name="frame">The frame in page points.</param>
    /// <param name="handles">The handles in page points.</param>
    /// <param name="lockReason">The reason the mark is locked, or <see langword="null"/>.</param>
    /// <param name="ghost">The shape being dragged, or <see langword="null"/>.</param>
    /// <param name="ghostColor">The colour of the ghost.</param>
    public void Show(int page, PageRect frame, IReadOnlyList<Handle> handles, string? lockReason, DrawingGeometry? ghost, AnnotationColor ghostColor)
    {
        (_page, _frame, _handles, IsActive) = (page, frame, handles, true);
        if (!string.Equals(_lockReason, lockReason, StringComparison.Ordinal))
        {
            _lockReason = lockReason;
            _lockText = null;
        }

        if (ghost is null)
        {
            _ghost.Clear();
        }
        else if (ghost.Kind is AnnotationKind.Rectangle or AnnotationKind.Ellipse)
        {
            _ghost.ShowBox(page, ghostColor, ghost.Width, ghost.Box, ghost.Kind == AnnotationKind.Ellipse);
        }
        else
        {
            _ghost.ShowStrokes(page, ghostColor, ghost.Width, ghost.Strokes);
        }

        Changed?.Invoke();
    }

    /// <summary>Hides the selection.</summary>
    public void Clear()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        _handles = [];
        _lockReason = null;
        _lockText = null;
        _ghost.Clear();
        Changed?.Invoke();
    }

    /// <inheritdoc />
    public void Render(DrawingContext context, PageView view)
    {
        if (!IsActive)
        {
            return;
        }

        _ghost.Render(context, view);
        if (view.ToView(_page, _frame) is not { } rect)
        {
            return;
        }

        var ink = BrushOf(view, "Raffica.Brush.Ink", Colors.Black);
        var band = BrushOf(view, "Raffica.Brush.Surface.Band", Colors.White);
        var frame = rect.Inflate(FramePadding);

        // A light line under a dark dashed one stays visible on a dark page and on a light one.
        context.DrawRectangle(null, new Pen(band, 3), frame);
        context.DrawRectangle(null, new Pen(ink, 1.5, new ImmutableDashStyle([4, 3], 0)), frame);
        foreach (var handle in _handles)
        {
            if (view.ToView(_page, handle.Position.X, handle.Position.Y) is not { } at)
            {
                continue;
            }

            var square = new Rect(at.X - (HandleDraw / 2), at.Y - (HandleDraw / 2), HandleDraw, HandleDraw);
            context.DrawRectangle(band, new Pen(ink, 1.5), square);
        }

        if (_lockReason is not null)
        {
            DrawLock(context, view, frame, ink, band);
        }
    }

    /// <summary>The area of a handle that takes the pointer, around its middle in the view.</summary>
    /// <param name="middle">The middle of the handle in the view.</param>
    /// <returns>The hit area.</returns>
    public static Rect HitAreaOf(Point middle) => new(middle.X - (HandleHit / 2), middle.Y - (HandleHit / 2), HandleHit, HandleHit);

    private static IBrush BrushOf(PageView view, string key, Color fallback) =>
        view.TryFindResource(key, view.ActualThemeVariant, out var value) && value is IBrush brush ? brush : new ImmutableSolidColorBrush(fallback);

    // The lock sign and the reason in a small band under the frame (above it when the frame is at the bottom of the view).
    private void DrawLock(DrawingContext context, PageView view, Rect frame, IBrush ink, IBrush band)
    {
        _lockText ??= new FormattedText(
            _lockReason!, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Typeface.Default, 12, ink)
        {
            MaxTextWidth = LockBandWidth - 30,
            MaxTextHeight = 64,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        var height = Math.Max(24, _lockText.Height + 10);
        var width = Math.Min(LockBandWidth, Math.Max(_lockText.Width + 34, 60));
        var below = frame.Bottom + 6;
        var top = below + height <= view.Bounds.Height ? below : Math.Max(0, frame.Top - 6 - height);
        var left = Math.Clamp(frame.Left, 0, Math.Max(0, view.Bounds.Width - width));
        var box = new Rect(left, top, width, height);
        context.DrawRectangle(band, new Pen(ink, 1), box, 3, 3);

        // The sign: a body with a shackle.
        var sign = new Point(box.Left + 10, box.Top + 5);
        var pen = new Pen(ink, 1.5);
        var shackle = new StreamGeometry();
        using (var g = shackle.Open())
        {
            g.BeginFigure(new Point(sign.X + 2, sign.Y + 6), false);
            g.LineTo(new Point(sign.X + 2, sign.Y + 3));
            g.ArcTo(new Point(sign.X + 8, sign.Y + 3), new Size(3, 3), 0, false, SweepDirection.Clockwise);
            g.LineTo(new Point(sign.X + 8, sign.Y + 6));
            g.EndFigure(false);
        }

        context.DrawGeometry(null, pen, shackle);
        context.DrawRectangle(ink, null, new Rect(sign.X, sign.Y + 6, 10, 8), 1, 1);
        context.DrawText(_lockText, new Point(box.Left + 26, box.Top + 5));
    }

    private void OnGhostChanged() => Changed?.Invoke();
}
