namespace Raffica.Write.Layout;

/// <summary>Stretches the spaces of a line so that its content fills the room.</summary>
internal static class Justifier
{
    /// <summary>
    /// Adds the free room to the spaces of a line (not the spaces that end the text). Returns false and changes nothing when the line
    /// has no space to stretch or no free room.
    /// </summary>
    public static bool Apply(List<GlyphRun> runs, List<PlacedBox> boxes, GlyphRun? lastText, float free)
    {
        if (free <= 0f || float.IsNaN(free))
        {
            return false;
        }

        var count = 0;
        foreach (var run in runs)
        {
            count += SpacesOf(run, lastText).Length;
        }

        if (count == 0)
        {
            return false;
        }

        var extra = free / count;
        var spaceXs = boxes.Count > 0 ? new List<float>(count) : null;
        var carried = 0f;
        foreach (var run in runs)
        {
            var spaces = SpacesOf(run, lastText);
            var origin = run.X;
            if (spaces.Length == 0)
            {
                run.X = origin + carried;
                continue;
            }

            var xs = run.Xs;
            var moved = new float[xs.Length];
            var shift = 0f;
            var next = 0;
            for (var i = 0; i < xs.Length; i++)
            {
                moved[i] = xs[i] + shift;
                if (next < spaces.Length && spaces[next] == i)
                {
                    spaceXs?.Add(origin + xs[i]);
                    shift += extra;
                    next++;
                }
            }

            run.SetXs(moved);
            run.X = origin + carried;
            run.Width += shift;
            carried += shift;
        }

        if (spaceXs is not null)
        {
            foreach (var box in boxes)
            {
                var passed = 0;
                foreach (var x in spaceXs)
                {
                    if (x < box.X)
                    {
                        passed++;
                    }
                }

                box.X += passed * extra;
            }
        }

        return true;
    }

    private static ReadOnlySpan<int> SpacesOf(GlyphRun run, GlyphRun? lastText)
    {
        var spaces = run.Spaces;
        if (spaces is null || spaces.Length == 0)
        {
            return default;
        }

        if (!ReferenceEquals(run, lastText))
        {
            return spaces;
        }

        var keep = 0;
        while (keep < spaces.Length && spaces[keep] < run.TrailStart)
        {
            keep++;
        }

        return spaces.AsSpan(0, keep);
    }
}
