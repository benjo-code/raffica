using Avalonia.Controls;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Hosting.Tests;

/// <summary>The placement logic every app shares (moved from the tests of the PDF window): what is stored when a window closes after a maximize, a minimize or a move.</summary>
public sealed class WindowPlacementTrackerTests
{
    private static readonly WindowPlacement Normal = new(100, 80, 1100, 700, false);

    [Fact]
    public void ANewTrackerHasNothingToStore()
    {
        var tracker = new WindowPlacementTracker();
        Assert.Null(tracker.RestoreBounds);
        Assert.Null(tracker.ToStore());
        Assert.False(tracker.Maximized);
    }

    [Fact]
    public void AStartedPlacementIsTheRestoreBoundsAndMayBeMaximized()
    {
        var tracker = new WindowPlacementTracker();
        tracker.Start(Normal with { Maximized = true });

        Assert.Equal(Normal, tracker.RestoreBounds);
        Assert.Equal(Normal with { Maximized = true }, tracker.ToStore());
    }

    [Fact]
    public void APositionThatArrivesBeforeTheMaximizedStateIsNotTheRestoreBounds()
    {
        var tracker = new WindowPlacementTracker();
        var at = 100_000L;
        tracker.NoteBounds(Normal, at);

        // Win32 raises the move to the maximized corner (and the old size) before the state changes.
        tracker.NoteBounds(new WindowPlacement(-8, -8, 1100, 700, false), at + 8_990);
        tracker.NoteState(WindowState.Maximized, at + 9_000);

        Assert.Equal(Normal, tracker.RestoreBounds);
        Assert.Equal(Normal with { Maximized = true }, tracker.ToStore());
    }

    [Fact]
    public void ASizeThatArrivesBeforeTheMinimizedStateAndTheSentinelPositionAreNotKept()
    {
        var tracker = new WindowPlacementTracker();
        tracker.NoteBounds(Normal, 1_000);

        tracker.NoteBounds(new WindowPlacement(-32000, -32000, 1100, 700, false), 9_980);
        tracker.NoteBounds(new WindowPlacement(100, 80, 0, 0, false), 9_985);
        Assert.Equal(Normal, tracker.RestoreBounds);

        tracker.NoteBounds(new WindowPlacement(120, 90, 160, 28, false), 9_990);
        tracker.NoteState(WindowState.Minimized, 10_000);
        Assert.Equal(Normal, tracker.RestoreBounds);
        Assert.False(tracker.Maximized);

        // Restoring brings the normal state back, and a later real move is noted again.
        tracker.NoteState(WindowState.Normal, 12_000);
        tracker.NoteBounds(new WindowPlacement(140, 100, 1000, 650, false), 20_000);
        Assert.Equal(new WindowPlacement(140, 100, 1000, 650, false), tracker.RestoreBounds);
    }

    [Fact]
    public void AMoveThatIsLongBeforeTheMaximizeStaysTheRestoreBounds()
    {
        var tracker = new WindowPlacementTracker();
        tracker.NoteBounds(Normal, 1_000);
        tracker.NoteBounds(new WindowPlacement(200, 120, 1000, 650, false), 5_000);

        tracker.NoteState(WindowState.Maximized, 9_000);

        Assert.Equal(new WindowPlacement(200, 120, 1000, 650, false), tracker.RestoreBounds);
        Assert.True(tracker.Maximized);
    }

    [Fact]
    public void AllSnapshotsYoungerThanTheTransitionFallBackToTheOldestOne()
    {
        var tracker = new WindowPlacementTracker();
        tracker.NoteBounds(Normal, 10_000);
        tracker.NoteBounds(new WindowPlacement(0, 0, 1920, 1080, false), 10_100);

        tracker.NoteState(WindowState.FullScreen, 10_200);

        Assert.Equal(Normal, tracker.RestoreBounds);
        Assert.True(tracker.Maximized);
    }

    [Fact]
    public void LeavingMaximizedClearsTheFlagAndAnUnusableBoundsIsIgnored()
    {
        var tracker = new WindowPlacementTracker();
        tracker.NoteBounds(Normal, 1_000);
        tracker.NoteState(WindowState.Maximized, 9_000);
        tracker.NoteState(WindowState.Normal, 12_000);
        Assert.False(tracker.Maximized);

        tracker.NoteBounds(new WindowPlacement(1, 1, double.NaN, 5, false), 13_000);
        Assert.Equal(Normal, tracker.RestoreBounds);
    }
}
