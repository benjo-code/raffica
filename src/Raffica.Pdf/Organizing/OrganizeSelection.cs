namespace Raffica.Pdf.Organizing;

/// <summary>
/// The pages selected in the pages pane while a plan is open: a set of positions of the plan with an anchor for range selection. The set
/// belongs to the plan the session holds; the session sets it after every operation (<see cref="Set"/>), the pane changes it by the user's
/// clicks. UI thread.
/// </summary>
internal sealed class OrganizeSelection
{
    private readonly SortedSet<int> _positions = [];
    private int[]? _snapshot;

    /// <summary>Raised after the set or the anchor changed.</summary>
    public event Action? Changed;

    /// <summary>The number of positions of the plan; a position is in range when it is at least 0 and less than this.</summary>
    public int PageCount { get; private set; }

    /// <summary>The position a range selection (<see cref="Extend"/>) starts from, or -1.</summary>
    public int Anchor { get; private set; } = -1;

    /// <summary>The number of selected positions.</summary>
    public int Count => _positions.Count;

    /// <summary>The selected positions, ascending; a snapshot.</summary>
    public IReadOnlyList<int> Positions => _snapshot ??= [.. _positions];

    public bool Contains(int position) => _positions.Contains(position);

    /// <summary>Selects exactly this page and makes it the anchor.</summary>
    public void Select(int position)
    {
        CheckRange(position);
        _positions.Clear();
        _positions.Add(position);
        Anchor = position;
        Raise();
    }

    /// <summary>Selects the pages from the anchor to <paramref name="position"/> (both included) and keeps the anchor; without an anchor it selects the page.</summary>
    public void Extend(int position)
    {
        CheckRange(position);
        if (Anchor < 0)
        {
            Select(position);
            return;
        }

        _positions.Clear();
        for (var i = Math.Min(Anchor, position); i <= Math.Max(Anchor, position); i++)
        {
            _positions.Add(i);
        }

        Raise();
    }

    /// <summary>Adds the page to the selection or removes it, and makes it the anchor.</summary>
    public void Toggle(int position)
    {
        CheckRange(position);
        if (!_positions.Add(position))
        {
            _positions.Remove(position);
        }

        Anchor = position;
        Raise();
    }

    public void SelectAll()
    {
        _positions.Clear();
        for (var i = 0; i < PageCount; i++)
        {
            _positions.Add(i);
        }

        Anchor = PageCount > 0 ? 0 : -1;
        Raise();
    }

    public void Clear()
    {
        if (_positions.Count == 0 && Anchor < 0)
        {
            return;
        }

        _positions.Clear();
        Anchor = -1;
        Raise();
    }

    /// <summary>Makes the selection the given positions of a plan of <paramref name="pageCount"/> pages (the session after an operation); the anchor is the first of them.</summary>
    internal void Set(int pageCount, IEnumerable<int> positions)
    {
        PageCount = pageCount;
        _positions.Clear();
        foreach (var position in positions)
        {
            if (position >= 0 && position < pageCount)
            {
                _positions.Add(position);
            }
        }

        Anchor = _positions.Count > 0 ? _positions.Min : -1;
        Raise();
    }

    private void CheckRange(int position)
    {
        if (position < 0 || position >= PageCount)
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "The position is not in the plan.");
        }
    }

    private void Raise()
    {
        _snapshot = null;
        Changed?.Invoke();
    }
}
