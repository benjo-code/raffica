using Layout.Common;

namespace LayoutBench;

/// <summary>The checks that must pass before any number is taken. The first failure ends the run (exit 1).</summary>
public static class Checks
{
    public static int Run(string engine, string docPath)
    {
        var root = FontSet.FindRepoRoot();
        string[] names = { nameof(FontsLoadFromSpikeFolder), nameof(ReaderSeesTheShape), nameof(IncrementalEqualsFull), nameof(HitTestRoundTrip), nameof(PageNumbers) };
        foreach (var name in names)
        {
            string? error;
            try
            {
                using var fonts = FontSet.LoadSpikeFonts(root);
                using var layouter = Engines.Create(engine, fonts);
                error = name switch
                {
                    nameof(FontsLoadFromSpikeFolder) => FontsLoadFromSpikeFolder(root, docPath),
                    nameof(ReaderSeesTheShape) => ReaderSeesTheShape(docPath),
                    nameof(IncrementalEqualsFull) => IncrementalEqualsFull(layouter, docPath),
                    nameof(HitTestRoundTrip) => HitTestRoundTrip(layouter, docPath),
                    _ => PageNumbers(layouter, docPath),
                };
            }
            catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
            if (error is not null)
            {
                Console.Error.WriteLine($"FAIL {name}: {error}");
                return 1;
            }
            Console.WriteLine($"ok   {name}");
        }
        return 0;
    }

    /// <summary>The 8 font files load, and every character of the test document maps to a glyph other than .notdef in its own font.</summary>
    public static string? FontsLoadFromSpikeFolder(string root, string docPath)
    {
        using var fonts = FontSet.LoadSpikeFonts(root);
        if (fonts.All.Count() != 8) return $"expected 8 fonts, found {fonts.All.Count()}";
        var doc = DocxReader.Read(docPath);
        var seen = new HashSet<(string, bool, bool, char)>();
        string? Check(Paragraph p)
        {
            foreach (var r in p.Runs)
            {
                var fe = fonts.Get(r.Family, r.Bold, r.Italic);
                for (int i = r.Start; i < r.Start + r.Length; i++)
                    if (seen.Add((r.Family, r.Bold, r.Italic, p.Text[i])) && !fe.HasGlyph(p.Text[i]))
                        return $"U+{(int)p.Text[i]:X4} has no glyph in {r.Family} (bold={r.Bold}, italic={r.Italic})";
            }
            return null;
        }
        foreach (var b in doc.Blocks)
        {
            if (b is Paragraph p && Check(p) is { } e1) return e1;
            if (b is Table t) foreach (var row in t.Rows) foreach (var cell in row) if (Check(cell) is { } e2) return e2;
        }
        foreach (var f in doc.Footnotes) if (Check(f.Content) is { } e3) return e3;
        return null;
    }

    public static string? ReaderSeesTheShape(string docPath)
    {
        var doc = DocxReader.Read(docPath);
        if (doc.Sections.Count != 3) return $"expected 3 sections, found {doc.Sections.Count}";
        int tables = doc.Blocks.OfType<Table>().Count();
        if (tables != 20) return $"expected 20 tables, found {tables}";
        if (doc.Footnotes.Count != 100) return $"expected 100 footnotes, found {doc.Footnotes.Count}";
        if (doc.Images.Count < 3) return $"expected at least 3 images, found {doc.Images.Count}";
        return null;
    }

    /// <summary>After each edit, a fresh layout of the edited model must give the same pages as the incremental one.</summary>
    public static string? IncrementalEqualsFull(IParagraphLayouter layouter, string docPath)
    {
        var doc = DocxReader.Read(docPath);
        using var pb = new PageBreaker(doc, layouter, PageSetup.A4);
        pb.LayoutAll();
        var rng = new Random(4242);
        var paragraphs = Enumerable.Range(0, pb.Blocks.Count).Where(i => pb.Blocks[i] is Paragraph p && p.Text.Length > 20).ToList();
        for (int k = 0; k < 20; k++)
        {
            int block;
            string text;
            if (k % 2 == 0) { block = paragraphs[rng.Next(paragraphs.Count)]; text = "x"; }
            else
            {
                // A paragraph at the top of a page, with a long insertion, to move page breaks.
                var starts = pb.PageStarts;
                block = starts[1 + rng.Next(starts.Count - 1)].BlockIndex;
                while (pb.Blocks[block] is not Paragraph) block++;
                text = string.Concat(Enumerable.Repeat("insertion ", 12 + rng.Next(40)));
            }
            var para = (Paragraph)pb.Blocks[block];
            int offset = rng.Next(para.Text.Length + 1);
            pb.InsertText(block, offset, text);

            using var fresh = new PageBreaker(pb.Snapshot(), layouter, PageSetup.A4);
            fresh.LayoutAll();
            string? diff = Compare(pb, fresh);
            if (diff is not null) return $"edit {k + 1} (block {block}, offset {offset}, {text.Length} chars): {diff}";
        }
        return null;
    }

