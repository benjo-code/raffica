namespace Raffica.Write.Layout;

/// <summary>
/// A table at one width: the column boundaries, solved once, and the rows, laid out on demand and in order. A page that holds the
/// first rows of a table with ten thousand rows lays out those rows and no others. Shared by every piece of the table, so a cut
/// inside a row re-uses the line layouts of its cells.
/// </summary>
internal sealed class TableData
{
    private const float MinimumContent = 1f;

    private readonly List<RowLayout> _rows = [];
    private readonly List<float> _tops = [0f];
    private readonly Dictionary<CellModel, CellBox> _heads = [];

    public TableData(TablePlan plan, float width)
    {
        Plan = plan;
        Model = plan.Model;
        width = float.IsFinite(width) ? Math.Clamp(width, 1f, TableLimits.MaximumLength) : 1f;
        var first = Model.Rows.Length > 0 && Model.Rows[0].Cells.Length > 0 ? Model.Rows[0].Cells[0].MarginLeft : 0f;
        var indent = Math.Clamp(Model.Indent, -TableLimits.MaximumLength, TableLimits.MaximumLength);
        var left = Math.Clamp(indent - (plan.TextAtIndent ? first : 0f), -(width / 2f) - first, width / 2f);
        var widths = GridSolver.Solve(Model, Math.Max(width - left, 1f));
        var boundaries = new float[widths.Length + 1];
        for (var c = 0; c < widths.Length; c++)
        {
            boundaries[c + 1] = boundaries[c] + widths[c];
        }

        Columns = boundaries;
        TableWidth = boundaries[^1];
        Left = Model.Alignment switch
        {
            ParagraphAlignment.Center => (width - TableWidth) / 2f,
            ParagraphAlignment.Right => width - TableWidth,
            _ => left,
        };
    }

    public TablePlan Plan { get; }

    public TableModel Model { get; }

    /// <summary>The x of each column boundary from the left edge of the table (one more than there are columns).</summary>
    public float[] Columns { get; }

    public float TableWidth { get; }

    /// <summary>Where the table's left edge is, from the left edge of the room it was laid out in.</summary>
    public float Left { get; }

    public int RowCount => Model.Rows.Length;

    /// <summary>The height of the rows that repeat on every page.</summary>
    public float HeaderHeight => Top(Model.HeaderRows);

    public RowLayout Row(int index)
    {
        while (_rows.Count <= index)
        {
            Compute(_rows.Count);
        }

        return _rows[index];
    }

    /// <summary>The top of a row from the top of the table (the bottom of the one before it); the rows above are laid out by this call.</summary>
    public float Top(int index)
    {
        if (index <= 0)
        {
            return 0f;
        }

        var last = Math.Min(index, RowCount) - 1;
        if (last >= 0)
        {
            Row(last);
        }

        return _tops[Math.Min(index, _tops.Count - 1)];
    }

    private void Compute(int index)
    {
        var model = Model.Rows[index];
        var boxes = new CellBox[model.Cells.Length];
        var need = 0f;
        for (var k = 0; k < model.Cells.Length; k++)
        {
            var cell = model.Cells[k];
            var x = Columns[cell.Start];
            var width = Columns[Math.Min(cell.Start + cell.Span, Columns.Length - 1)] - x;
            var stack = cell.IsContinuation
                ? CellStack.Empty
                : CellStack.Build(cell.Content, Math.Max(width - cell.MarginLeft - cell.MarginRight, MinimumContent));
            var box = new CellBox(cell, x, width, stack);
            boxes[k] = box;
            if (cell.IsContinuation)
            {
                continue;
            }

            if (cell.RowSpan > 1)
            {
                _heads[cell] = box;
            }
            else
            {
                need = Math.Max(need, box.Need);
            }
        }

        var height = need;
        if (model.Rule == RowHeightRule.Exact && model.Height > 0f)
        {
            height = model.Height;
        }
        else
        {
            if (model.Rule == RowHeightRule.AtLeast)
            {
                height = Math.Max(height, model.Height);
            }

            // The last row of a vertical merge grows when the content of the merged cell is taller than the rows it covers.
            foreach (var box in boxes)
            {
                if (box.Model.Head is { } head && head.Row + head.RowSpan - 1 == index && _heads.TryGetValue(head, out var headBox))
                {
                    var covered = _tops[index] - _tops[head.Row];
                    height = Math.Max(height, headBox.Need - covered);
                    _heads.Remove(head);
                }
            }
        }

        height = TableLimits.Length(height);
        _rows.Add(new RowLayout(model, index, boxes, height));
        _tops.Add(_tops[index] + height);
    }
}

