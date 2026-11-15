using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>One content item of a run, with its place: the run, the index in the run and the list the run sits in.</summary>
internal readonly record struct FlatItem(Run Run, int Index, RunContent Content, IList<Inline> Owner);

/// <summary>A simple field: its items are <c>[Start, End)</c> of the flat list.</summary>
internal sealed record SimpleFieldSpan(SimpleField Field, IList<Inline> Owner, int Start, int End);

/// <summary>The field that holds a piece: the flat items <c>[First, Last]</c>, and the simple field when it is one.</summary>
internal readonly record struct FieldHit(int First, int Last, SimpleFieldSpan? Simple);

/// <summary>
/// A paragraph's projection together with the flat list of all its run contents in document order (including contents the projection
/// does not show, such as deleted text), which is what the edit steps work on.
/// </summary>
internal sealed class ParagraphMap
{
    private readonly List<(int Begin, int End)> _fields = [];
    private readonly List<SimpleFieldSpan> _simpleFields = [];

    private ParagraphMap(Paragraph paragraph, ParagraphProjection projection)
    {
        Paragraph = paragraph;
        Projection = projection;
    }

    public Paragraph Paragraph { get; }

    public ParagraphProjection Projection { get; }

    public List<FlatItem> Items { get; } = [];

    public Dictionary<RunContent, int> IndexOf { get; } = new(ReferenceEqualityComparer.Instance);

    public IReadOnlyList<SimpleFieldSpan> SimpleFields => _simpleFields;

    public static ParagraphMap Build(Paragraph paragraph, IProjectionHost host)
    {
        var map = new ParagraphMap(paragraph, ParagraphProjection.Build(paragraph, host));
        var open = new Stack<int>();
        map.Walk(paragraph.Inlines, open);
        while (open.Count > 0)
        {
            map._fields.Add((open.Pop(), map.Items.Count - 1));
        }

        return map;
    }

    /// <summary>The inline list of an inline that holds other inlines (the same four kinds the projection walks), or null.</summary>
    public static IList<Inline>? ChildrenOf(Inline inline) => inline switch
    {
        Hyperlink link => link.Inlines,
        SimpleField field => field.Inlines,
        ContentControlInline control => control.Inlines,
        TrackedChange change => change.Inlines,
        _ => null,
    };

    /// <summary>The flat index of the first content of the run, or -1 when it has none.</summary>
    public int FirstItemOf(Run run)
    {
        for (var i = 0; i < Items.Count; i++)
        {
            if (ReferenceEquals(Items[i].Run, run))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The flat index of the content a piece stands for, or -1.</summary>
    public int FlatOf(ProjectionPiece piece)
    {
        if (piece.Content is not null && IndexOf.TryGetValue(piece.Content, out var index))
        {
            return index;
        }

        return piece.Run is null ? -1 : FirstItemOf(piece.Run);
    }

    /// <summary>The outermost field (complex or simple) that holds the flat item, or null.</summary>
    public FieldHit? FieldAt(int flat)
    {
        (int Begin, int End)? complex = null;
        foreach (var field in _fields)
        {
            if (field.Begin <= flat && flat <= field.End && (complex is null || field.Begin < complex.Value.Begin))
            {
                complex = field;
            }
        }

        SimpleFieldSpan? simple = null;
        foreach (var span in _simpleFields)
        {
            if (span.Start <= flat && flat < span.End && (simple is null || span.Start < simple.Start))
            {
                simple = span;
            }
        }

        if (complex is null && simple is null)
        {
            return null;
        }

        if (simple is not null && (complex is null || simple.Start < complex.Value.Begin))
        {
            return new FieldHit(simple.Start, simple.End - 1, simple);
        }

        return new FieldHit(complex!.Value.Begin, complex.Value.End, null);
    }

    private void Walk(IList<Inline> list, Stack<int> open)
    {
        foreach (var inline in list)
        {
            switch (inline)
            {
                case Run run:
                    for (var i = 0; i < run.Content.Count; i++)
                    {
                        var content = run.Content[i];
                        var flat = Items.Count;
                        Items.Add(new FlatItem(run, i, content, list));
                        IndexOf[content] = flat;
                        if (content is FieldChar { Kind: FieldCharKind.Begin })
                        {
                            open.Push(flat);
                        }
                        else if (content is FieldChar { Kind: FieldCharKind.End } && open.Count > 0)
                        {
                            _fields.Add((open.Pop(), flat));
                        }
                    }

                    break;
                case SimpleField field:
                    var start = Items.Count;
                    Walk(field.Inlines, open);
                    _simpleFields.Add(new SimpleFieldSpan(field, list, start, Items.Count));
                    break;
                default:
                    if (ChildrenOf(inline) is { } children)
                    {
                        Walk(children, open);
                    }

                    break;
            }
        }
    }
}
