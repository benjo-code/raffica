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

    /// <summary>True when the field starts inside a tracked change (an insertion, a deletion or a move).</summary>
    public bool InTrackedChange { get; internal set; }

    /// <summary>True when the field starts inside a tracked deletion or in a range that was moved away: it is not part of the document as it reads now.</summary>
    public bool InDeletion { get; internal set; }

    /// <summary>The <c>w:fldSimple</c> of a simple field, or null for a complex one.</summary>
    public SimpleField? Simple { get; internal set; }

    /// <summary>The list that holds the begin run (or the simple field), or null when the scanned sequence was not a list.</summary>
    public IList<Inline>? Container { get; internal set; }

    /// <summary>The run of the begin character of a complex field, or null.</summary>
    public Run? BeginRun { get; internal set; }

    /// <summary>The run of the separate character of a complex field, or null when it has none (yet).</summary>
    public Run? SeparateRun { get; internal set; }

    /// <summary>The run of the end character of a complex field, or null when the field is not closed.</summary>
    public Run? EndRun { get; internal set; }

    /// <summary>The list that holds the result of the field: the content of a simple field, or, for a complex field, the list that holds the begin, the separate and the end run. Null when there is no such single list.</summary>
    public IList<Inline>? ResultContainer { get; internal set; }
}

public static class FieldScanner
{
    /// <summary>Finds the complex (begin, instruction, separate, result, end) and simple fields in document order, nested ones included. A field that is not closed is reported with <c>IsComplete == false</c>; nothing throws.</summary>
    /// <param name="inlines">The inlines to scan.</param>
    /// <param name="intoContentControls">True to look inside inline content controls too (their inlines are the container of a field found there); false, the default, leaves them out, as before.</param>
    public static IReadOnlyList<FieldInfo> Scan(IEnumerable<Inline> inlines, bool intoContentControls = false)
    {
        ArgumentNullException.ThrowIfNull(inlines);
        var state = new ScanState { IntoControls = intoContentControls };
        Walk(inlines, inlines as IList<Inline>, state);
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

        public int TrackedDepth { get; set; }

        public int DeletedDepth { get; set; }

        public bool IntoControls { get; init; }
    }

    private static void Walk(IEnumerable<Inline> inlines, IList<Inline>? container, ScanState state)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    foreach (var content in run.Content)
                    {
                        Content(content, run, container, state);
                    }

                    break;
                case SimpleField simple:
                    var field = new FieldInfo(state.Open.Count > 0 ? state.Open[^1].Field : null)
                    {
                        Instruction = simple.Instruction.Trim(),
                        IsComplete = true,
                        InTrackedChange = state.TrackedDepth > 0,
                        InDeletion = state.DeletedDepth > 0,
                        Simple = simple,
                        Container = container,
                        ResultContainer = simple.Inlines,
                    };
                    state.Result.Add(field);
                    var holder = new OpenField(field) { InResult = true };
                    state.Open.Add(holder);
                    Walk(simple.Inlines, simple.Inlines, state);
                    state.Open.Remove(holder);
                    break;
                case Hyperlink link:
                    Walk(link.Inlines, link.Inlines, state);
                    break;
                case ContentControlInline control when state.IntoControls:
                    Walk(control.Inlines, control.Inlines, state);
                    break;
                case TrackedChange change:
                    var deleted = change.Kind is TrackedChangeKind.Delete or TrackedChangeKind.MoveFrom ? 1 : 0;
                    state.TrackedDepth++;
                    state.DeletedDepth += deleted;
                    Walk(change.Inlines, change.Inlines, state);
                    state.DeletedDepth -= deleted;
                    state.TrackedDepth--;
                    break;
            }
        }
    }

    private static void Content(RunContent content, Run run, IList<Inline>? container, ScanState state)
    {
        switch (content)
        {
            case FieldChar { Kind: FieldCharKind.Begin }:
                var field = new FieldInfo(state.Open.Count > 0 ? state.Open[^1].Field : null)
                {
                    InTrackedChange = state.TrackedDepth > 0,
                    InDeletion = state.DeletedDepth > 0,
                    Container = container,
                    BeginRun = run,
                };
                state.Result.Add(field);
                state.Open.Add(new OpenField(field));
                break;
            case FieldChar { Kind: FieldCharKind.Separate } when state.Open.Count > 0:
                state.Open[^1].InResult = true;
                state.Open[^1].Field.SeparateRun ??= run;
                break;
            case FieldChar { Kind: FieldCharKind.End } when state.Open.Count > 0:
                var closed = state.Open[^1];
                closed.Field.Instruction = closed.Text.ToString().Trim();
                closed.Field.IsComplete = true;
                closed.Field.EndRun = run;
                var info = closed.Field;
                info.ResultContainer = info.SeparateRun is not null && container is not null && ReferenceEquals(info.Container, container) ? container : null;
                state.Open.RemoveAt(state.Open.Count - 1);
                break;
            case InstructionText text when state.Open.Count > 0 && !state.Open[^1].InResult:
                state.Open[^1].Text.Append(text.Text);
                break;
        }
    }
}
