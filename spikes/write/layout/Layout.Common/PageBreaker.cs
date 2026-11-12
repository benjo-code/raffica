namespace Layout.Common;

/// <summary>An item placed on a page: lines (paragraph) or rows (table) of one block, starting at <see cref="Y"/> (for a repeated table header: at the top of the header).</summary>
public readonly record struct Placed(int Block, int First, int Count, float Y, bool RepeatHeader);

public sealed class PageInfo
{
    public PageStart Start { get; init; }
    public int Section { get; init; }
    public List<Placed> Items { get; } = new();
    public List<int> Footnotes { get; } = new();
    public float FootnoteHeight { get; set; }
}

/// <summary>
/// Own page breaking, written once for both layout candidates: A4 pages, header and footer per section, a table breaks between rows (header
/// rows repeat), a footnote sits at the bottom of the page whose line holds its reference (its space is reserved when that line is placed),
/// widow and orphan control is off, a heading keeps with the next block. Every paragraph keeps its <see cref="IParagraphLayout"/>; an edit
/// re-lays out only that paragraph, then refills pages forward until a page starts where it started before the edit.
/// </summary>
public sealed class PageBreaker : IDisposable
{
    public const float FootnoteSeparator = 12f;
    const float CellPaddingX = 5.4f, CellPaddingY = 1f;

    readonly IParagraphLayouter _layouter;
    readonly PageSetup _setup;
    readonly DocModel _doc;
    readonly List<Block> _blocks;
    readonly BlockLayout?[] _layouts;
    readonly int[] _sectionOf;
    readonly HashSet<int> _sectionStarts = new();
    readonly Dictionary<int, Footnote> _footnotes;
    readonly Dictionary<int, FootnoteLayout> _footnoteLayouts = new();
    readonly List<PageInfo> _pages = new();
    PageStart _next = new(0, 0);
    bool _complete;
    List<PageStart>? _startsCache;

    public PageBreaker(DocModel doc, IParagraphLayouter layouter, PageSetup setup)
    {
        _doc = doc; _layouter = layouter; _setup = setup;
        _blocks = new List<Block>(doc.Blocks);
        _layouts = new BlockLayout?[_blocks.Count];
        _footnotes = doc.Footnotes.ToDictionary(f => f.Id);
        _sectionOf = new int[Math.Max(1, _blocks.Count)];
        for (int s = 0; s < doc.Sections.Count; s++)
        {
            int from = doc.Sections[s].FirstBlock;
            int to = s + 1 < doc.Sections.Count ? doc.Sections[s + 1].FirstBlock : _blocks.Count;
            for (int b = from; b < to && b < _sectionOf.Length; b++) _sectionOf[b] = s;
            if (s > 0) _sectionStarts.Add(from);
        }
    }

    public IParagraphLayouter Layouter => _layouter;
    public DocModel Doc => _doc;
    public PageSetup Setup => _setup;
    public IReadOnlyList<Block> Blocks => _blocks;
    public int PageCount => _pages.Count;
    public bool IsComplete => _complete;
    public PageInfo GetPage(int page) => _pages[page];

    /// <summary>The current model, with every edit applied.</summary>
    public DocModel Snapshot() => new(_doc.Sections, _blocks.ToList(), _doc.Footnotes, _doc.Images);

    public IReadOnlyList<PageStart> PageStarts => _startsCache ??= _pages.Select(p => p.Start).ToList();

    public int LayoutUntil(int pageIndex)
    {
        while (!_complete && _pages.Count <= pageIndex) AppendPage();
        return _pages.Count;
    }

    public int LayoutAll()
    {
        while (!_complete) AppendPage();
        return _pages.Count;
    }

    void AppendPage()
    {
        var page = FillPage(_next, out var next);
        _pages.Add(page);
        _next = next;
        _startsCache = null;
    }

    // ---- block layouts ----------------------------------------------------------------------------------------------

    abstract class BlockLayout : IDisposable { public abstract void Dispose(); }

