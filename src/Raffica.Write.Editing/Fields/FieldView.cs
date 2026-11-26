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
        var infos = FieldScanner.Scan(paragraph.Inlines);
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
}
