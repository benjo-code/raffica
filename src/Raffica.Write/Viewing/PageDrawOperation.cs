using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Raffica.Write.Layout;
using SkiaSharp;

namespace Raffica.Write.Viewing;

/// <summary>
/// Lets draws and the end of the pictures meet safely: the render thread enters the gate for the length of one draw, and the tab closing closes it, which waits for
/// the draws that run and refuses every later one. The image cache of a view may only be cleared or disposed after <see cref="Close"/> returned true.
/// </summary>
internal sealed class DrawGate
{
    private readonly object _lock = new();
    private int _inFlight;
    private bool _closed;

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

    /// <summary>Leaves after a draw that <see cref="TryEnter"/> let in.</summary>
    public void Exit()
    {
        lock (_lock)
        {
            _inFlight--;
            if (_inFlight == 0 && _closed)
            {
                Monitor.PulseAll(_lock);
            }
        }
    }

    /// <summary>Refuses later draws and waits (at most <paramref name="timeout"/>) for the ones that run; true when none runs any more.</summary>
    public bool Close(TimeSpan timeout)
    {
        lock (_lock)
        {
            _closed = true;
            var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
            while (_inFlight > 0)
            {
                var left = deadline - Environment.TickCount64;
                if (left <= 0 || !Monitor.Wait(_lock, (int)left))
                {
                    return _inFlight == 0;
                }
            }

            return true;
        }
    }
}

/// <summary>
/// Draws one laid-out page into the Skia canvas of the render thread: the page's origin is rounded to a whole device pixel (the hairlines of the renderer snap
/// to pixels measured from the page origin), the scale is the pixels per point at the current zoom and screen scaling, and everything runs inside the
/// <see cref="DrawGate"/> so a tab that closes cannot dispose the pictures under the draw. It runs on Avalonia's render thread and allocates no bitmap.
/// </summary>
internal sealed class PageDrawOperation(Rect bounds, LaidOutPage page, double dipsPerPoint, ImageCache images, DrawGate gate, Action<int>? drawn) : ICustomDrawOperation
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
        }
        finally
        {
            gate.Exit();
        }
    }
}