    sealed class ParaLayout : BlockLayout
    {
        public IParagraphLayout Layout = null!;
        public float[] Tops = null!;       // cumulative line tops, last = paragraph height
        public int[] RefLine = null!;      // line of each footnote reference
        public float LineH(int i) => Tops[i + 1] - Tops[i];
        public int LineCount => Tops.Length - 1;
        public override void Dispose() => Layout.Dispose();
    }

    sealed class TableLayout : BlockLayout
    {
        public IParagraphLayout[][] Cells = null!;
        public float[] RowTops = null!;    // cumulative row tops
        public float[] ColumnX = null!;
        public float[] ColumnW = null!;
        public float RowH(int r) => RowTops[r + 1] - RowTops[r];
        public override void Dispose() { foreach (var row in Cells) foreach (var c in row) c.Dispose(); }
    }

    sealed class ImageLayout : BlockLayout { public float Height; public override void Dispose() { } }

    sealed class FootnoteLayout
    {
        public IParagraphLayout Layout = null!;
        public float Height;
    }

    BlockLayout Ensure(int index)
    {
        if (_layouts[index] is { } l) return l;
        BlockLayout made;
        switch (_blocks[index])
        {
            case Paragraph p:
                made = MakeParagraph(p, _setup.TextWidth - p.Format.LeftIndent);
                break;
            case Table t:
                made = MakeTable(t);
                break;
            case ImageBlock im:
                made = new ImageLayout { Height = im.Height + 6f };
                break;
            default: throw new NotSupportedException();
        }
        return _layouts[index] = made;
    }

    ParaLayout MakeParagraph(Paragraph p, float width)
    {
        var layout = _layouter.Layout(p, width);
        var lines = layout.Lines;
        var refLine = new int[p.FootnoteRefs.Count];
        for (int r = 0; r < refLine.Length; r++)
        {
            int off = p.FootnoteRefs[r].Offset, line = lines.Count - 1;
            for (int i = 0; i < lines.Count; i++)
                if (off < lines[i].Start + lines[i].Length || i == lines.Count - 1) { line = i; break; }
            refLine[r] = line;
        }
        return new ParaLayout { Layout = layout, Tops = LineGeometry.Tops(lines, p.Format), RefLine = refLine };
    }

    TableLayout MakeTable(Table t)
    {
        int rows = t.Rows.Count;
        var widths = t.ColumnWidths.ToArray();
        var colX = new float[widths.Length + 1];
        for (int c = 0; c < widths.Length; c++) colX[c + 1] = colX[c] + widths[c];
        var cells = new IParagraphLayout[rows][];
        var tops = new float[rows + 1];
        for (int r = 0; r < rows; r++)
        {
            var row = t.Rows[r];
            cells[r] = new IParagraphLayout[row.Count];
            float h = 0;
            for (int c = 0; c < row.Count; c++)
            {
                cells[r][c] = _layouter.Layout(row[c], Math.Max(1f, widths[c] - 2 * CellPaddingX));
                h = Math.Max(h, LineGeometry.Tops(cells[r][c].Lines, row[c].Format)[^1]);
            }
            tops[r + 1] = tops[r] + h + 2 * CellPaddingY;
        }
        return new TableLayout { Cells = cells, RowTops = tops, ColumnX = colX, ColumnW = widths };
    }

    FootnoteLayout GetFootnote(int id)
    {
        if (_footnoteLayouts.TryGetValue(id, out var f)) return f;
        var fn = _footnotes[id];
        var layout = _layouter.Layout(fn.Content, _setup.TextWidth);
        return _footnoteLayouts[id] = new FootnoteLayout { Layout = layout, Height = LineGeometry.Tops(layout.Lines, fn.Content.Format)[^1] };
    }

    // ---- filling a page -----------------------------------------------------------------------------------------------

    bool StartsNewPage(int block) =>
        _sectionStarts.Contains(block) || (_blocks[block] is Paragraph p && p.Format.PageBreakBefore);

