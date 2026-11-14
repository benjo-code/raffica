namespace Raffica.Write.Layout;

/// <summary>Turns one laid-out line into page items: highlight fills, glyph runs, underline and strike-through rules, and inline boxes.</summary>
internal static class LineItems
{
    private const uint Black = 0xFF000000;

    /// <summary>Adds the items of <paramref name="line"/>, whose left edge is <paramref name="x"/> and top is <paramref name="top"/> on the page.</summary>
    public static void Emit(PageBuilder page, LineBox line, float x, float top)
    {
        var baseline = top + line.Baseline;
        foreach (var run in line.Runs)
        {
            if (run.Glyphs.Length == 0 && run.Width <= 0f)
            {
                continue;
            }

            var left = x + run.X;
            var formatting = run.Run;
            var color = formatting.Color ?? Black;
            var runBaseline = baseline - run.Raise;
            var decorated = !run.IsLabel && !run.IsLeader;
            if (decorated && formatting.Highlight is { } highlight && run.Width > 0f)
            {
                var ascent = run.Face.Ascent * run.Size;
                page.Add(new FillItem(left, runBaseline - ascent, run.Width, (run.Face.Ascent + run.Face.Descent) * run.Size, highlight));
            }

            if (run.Glyphs.Length > 0)
            {
                page.Add(new GlyphRunItem(run.Face, run.Size, color, run.GlyphArray, run.XArray, left, runBaseline));
            }

            if (!decorated || run.Width <= 0f)
            {
                continue;
            }

            var thickness = Math.Max(0.5f, run.Size / 18f);
            if (formatting.Underline != UnderlineKind.None)
            {
                var y = runBaseline + (run.Size * 0.1f);
                var weight = formatting.Underline == UnderlineKind.Thick ? thickness * 2f : thickness;
                page.Add(new RuleItem(left, y, run.Width, weight, color));
                if (formatting.Underline == UnderlineKind.DoubleLine)
                {
                    page.Add(new RuleItem(left, y + (thickness * 2f), run.Width, weight, color));
                }
            }

            if (formatting.Strike)
            {
                page.Add(new RuleItem(left, runBaseline - (run.Size * 0.28f), run.Width, thickness, color));
            }
        }

        foreach (var placed in line.Boxes)
        {
            var box = placed.Box;
            var left = x + placed.X;
            var y = top + placed.Top;
            if (box.Image is { } image)
            {
                page.Add(new ImageItem(image, left, y, box.Width, box.Height));
            }
            else
            {
                page.Add(new PlaceholderItem(left, y, box.Width, box.Height));
            }
        }
    }
}
