using System.Text;

namespace Raffica.Write.Model;

/// <summary>A field found in a sequence of inlines. A view: the fields are not a second structure, the runs stay as they are.</summary>
public sealed class FieldInfo
{
    internal FieldInfo(FieldInfo? parent)
    {
        Parent = parent;
    }

    /// <summary>The instruction text, trimmed.</summary>
    public string Instruction { get; internal set; } = string.Empty;

    /// <summary>The field this one is nested in (in its instruction or its result), or null.</summary>
    public FieldInfo? Parent { get; }

    /// <summary>False when the sequence ended before the field's end character.</summary>
    public bool IsComplete { get; internal set; }
}

public static class FieldScanner
{
    /// <summary>Finds the complex (begin, instruction, separate, result, end) and simple fields in document order, nested ones included. A field that is not closed is reported with <c>IsComplete == false</c>; nothing throws.</summary>
    public static IReadOnlyList<FieldInfo> Scan(IEnumerable<Inline> inlines)
    {
        ArgumentNullException.ThrowIfNull(inlines);
        var state = new ScanState();
        Walk(inlines, state);
        foreach (var open in state.Open)
        {
            open.Field.Instruction = open.Text.ToString().Trim();
        }

        return state.Result;
    }

    private sealed class OpenField(FieldInfo field)
    {
        public FieldInfo Field { get; } = field;

        public StringBuilder Text { get; } = new();

        public bool InResult { get; set; }
    }

    private sealed class ScanState
    {
        public List<FieldInfo> Result { get; } = [];

        public List<OpenField> Open { get; } = [];
    }

    private static void Walk(IEnumerable<Inline> inlines, ScanState state)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    foreach (var content in run.Content)
                    {
                        Content(content, state);
                    }

                    break;
                case SimpleField simple:
                    var field = new FieldInfo(state.Open.Count > 0 ? state.Open[^1].Field : null)
                    {
                        Instruction = simple.Instruction.Trim(),
                        IsComplete = true,
                    };
                    state.Result.Add(field);
                    var holder = new OpenField(field) { InResult = true };
                    state.Open.Add(holder);
                    Walk(simple.Inlines, state);
                    state.Open.Remove(holder);
                    break;
                case Hyperlink link:
                    Walk(link.Inlines, state);
                    break;
                case TrackedChange change:
                    Walk(change.Inlines, state);
                    break;
            }
        }
    }

    private static void Content(RunContent content, ScanState state)
    {
        switch (content)
        {
            case FieldChar { Kind: FieldCharKind.Begin }:
                var field = new FieldInfo(state.Open.Count > 0 ? state.Open[^1].Field : null);
                state.Result.Add(field);
                state.Open.Add(new OpenField(field));
                break;
            case FieldChar { Kind: FieldCharKind.Separate } when state.Open.Count > 0:
                state.Open[^1].InResult = true;
                break;
            case FieldChar { Kind: FieldCharKind.End } when state.Open.Count > 0:
                var closed = state.Open[^1];
                closed.Field.Instruction = closed.Text.ToString().Trim();
                closed.Field.IsComplete = true;
                state.Open.RemoveAt(state.Open.Count - 1);
                break;
            case InstructionText text when state.Open.Count > 0 && !state.Open[^1].InResult:
                state.Open[^1].Text.Append(text.Text);
                break;
        }
    }
}
