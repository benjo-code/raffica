namespace Raffica.Write.Layout;

/// <summary>Finds the next tab stop after a position.</summary>
internal static class TabStops
{
    private const float Epsilon = 0.01f;

    /// <summary>
    /// The stop a tab at <paramref name="x"/> goes to: the nearest custom stop (stops are sorted), the implicit stop of a hanging
    /// indent (<paramref name="hangingStop"/>, NaN for none), or, after the last custom stop, the next multiple of the default interval.
    /// </summary>
    public static TabStop Next(float x, IReadOnlyList<TabStop> custom, float hangingStop, float interval)
    {
        TabStop? best = null;
        for (var i = 0; i < custom.Count; i++)
        {
            var stop = custom[i];
            if (stop.Alignment != TabAlignment.Bar && stop.Position > x + Epsilon)
            {
                best = stop;
                break;
            }
        }

        if (!float.IsNaN(hangingStop) && hangingStop > x + Epsilon && (best is null || hangingStop < best.Value.Position))
        {
            best = new TabStop(hangingStop, TabAlignment.Left, TabLeader.None);
        }

        if (best is { } found)
        {
            return found;
        }

        var step = interval > 0f && !float.IsNaN(interval) && !float.IsInfinity(interval) ? interval : 36f;
        var next = (MathF.Floor((x + Epsilon) / step) + 1f) * step;
        return new TabStop(next, TabAlignment.Left, TabLeader.None);
    }
}