/// <summary>
/// A table, or the part of one that is left after a cut, as the page filler sees it. A piece is the rows <c>[from, to)</c> with an
/// optional cut row in front (the rest of a row that did not fit) and one behind (the start of a row that did not fit), and, on a
/// continuation page, the header rows repeated above. A cut goes between rows, or inside a row at the line boundaries of its cells
/// when the row may split. Pieces are immutable and share one <see cref="TableData"/>.
/// </summary>
internal sealed class TableFlow : IBlockFlow
{
    private const float Epsilon = 0.01f;

    private readonly TableData _data;
    private readonly bool _first;
    private readonly bool _repeat;
    private readonly RowFragment? _lead;
    private readonly int _from;
    private readonly int _to;
    private readonly RowFragment? _trail;
    private float _height = float.NaN;
    private IReadOnlyList<NoteRef>? _notes;

    private TableFlow(TableData data, bool first, bool repeat, RowFragment? lead, int from, int to, RowFragment? trail)
    {
        _data = data;
        _first = first;
        _repeat = repeat;
        _lead = lead;
        _from = from;
        _to = to;
        _trail = trail;
    }

    public static IBlockFlow Create(TablePlan plan, float width)
    {
        var data = new TableData(plan, width);
        return new TableFlow(data, true, false, null, 0, data.RowCount, null);
    }

    public int BodyIndex => _data.Plan.BodyIndex;

    public bool IsFirstPiece => _first;

    public int StartLine => _from;

    // Whole rows of the same layout; a piece with a cut row has fragments of its own and is never called the same.
    public bool SamePieceAs(IBlockFlow? other) =>
        other is TableFlow flow
        && ReferenceEquals(_data, flow._data)
        && _first == flow._first
        && _repeat == flow._repeat
        && _from == flow._from
        && _to == flow._to
        && _lead is null
        && flow._lead is null
        && _trail is null
        && flow._trail is null;

    public float Height
    {
        get
        {
            if (float.IsNaN(_height))
            {
                var height = (_repeat ? _data.HeaderHeight : 0f) + (_lead?.Height ?? 0f) + (_data.Top(_to) - _data.Top(_from)) + (_trail?.Height ?? 0f);
                _height = TableLimits.Length(height);
            }

            return _height;
        }
    }

    public float SpaceBefore => 0f;

    public float SpaceAfter => 0f;

    public bool KeepNext => false;

    public bool PageBreakBefore => false;

    public string? ContextualGroup => null;

    public float MinimumHead
    {
        get
        {
            var count = Math.Min(UnitCount, Protected + 1);
            var height = _repeat ? _data.HeaderHeight : 0f;
            for (var k = 0; k < count; k++)
            {
                height += UnitHeight(k);
            }

            return height;
        }
    }

    public IReadOnlyList<NoteRef> Notes => _notes ??= FindNotes();

    public BreakKind? EndBreak => null;

    public IBlockFlow? AfterBreak(float width) => null;

    // The pieces of the piece in order: the cut row in front, whole rows, the cut row behind.
    private int LeadCount => _lead is null ? 0 : 1;

    private int UnitCount => LeadCount + (_to - _from) + (_trail is null ? 0 : 1);

    // The header rows of the first piece stay together with the first row after them: a cut may not leave only the header behind.
    private int Protected => _first && _lead is null && _from == 0 ? Math.Min(_data.Model.HeaderRows, _to - _from) : 0;

    private int UnitRow(int k)
    {
        if (k < LeadCount)
        {
            return _lead!.Row.Index;
        }

        var whole = k - LeadCount;
        return whole < _to - _from ? _from + whole : _trail!.Row.Index;
    }

    private float UnitHeight(int k)
    {
        if (k < LeadCount)
        {
            return _lead!.Height;
        }

        var whole = k - LeadCount;
        return whole < _to - _from ? _data.Row(_from + whole).Height : _trail!.Height;
    }

    private RowFragment FragmentOf(int k)
    {
        if (k < LeadCount)
        {
            return _lead!;
        }

        var whole = k - LeadCount;
        return whole < _to - _from ? _data.Row(_from + whole).Whole : _trail!;
    }

    private bool CanSplitInside(int k)
    {
        var row = _data.Model.Rows[UnitRow(k)];
        return !row.CantSplit && row.Rule != RowHeightRule.Exact && !row.InMerge;
    }

    // A page may break before unit `at` unless a vertical merge runs across the line.
    private bool CutAllowed(int at)
    {
        var before = UnitRow(at - 1);
        var after = UnitRow(at);
        return !(after == before + 1 && _data.Model.Rows[after].NoCutBefore);
    }