    PageInfo FillPage(PageStart start, out PageStart next)
    {
        var page = new PageInfo { Start = start, Section = start.BlockIndex < _sectionOf.Length ? _sectionOf[start.BlockIndex] : _sectionOf[^1] };
        float bottom = _setup.Height - _setup.MarginBottom;
        float y = _setup.MarginTop;
        int block = start.BlockIndex, line = start.LineIndex;
        next = default;

        while (true)
        {
            if (block >= _blocks.Count) { next = new PageStart(_blocks.Count, 0); _complete = true; break; }
            bool pageEmpty = page.Items.Count == 0;
            if (!pageEmpty && line == 0 && StartsNewPage(block)) { next = new PageStart(block, 0); break; }
            var layout = Ensure(block);

            if (_blocks[block] is Paragraph p)
            {
                var pl = (ParaLayout)layout;
                var f = p.Format;
                int n = pl.LineCount;
                if (line == 0 && f.KeepNext && !pageEmpty && !FitsWithNext(block, y, bottom)) { next = new PageStart(block, 0); break; }
                float before = line == 0 && !pageEmpty ? f.SpaceBefore : 0;
                float yy = y + before;
                float fh = page.FootnoteHeight;
                int i = line;
                for (; i < n; i++)
                {
                    float h = pl.LineH(i);
                    float extra = 0;
                    List<int>? ids = null;
                    for (int r = 0; r < pl.RefLine.Length; r++)
                        if (pl.RefLine[r] == i)
                        {
                            int id = p.FootnoteRefs[r].FootnoteId;
                            (ids ??= new()).Add(id);
                            extra += GetFootnote(id).Height;
                        }
                    float newFh = fh + extra + (fh == 0 && extra > 0 ? FootnoteSeparator : 0);
                    bool anyPlaced = !pageEmpty || i > line;
                    if (yy + h + newFh > bottom && anyPlaced) break;
                    yy += h;
                    fh = newFh;
                    if (ids is not null) page.Footnotes.AddRange(ids);
                }
                if (i == line) { next = new PageStart(block, line); break; }
                page.Items.Add(new Placed(block, line, i - line, y + before, false));
                page.FootnoteHeight = fh;
                if (i < n) { next = new PageStart(block, i); break; }
                y = yy + f.SpaceAfter;
                block++; line = 0;
                if (f.PageBreakAfter) { next = new PageStart(block, 0); if (block >= _blocks.Count) { _complete = true; } break; }
            }
            else if (_blocks[block] is Table t)
            {
                var tl = (TableLayout)layout;
                int rows = t.Rows.Count, hdr = t.HeaderRows;
                bool repeat = hdr > 0 && line >= hdr;
                float yy = y + (repeat ? tl.RowTops[hdr] : 0);
                int i = line;
                for (; i < rows; i++)
                {
                    float h = tl.RowH(i);
                    bool anyPlaced = !pageEmpty || i > line;
                    if (yy + h > bottom && anyPlaced) break;
                    yy += h;
                }
                // Never leave the header rows alone at the bottom of a page: move the whole table.
                if (line == 0 && hdr > 0 && i == hdr && i < rows && !pageEmpty) { next = new PageStart(block, 0); break; }
                if (i == line) { next = new PageStart(block, line); break; }
                page.Items.Add(new Placed(block, line, i - line, y, repeat));
                y = yy;
                if (i < rows) { next = new PageStart(block, i); break; }
                block++; line = 0;
            }
            else
            {
                float h = ((ImageLayout)layout).Height;
                if (y + h > bottom && !pageEmpty) { next = new PageStart(block, 0); break; }
                page.Items.Add(new Placed(block, 0, 1, y, false));
                y += h;
                block++; line = 0;
            }
        }
        return page;
    }

