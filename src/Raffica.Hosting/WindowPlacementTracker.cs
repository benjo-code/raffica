using Avalonia;
using Avalonia.Controls;
using Raffica.Shell.Frame;

namespace Raffica.Hosting;

/// <summary>
/// Keeps what an app stores about its window when it closes: the restore bounds (the last bounds seen in the normal state) and whether the window was maximized.
/// A real window system may raise the position or size event of a maximize or a minimize before the window state changes (Win32 does), so such a snapshot looks like a
/// normal one. Every snapshot is kept with its time; when the state turns Maximized, FullScreen or Minimized, the snapshots of the last <see cref="TransitionMs"/> are
/// dropped and the newest older one is the restore bounds. The minimized sentinel position (-32000) and a size of zero are never snapshots. The logic is moved here
/// from the PDF window so every app of the suite stores the same placement (<c>Docs/DECISIONS.md</c>, "Raffica PDF: window placement and hand-over behavior (M2.7)").
/// UI thread only; time is a monotonic millisecond clock the caller gives (tests give their own).
/// </summary>
internal sealed class WindowPlacementTracker
{
    /// <summary>How long before a change of the window state a position or size is still counted as part of that change.</summary>
    internal const long TransitionMs = 300;

    /// <summary>Positions at or below this are the sentinel of a minimized window, not a place.</summary>
    internal const double MinimizedSentinel = -30000;

    private const int MaxHistory = 16;

    private readonly List<(WindowPlacement Bounds, long At)> _history = [];
    private Func<WindowPlacement?>? _snapshot;

    /// <summary>The bounds that would be stored for the restore now, or null while none was seen.</summary>
    public WindowPlacement? RestoreBounds { get; private set; }

    /// <summary>Whether the window was last maximized (or full screen).</summary>
    public bool Maximized { get; private set; }

    /// <summary>What to store when the window closes (the restore bounds with the maximized flag), or null when there is nothing to store.</summary>
    public WindowPlacement? ToStore() => RestoreBounds is { } bounds ? bounds with { Maximized = Maximized } : null;

    /// <summary>The placement the window was given before it was shown: its bounds count as the restore bounds whatever the clock says, and it may start maximized.</summary>
    public void Start(WindowPlacement placed)
    {
        ArgumentNullException.ThrowIfNull(placed);
        NoteBounds(placed, long.MinValue / 2);
        Maximized = placed.Maximized;
    }

    /// <summary>Notes the bounds the window has in the normal state at time <paramref name="now"/> (milliseconds); a sentinel position or an empty size is ignored.</summary>
    public void NoteBounds(WindowPlacement bounds, long now)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        if (!bounds.IsUsable || bounds.X <= MinimizedSentinel || bounds.Y <= MinimizedSentinel)
        {
            return;
        }

        if (RestoreBounds is { } current && current == bounds with { Maximized = false })
        {
            return;
        }

        RestoreBounds = bounds with { Maximized = false };
        _history.Add((RestoreBounds, now));
        if (_history.Count > MaxHistory)
        {
            _history.RemoveAt(0);
        }
    }

    /// <summary>Notes a change of the window state at time <paramref name="now"/> (milliseconds).</summary>
    public void NoteState(WindowState state, long now)
    {
        if (state != WindowState.Minimized)
        {
            Maximized = state is WindowState.Maximized or WindowState.FullScreen;
        }

        if (state is WindowState.Maximized or WindowState.Minimized or WindowState.FullScreen)
        {
            // What arrived just before is part of this change: the newest older snapshot (the oldest one there is, when all are that young) is the restore bounds.
            var stable = _history.FindLastIndex(entry => now - entry.At >= TransitionMs);
            var keep = stable >= 0 ? stable : (_history.Count > 0 ? 0 : -1);
            if (keep >= 0)
            {
                RestoreBounds = _history[keep].Bounds;
                _history.RemoveRange(keep + 1, _history.Count - keep - 1);
            }
        }
        else if (_snapshot?.Invoke() is { } bounds)
        {
            NoteBounds(bounds, now);
        }
    }

    /// <summary>
    /// Follows the window: every move and resize in the normal state is a snapshot, and every change of the state is noted. Call it once, after
    /// <see cref="Start"/>; the tracker lives as long as the window.
    /// </summary>
    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        _snapshot = () => window.WindowState == WindowState.Normal && window.IsVisible
            ? new WindowPlacement(window.Position.X, window.Position.Y, window.ClientSize.Width, window.ClientSize.Height, false)
            : null;
        window.PositionChanged += (_, _) => Track();
        window.Resized += (_, _) => Track();
        window.GetObservable(Window.WindowStateProperty).Subscribe(new StateObserver(this));
    }

    private void Track()
    {
        if (_snapshot?.Invoke() is { } bounds)
        {
            NoteBounds(bounds, Environment.TickCount64);
        }
    }

    private sealed class StateObserver(WindowPlacementTracker tracker) : IObserver<WindowState>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(WindowState value) => tracker.NoteState(value, Environment.TickCount64);
    }
}