    public FlowSplit Split(float available, bool mustProgress)
    {
        var total = UnitCount;
        if (total == 0)
        {
            return new FlowSplit(this, null);
        }

        var y = _repeat ? _data.HeaderHeight : 0f;
        var k = 0;
        for (; k < total; k++)
        {
            var height = UnitHeight(k);
            if (y + height > available + Epsilon)
            {
                break;
            }

            y += height;
        }

        if (k == total)
        {
            return new FlowSplit(this, null);
        }

        var guarded = Protected;
        var room = available - y;

        // Inside the row that does not fit: each cell gives what fits at its line boundaries.
        if (k >= guarded && CanSplitInside(k))
        {
            var forced = mustProgress && k <= guarded;
            if (FragmentOf(k).Split(room, forced) is { } cut)
            {
                return Cut(k, cut.Head, cut.Tail, available, mustProgress);
            }
        }

        // Between rows, as late as a merge allows; the header never stays alone.
        var at = k;
        while (at > guarded && !CutAllowed(at))
        {
            at--;
        }

        if (at > guarded)
        {
            return Cut(at, null, null, available, mustProgress);
        }

        if (!mustProgress)
        {
            return new FlowSplit(null, this);
        }

        // At the top of an empty page the row that does not fit stays whole (it overflows) so that the layout moves on.
        var end = k + 1;
        return end >= total ? new FlowSplit(this, null) : Cut(end, null, null, available, mustProgress);
    }

    private FlowSplit Cut(int at, RowFragment? headEnd, RowFragment? tailStart, float available, bool mustProgress)
    {
        var total = UnitCount;
        var inRow = headEnd is not null;
        var header = _data.Model.HeaderRows;
        var headerHeight = header > 0 ? _data.HeaderHeight : 0f;
        var repeat = header > 0
            && (_repeat || (_first && _lead is null && _from == 0 && at >= header))
            && headerHeight <= available
            && (!mustProgress || headerHeight * 2f <= available);
        var head = Slice(0, at, null, headEnd, _first, _repeat);
        var tail = Slice(inRow ? at + 1 : at, total, tailStart, null, false, repeat);
        return new FlowSplit(head, tail);
    }

    private TableFlow Slice(int start, int end, RowFragment? lead, RowFragment? trail, bool first, bool repeat)
    {
        var leadCount = LeadCount;
        var whole = _to - _from;
        var total = UnitCount;
        var from = _from + Math.Clamp(start - leadCount, 0, whole);
        var to = Math.Max(_from + Math.Clamp(end - leadCount, 0, whole), from);
        lead ??= start == 0 && end > 0 ? _lead : null;
        trail ??= end >= total && start < total ? _trail : null;
        return new TableFlow(_data, first, repeat, lead, from, to, trail);
    }

    public void Emit(PageBuilder page, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(page);
        var runs = new List<RowRun>();
        var at = y;
        if (_repeat)
        {
            for (var r = 0; r < _data.Model.HeaderRows; r++)
            {
                var row = _data.Row(r);
                runs.Add(new RowRun(row, row.Whole.Stacks, at, row.Height, true));
                at += row.Height;
            }
        }

        if (_lead is { } lead)
        {
            runs.Add(new RowRun(lead.Row, lead.Stacks, at, lead.Height, false));
            at += lead.Height;
        }

        for (var r = _from; r < _to; r++)
        {
            var row = _data.Row(r);
            runs.Add(new RowRun(row, row.Whole.Stacks, at, row.Height, true));
            at += row.Height;
        }

        if (_trail is { } trail)
        {
            runs.Add(new RowRun(trail.Row, trail.Stacks, at, trail.Height, false));
        }

        TableDrawing.Emit(page, x + _data.Left, runs, _data.Columns);
    }

    // The references of the cells in this piece (a repeated header row shows its text again, but its notes belong to the first page).
    private List<NoteRef> FindNotes()
    {
        List<NoteRef>? found = null;
        void Add(IEnumerable<CellStack?> stacks)
        {
            foreach (var stack in stacks)
            {
                if (stack is { Notes.Count: > 0 })
                {
                    (found ??= []).AddRange(stack.Notes);
                }
            }
        }

        if (_lead is not null)
        {
            Add(_lead.Stacks);
        }

        for (var r = _from; r < _to; r++)
        {
            Add(_data.Row(r).Whole.Stacks);
        }

        if (_trail is not null)
        {
            Add(_trail.Stacks);
        }

        return found ?? [];
    }
}
