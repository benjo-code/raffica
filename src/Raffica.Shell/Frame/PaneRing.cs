namespace Raffica.Shell.Frame;

/// <summary>A stop of the F6 ring: a named pane that is either available (open and visible) or not.</summary>
public interface IPaneItem
{
    /// <summary>The name shown in the status bar during pane navigation.</summary>
    string Name { get; }

    /// <summary>False for a closed or hidden pane; the ring skips it.</summary>
    bool IsAvailable { get; }
}

/// <summary>
/// The order of F6 pane navigation, pure logic without Avalonia types. The ring runs through the list in order and wraps around;
/// panes that are not available are skipped. Both methods return the index of the next stop, or null when no pane is available.
/// </summary>
public static class PaneRing
{
    /// <summary>The next available pane after <paramref name="current"/>; with no current pane the first available one.</summary>
    public static int? Next(IReadOnlyList<IPaneItem> panes, int? current) => Step(panes, current, 1);

    /// <summary>The previous available pane before <paramref name="current"/>; with no current pane the last available one.</summary>
    public static int? Previous(IReadOnlyList<IPaneItem> panes, int? current) => Step(panes, current, -1);

    private static int? Step(IReadOnlyList<IPaneItem> panes, int? current, int direction)
    {
        ArgumentNullException.ThrowIfNull(panes);
        var count = panes.Count;
        if (count == 0)
        {
            return null;
        }

        // Without a current pane the walk starts just outside the ring, so that the first step lands on the first (or last) pane.
        var index = current ?? (direction > 0 ? -1 : count);
        for (var step = 0; step < count; step++)
        {
            index = (((index + direction) % count) + count) % count;
            if (panes[index].IsAvailable)
            {
                return index;
            }
        }

        return null;
    }
}
