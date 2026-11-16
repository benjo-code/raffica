namespace Raffica.Write.Editing;

/// <summary>
/// The character format that the next typed text takes when a character command was given at a caret without a selection. It lives in the session only: it never enters
/// the model, the history or the revision (like the input method preedit). The session clears it on a real selection change, on every edit that is not typed text and on Esc.
/// </summary>
internal sealed class PendingFormat
{
    private IReadOnlyList<PropertyChange> _changes = [];

    /// <summary>The changes the next typed text takes; empty when none is pending.</summary>
    public IReadOnlyList<PropertyChange> Changes => _changes;

    /// <summary>
    /// Raised on the thread that changed it, after a real change only (a set or clear that leaves the same changes in place raises nothing). A pending-only command (Ctrl+B at a caret)
    /// changes no document, selection or revision, so this is how the ribbon learns that what typing there would give has changed.
    /// </summary>
    public event Action? Changed;

    /// <summary>Makes <paramref name="changes"/> the pending format (an empty list clears it).</summary>
    public void Set(IReadOnlyList<PropertyChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var next = changes.Count == 0 ? [] : (IReadOnlyList<PropertyChange>)[.. changes];
        var same = Same(_changes, next);
        _changes = next;
        if (!same)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Forgets the pending format.</summary>
    public void Clear()
    {
        if (_changes.Count > 0)
        {
            Set([]);
        }
    }

    private static bool Same(IReadOnlyList<PropertyChange> a, IReadOnlyList<PropertyChange> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            var left = a[i];
            var right = b[i];
            if (left.Name != right.Name || (left.Value is null) != (right.Value is null) || (left.Value is not null && !System.Xml.Linq.XNode.DeepEquals(left.Value, right.Value)))
            {
                return false;
            }
        }

        return true;
    }
}
