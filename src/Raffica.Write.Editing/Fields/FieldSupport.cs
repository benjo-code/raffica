namespace Raffica.Write.Editing;

/// <summary>The fields Write understands and can refresh.</summary>
public enum FieldKind
{
    None,
    Seq,
    StyleRef,
    Ref,
    PageRef,
    NoteRef,
    Toc,
}

/// <summary>Whether Write supports a field instruction: a name from the spec's table, with only the switches of the table. Anything else keeps its stored result.</summary>
public sealed record FieldSupport(FieldKind Kind, bool IsSupported)
{
    public static FieldSupport Of(FieldInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        var kind = instruction.Name switch
        {
            "SEQ" => FieldKind.Seq,
            "STYLEREF" => FieldKind.StyleRef,
            "REF" => FieldKind.Ref,
            "PAGEREF" => FieldKind.PageRef,
            "NOTEREF" => FieldKind.NoteRef,
            "TOC" => FieldKind.Toc,
            _ => FieldKind.None,
        };
        if (kind == FieldKind.None)
        {
            return new FieldSupport(FieldKind.None, false);
        }

        // Every field but TOC names its target (a sequence identifier or a bookmark) as its argument.
        var supported = !instruction.HasUnknownToken && (kind == FieldKind.Toc || !string.IsNullOrEmpty(instruction.Argument));
        return new FieldSupport(kind, supported);
    }
}
