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

    /// <summary>Makes <paramref name="changes"/> the pending format (an empty list clears it).</summary>
    public void Set(IReadOnlyList<PropertyChange> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        _changes = changes.Count == 0 ? [] : [.. changes];
    }

    /// <summary>Forgets the pending format.</summary>
    public void Clear() => _changes = [];
}
