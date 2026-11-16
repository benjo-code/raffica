using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Raffica.Write.Layout;
using SkiaSharp;

namespace Raffica.Write.Viewing;

/// <summary>
/// Lets draws and the end of the pictures meet safely: the render thread enters the gate for the length of one draw, and the tab closing closes it with an action that
/// frees what the draws use. Closing never waits: the action runs at once when no draw is in flight, otherwise on the thread of the last draw that ends (never before
/// it ends), and a draw that starts after the close is refused, so nothing uses what was freed.
/// </summary>
internal sealed class DrawGate
{
    private readonly object _lock = new();
    private int _inFlight;
    private bool _closed;
    private Action? _whenIdle;

    /// <summary>Whether the gate is closed (no draw may start).</summary>
    public bool IsClosed
    {
        get
        {
            lock (_lock)
            {
                return _closed;
            }
        }
    }

    /// <summary>Enters for one draw; false once the gate is closed (the draw must then do nothing).</summary>
    public bool TryEnter()
    {
        lock (_lock)
        {
            if (_closed)
            {
                return false;
            }

            _inFlight++;
            return true;
        }
    }

    /// <summary>Leaves after a draw that <see cref="TryEnter"/> let in; the last one out after a close runs the action of the close.</summary>
    public void Exit()
    {
        Action? run = null;
        lock (_lock)
        {
            _inFlight--;
            if (_inFlight == 0 && _closed)
            {
                run = _whenIdle;
                _whenIdle = null;
            }
        }

        run?.Invoke();
    }

    /// <summary>Refuses later draws and runs <paramref name="whenIdle"/> once no draw is in flight (at once, or when the last one ends). Never waits. A second close does nothing.</summary>
    public void Close(Action whenIdle)
    {
        ArgumentNullException.ThrowIfNull(whenIdle);
        var runNow = false;
        lock (_lock)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            if (_inFlight == 0)
            {
                runNow = true;
            }
            else
            {
                _whenIdle = whenIdle;
            }
        }

        if (runNow)
        {
            whenIdle();
        }
    }
}

/// <summary>
/// Draws one laid-out page into the Skia canvas of the render thread: the page's origin is rounded to a whole device pixel (the hairlines of the renderer snap
/// to pixels measured from the page origin), the scale is the pixels per point at the current zoom and screen scaling, and everything runs inside the
/// <see cref="DrawGate"/> so a tab that closes cannot dispose the pictures under the draw. It runs on Avalonia's render thread and allocates no bitmap.
/// </summary>
internal sealed class PageDrawOperation(Rect bounds, LaidOutPage page, double dipsPerPoint, ImageCache images, DrawGate gate, Action<int>? drawn, Action<long, long>? timed = null) : ICustomDrawOperation
{
    public Rect Bounds => bounds;

    public bool HitTest(Point p) => bounds.Contains(p);

    // Two operations are never equal: a page is drawn again whenever the view asks for a frame.
    public bool Equals(ICustomDrawOperation? other) => false;

    public void Dispose()
    {
    }

    public void Render(ImmediateDrawingContext context)
    {
        if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature || !gate.TryEnter())
        {
            return;
        }

        var startedAt = timed is null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            var matrix = canvas.TotalMatrix;
            var pixelsPerDip = matrix.ScaleX;
            if (!(pixelsPerDip > 0f) || !float.IsFinite(pixelsPerDip))
            {
                return;
            }

            // The page origin in device pixels, made whole; from here on the canvas works in device pixels and the scale carries the zoom.
            var origin = matrix.MapPoint((float)bounds.X, (float)bounds.Y);
            var save = canvas.Save();
            try
            {
                canvas.SetMatrix(SKMatrix.CreateTranslation(MathF.Round(origin.X), MathF.Round(origin.Y)));
                PageRenderer.Draw(canvas, page, (float)(dipsPerPoint * pixelsPerDip), images);
            }
            finally
            {
                canvas.RestoreToCount(save);
            }

            drawn?.Invoke(page.Index);
            timed?.Invoke(startedAt, System.Diagnostics.Stopwatch.GetTimestamp());
        }
        finally
        {
            gate.Exit();
        }
    }
}