    /// <summary>Does the chain of keep-with-next paragraphs starting at <paramref name="block"/> fit with the first piece of what follows?</summary>
    bool FitsWithNext(int block, float y, float bottom)
    {
        float need = 0;
        int b = block;
        for (int guard = 0; guard < 6 && b < _blocks.Count; guard++)
        {
            if (_blocks[b] is Paragraph p && p.Format.KeepNext)
            {
                var pl = (ParaLayout)Ensure(b);
                need += (b == block ? p.Format.SpaceBefore : 0) + pl.Tops[^1] + p.Format.SpaceAfter;
                b++;
                continue;
            }
            break;
        }
        if (b < _blocks.Count)
        {
            var layout = Ensure(b);
            need += _blocks[b] switch
            {
                Paragraph p => p.Format.SpaceBefore + ((ParaLayout)layout).LineH(0),
                Table t => ((TableLayout)layout).RowTops[Math.Min(t.Rows.Count, t.HeaderRows + 1)],
                _ => ((ImageLayout)layout).Height,
            };
        }
        return y + need <= bottom;
    }

    // ---- editing ----------------------------------------------------------------------------------------------------------

    /// <summary>Inserts text in a paragraph block and re-lays out incrementally: only the paragraph, then the pages forward until the page starts agree again.</summary>
    public void InsertText(int blockIndex, int offset, string text)
    {
        var p = (Paragraph)_blocks[blockIndex];
        var runs = p.Runs.ToList();
        if (runs.Count == 0)
            runs.Add(new TextRun(0, 0, p.Format.Family, p.Format.Size, false, false));
        int ri = runs.FindIndex(r => offset >= r.Start && offset <= r.Start + r.Length);
        if (ri < 0) ri = runs.Count - 1;
        runs[ri] = runs[ri] with { Length = runs[ri].Length + text.Length };
        for (int i = ri + 1; i < runs.Count; i++) runs[i] = runs[i] with { Start = runs[i].Start + text.Length };
        var refs = p.FootnoteRefs.Select(r => r.Offset >= offset ? r with { Offset = r.Offset + text.Length } : r).ToList();
        var edited = p with { Text = p.Text.Insert(offset, text), Runs = runs, FootnoteRefs = refs };

        _blocks[blockIndex] = edited;
        _layouts[blockIndex]?.Dispose();
        _layouts[blockIndex] = null;

        bool laidOut = _pages.Count > 0 && (_complete || blockIndex < _next.BlockIndex || (blockIndex == _next.BlockIndex && _next.LineIndex > 0));
        if (!laidOut) return;
        Ensure(blockIndex);
        Refill(blockIndex);
    }

    static int Compare(PageStart a, PageStart b) => a.BlockIndex != b.BlockIndex ? a.BlockIndex.CompareTo(b.BlockIndex) : a.LineIndex.CompareTo(b.LineIndex);

    void Refill(int editedBlock)
    {
        int startBlock = Math.Max(0, editedBlock - 1);
        while (startBlock > 0 && _blocks[startBlock - 1] is Paragraph { Format.KeepNext: true }) startBlock--;
        int p = 0;
        for (int i = _pages.Count - 1; i >= 0; i--)
            if (Compare(_pages[i].Start, new PageStart(startBlock, 0)) <= 0) { p = i; break; }

        var tail = _pages.GetRange(p, _pages.Count - p);
        var oldNext = _next;
        bool oldComplete = _complete;
        _pages.RemoveRange(p, _pages.Count - p);
        _complete = false;
        _startsCache = null;

        var cur = tail[0].Start;
        while (true)
        {
            var page = FillPage(cur, out var next);
            _pages.Add(page);
            _next = next;
            if (_complete) break;
            if (next.BlockIndex > editedBlock)
            {
                int q = tail.FindIndex(t => t.Start == next);
                if (q >= 0)
                {
                    _pages.AddRange(tail.Skip(q));
                    _next = oldNext;
                    _complete = oldComplete;
                    break;
                }
                if (!oldComplete && next == oldNext) { _next = oldNext; break; }
            }
            cur = next;
        }
        _startsCache = null;
    }

    // ---- positions --------------------------------------------------------------------------------------------------------------

