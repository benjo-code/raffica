using Raffica.Core.Redaction;

namespace Raffica.Pdf.Redacting;

/// <summary>
/// The marks of the plan that are selected (by id, so the selection survives a change of the list), and the order the Tab key walks the marks in:
/// reading order, page by page, top to bottom, left to right. Tab never traps the focus: past the last mark (or before the first) the walk
/// answers nothing, and the key goes on to the next control. The ids are pruned against the plan whenever it changes. UI thread only.
/// </summary>
internal sealed class RedactSelection
{
    private readonly HashSet<Guid> _ids = [];

    /// <summary>Raised after the selection changed.</summary>
    public event Action? Changed;

    /// <summary>The selected ids.</summary>
    public IReadOnlySet<Guid> Ids => _ids;

    public int Count => _ids.Count;

    /// <summary>The one selected mark, or null when none or several are selected.</summary>
    public Guid? Single => _ids.Count == 1 ? _ids.First() : null;

    /// <summary>The last mark that was selected (the one the keys act on and the list reveals).</summary>
    public Guid? Primary { get; private set; }

    public bool Contains(Guid id) => _ids.Contains(id);

    /// <summary>Replaces the selection.</summary>
    public void Set(IEnumerable<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var next = new HashSet<Guid>(ids);
        if (next.SetEquals(_ids))
        {
            return;
        }

        _ids.Clear();
        _ids.UnionWith(next);
        Primary = _ids.Count == 0 ? null : (Primary is { } p && _ids.Contains(p) ? p : next.Last());
        Changed?.Invoke();
    }

    public void Select(Guid id) => Set([id]);

    public void Clear() => Set([]);

    /// <summary>Drops the ids the plan no longer has.</summary>
    public void Prune(RedactionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (_ids.Count == 0)
        {
            return;
        }

        var present = new HashSet<Guid>(plan.Marks.Count);
        foreach (var mark in plan.Marks)
        {
            present.Add(mark.Id);
        }

        Set(_ids.Where(present.Contains));
    }

    /// <summary>The marks of a plan in reading order: by page, then by the top of the first area, then by its left edge, then in the order they were made.</summary>
    public static IReadOnlyList<RedactionMark> ReadingOrder(RedactionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return [.. plan.Marks
            .Select((mark, index) => (mark, index))
            .OrderBy(x => x.mark.Page)
            .ThenBy(x => x.mark.Areas[0].Top)
            .ThenBy(x => x.mark.Areas[0].Left)
            .ThenBy(x => x.index)
            .Select(x => x.mark)];
    }

    /// <summary>
    /// The mark a Tab (<paramref name="forward"/>) or Shift+Tab walk reaches from <paramref name="current"/>; with no current mark the first (or the last).
    /// Null past the end: the key is then not used, so the focus leaves the page instead of being trapped in the marks.
    /// </summary>
    public static Guid? Next(RedactionPlan plan, Guid? current, bool forward)
    {
        var order = ReadingOrder(plan);
        if (order.Count == 0)
        {
            return null;
        }

        var at = -1;
        if (current is { } id)
        {
            for (var i = 0; i < order.Count; i++)
            {
                if (order[i].Id == id)
                {
                    at = i;
                    break;
                }
            }
        }

        var target = at < 0 ? (forward ? 0 : order.Count - 1) : (forward ? at + 1 : at - 1);
        return target >= 0 && target < order.Count ? order[target].Id : null;
    }
}