    static string? Compare(PageBreaker a, PageBreaker b)
    {
        if (a.PageCount != b.PageCount) return $"page count {a.PageCount} against {b.PageCount}";
        for (int i = 0; i < a.PageCount; i++)
        {
            var x = a.GetPage(i);
            var y = b.GetPage(i);
            if (x.Start != y.Start) return $"page {i + 1} starts at {x.Start} against {y.Start}";
            if (!x.Items.SequenceEqual(y.Items)) return $"page {i + 1} has other items";
            if (!x.Footnotes.SequenceEqual(y.Footnotes)) return $"page {i + 1} has other footnotes";
        }
        return null;
    }

    /// <summary>For 1,000 seeded points inside text lines, Caret(HitTest(p)) lies on the same line and within one glyph advance of the point.</summary>
    public static string? HitTestRoundTrip(IParagraphLayouter layouter, string docPath)
    {
        var doc = DocxReader.Read(docPath);
        using var pb = new PageBreaker(doc, layouter, PageSetup.A4);
        pb.LayoutAll();
        var rng = new Random(4242);
        int tested = 0;
        for (int guard = 0; tested < 1000 && guard < 100_000; guard++)
        {
            int page = rng.Next(pb.PageCount);
            var info = pb.GetPage(page);
            if (info.Items.Count == 0) continue;
            var it = info.Items[rng.Next(info.Items.Count)];
            if (pb.Blocks[it.Block] is not Paragraph p) continue;
            var layout = pb.ParagraphLayoutOf(it.Block);
            var tops = pb.LineTopsOf(it.Block);
            int line = it.First + rng.Next(it.Count);
            var m = layout.Lines[line];
            if (m.Width < 5) continue;
            float xLocal = (float)(rng.NextDouble() * (m.Width - 1));
            float offsetX = LineGeometry.XOffset(m, pb.Setup.TextWidth - p.Format.LeftIndent, p.Format);
            float x = pb.Setup.MarginLeft + p.Format.LeftIndent + offsetX + xLocal;
            float y = it.Y + (tops[line] - tops[it.First]) + (tops[line + 1] - tops[line]) / 2;
            var pos = pb.HitTest(page, x, y);
            var caret = pb.Caret(pos);
            tested++;
            if (y < caret.Y - 0.01f || y > caret.Y + caret.Height + 0.01f)
                return $"point ({x:F1}, {y:F1}) on page {page + 1} gives a caret on another line ({caret.Y:F1}..{caret.Y + caret.Height:F1})";
            float size = p.Runs.Count > 0 ? p.Runs.Max(r => r.Size) : p.Format.Size;
            if (Math.Abs(caret.X - x) > size)
                return $"point ({x:F1}, {y:F1}) on page {page + 1} gives a caret {Math.Abs(caret.X - x):F1} pt away (limit {size} pt)";
        }
        return tested < 1000 ? $"only {tested} test points found" : null;
    }

    /// <summary>Physical page 3 shows "1" and the first appendix page "A-1"; the cover and the contents have no footer.</summary>
    public static string? PageNumbers(IParagraphLayouter layouter, string docPath)
    {
        var doc = DocxReader.Read(docPath);
        using var pb = new PageBreaker(doc, layouter, PageSetup.A4);
        pb.LayoutAll();
        if (PageRenderer.FooterText(pb, 0) != "" || PageRenderer.FooterText(pb, 1) != "")
            return "the cover or the contents page has a footer";
        string third = PageRenderer.FooterText(pb, 2);
        if (third != "1") return $"page 3 shows \"{third}\", expected \"1\"";
        int appendix = Enumerable.Range(0, pb.PageCount).First(i => pb.GetPage(i).Section == 2);
        string a1 = PageRenderer.FooterText(pb, appendix);
        if (a1 != "A-1") return $"the first appendix page (page {appendix + 1}) shows \"{a1}\", expected \"A-1\"";
        return null;
    }
}