    public int PageNumber(int page)
    {
        int s = _pages[page].Section;
        int first = page;
        while (first > 0 && _pages[first - 1].Section == s) first--;
        int index = page - first;
        var sec = _doc.Sections[s];
        if (sec.PageNumberStart is { } start) return start + index;
        if (first == 0) return 1 + index;
        return PageNumber(first - 1) + 1 + index;
    }

    /// <summary>The footer text of a page ("" when the section has no footer), for example "1" or "A-1".</summary>
    public string FooterText(int page)
    {
        var sec = _doc.Sections[_pages[page].Section];
        return sec.HasFooter ? sec.FooterPrefix + PageNumber(page) : "";
    }

    public TextPosition HitTest(int page, float x, float y)
    {
        var info = _pages[page];
        if (info.Items.Count == 0) return new TextPosition(info.Start.BlockIndex, 0);
        Placed best = info.Items[0];
        foreach (var it in info.Items)
        {
            if (y >= it.Y) best = it;
            else break;
        }
        if (_blocks[best.Block] is not Paragraph p) return new TextPosition(best.Block, 0);
        var pl = (ParaLayout)Ensure(best.Block);
        float yLocal = Math.Clamp(pl.Tops[best.First] + (y - best.Y), pl.Tops[best.First], pl.Tops[best.First + best.Count] - 0.01f);
        int offset = pl.Layout.HitTest(x - _setup.MarginLeft - p.Format.LeftIndent, yLocal);
        return new TextPosition(best.Block, offset);
    }

    public RectF Caret(TextPosition position)
    {
        var p = (Paragraph)_blocks[position.Block];
        var pl = (ParaLayout)Ensure(position.Block);
        var lines = pl.Layout.Lines;
        int line = lines.Count - 1;
        for (int i = 0; i < lines.Count; i++)
            if (position.Offset < lines[i].Start + lines[i].Length) { line = i; break; }
        var target = new PageStart(position.Block, line);
        while (!_complete && Compare(_next, target) <= 0) AppendPage();
        int page = 0;
        for (int i = _pages.Count - 1; i >= 0; i--)
            if (Compare(_pages[i].Start, target) <= 0) { page = i; break; }
        foreach (var it in _pages[page].Items)
            if (it.Block == position.Block && line >= it.First && line < it.First + it.Count)
            {
                var r = pl.Layout.Caret(position.Offset);
                return new RectF(_setup.MarginLeft + p.Format.LeftIndent + r.X, it.Y + (r.Y - pl.Tops[it.First]), r.Width, r.Height);
            }
        throw new InvalidOperationException("position is not on the laid-out page");
    }

    /// <summary>The page index that holds the first line of a paragraph block (lays out as far as needed).</summary>
    public int PageOfBlock(int block)
    {
        var target = new PageStart(block, 0);
        while (!_complete && Compare(_next, target) <= 0) AppendPage();
        int page = 0;
        for (int i = _pages.Count - 1; i >= 0; i--)
            if (Compare(_pages[i].Start, target) <= 0) { page = i; break; }
        return page;
    }

    // ---- access for the renderer --------------------------------------------------------------------------------------------

    public IParagraphLayout ParagraphLayoutOf(int block) => ((ParaLayout)Ensure(block)).Layout;
    public float[] LineTopsOf(int block) => ((ParaLayout)Ensure(block)).Tops;
    internal (IParagraphLayout[][] Cells, float[] RowTops, float[] ColumnX, float[] ColumnW) TableOf(int block)
    {
        var t = (TableLayout)Ensure(block);
        return (t.Cells, t.RowTops, t.ColumnX, t.ColumnW);
    }
    internal (IParagraphLayout Layout, float Height) FootnoteOf(int id)
    {
        var f = GetFootnote(id);
        return (f.Layout, f.Height);
    }

    public void Dispose()
    {
        foreach (var l in _layouts) l?.Dispose();
        foreach (var f in _footnoteLayouts.Values) f.Layout.Dispose();
    }
}
