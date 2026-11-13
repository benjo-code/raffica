namespace Raffica.Write.Model;

public enum TrackedChangeKind
{
    Insert,
    Delete,
    MoveFrom,
    MoveTo,
}

/// <summary>A <c>w:ins</c>, <c>w:del</c>, <c>w:moveFrom</c> or <c>w:moveTo</c> in a paragraph: a wrapper around the inline content it changed. Nothing is accepted or rejected by reading.</summary>
public sealed class TrackedChange : Inline
{
    public TrackedChange(TrackedChangeKind kind, RevisionInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        Kind = kind;
        Info = info;
    }

    public TrackedChangeKind Kind { get; }

    public RevisionInfo Info { get; }

    public IList<Inline> Inlines { get; } = new List<Inline>();
}
