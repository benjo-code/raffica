using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>A field of a paragraph: its parsed instruction, whether Write supports it, whether it is closed, whether it sits in a tracked change, and the field it is nested in.</summary>
public sealed record FieldSpan(FieldInstruction Instruction, FieldSupport Support, bool IsComplete, bool InTrackedChange)
{
    /// <summary>The field this one is nested in, or null.</summary>
    public FieldSpan? Parent { get; init; }

    /// <summary>The scanner's view of the runs (the same objects as in the paragraph).</summary>
    internal FieldInfo? Info { get; init; }
}

/// <summary>The fields of a paragraph as typed spans, over <see cref="FieldScanner"/> (the one field grammar): complex and simple fields, nested ones included, in document order.</summary>
public static class FieldView
{
    public static IReadOnlyList<FieldSpan> InParagraph(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        var infos = FieldScanner.Scan(paragraph.Inlines, intoContentControls: true);
        var spans = new List<FieldSpan>(infos.Count);
        var byInfo = new Dictionary<FieldInfo, FieldSpan>(ReferenceEqualityComparer.Instance);
        foreach (var info in infos)
        {
            var instruction = FieldInstruction.Parse(info.Instruction);
            var span = new FieldSpan(instruction, FieldSupport.Of(instruction), info.IsComplete, info.InTrackedChange)
            {
                Parent = info.Parent is not null && byInfo.TryGetValue(info.Parent, out var parent) ? parent : null,
                Info = info,
            };
            byInfo[info] = span;
            spans.Add(span);
        }

        return spans;
    }

    /// <summary>True when any paragraph of the body (tables and block content controls included) holds a field: a cheap scan that parses nothing. A document with none needs no refresh.</summary>
    public static bool AnyIn(WordDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return AnyIn(document.Body.Blocks);
    }

    /// <summary>True when any paragraph of the blocks (tables and block content controls entered) holds a field.</summary>
    public static bool AnyIn(IEnumerable<Block> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        return BookmarkOperations.ParagraphsOf(blocks).Any(FieldRefresh.HasFieldMarkup);
    }

    /// <summary>
    /// True when the range <paramref name="start"/> to <paramref name="end"/> (offsets of the projection the <paramref name="host"/> makes, a caret has both equal) touches the result of a top-level field that Write does not update: a name or switch outside the
    /// support table, or a field in a tracked change. The result of a field is atomic, so a caret is never inside one: it touches the field at either edge. False when the fields and their results cannot be paired (the number of top-level fields and
    /// of field results differs), never a guess.
    /// </summary>
    public static bool IsNotUpdatedAt(Paragraph paragraph, IProjectionHost host, int start, int end)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(host);
        if (!FieldRefresh.HasFieldMarkup(paragraph))
        {
            return false;
        }

        var spans = InParagraph(paragraph).Where(s => s.Parent is null).ToList();
        var results = ParagraphProjection.Build(paragraph, host).Pieces.OfType<AtomicPiece>().Where(p => p.Atomic == AtomicKind.FieldResult).ToList();
        if (spans.Count != results.Count)
        {
            return false;
        }

        for (var i = 0; i < results.Count; i++)
        {
            var touches = start == end ? start >= results[i].Start && start <= results[i].End : start < results[i].End && end > results[i].Start;
            if (touches)
            {
                return !spans[i].Support.IsSupported || spans[i].InTrackedChange;
            }
        }

        return false;
    }
}
