using System.Text;
using Raffica.Write.Model;

namespace Raffica.Write.Editing;

/// <summary>Writes the result of a field and makes new complex fields. The instruction of an existing field is never touched.</summary>
public static class FieldWriter
{
    /// <summary>True when <see cref="SetResult"/> can put a result into the field at all: it has a result position (a separate character in its own run, a closed field, begin and end in one list).</summary>
    public static bool CanWrite(FieldSpan field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return Locate(field, out _, out _, out _);
    }

    private static bool Locate(FieldSpan field, out IList<Inline> list, out int from, out int to)
    {
        list = [];
        from = 0;
        to = 0;
        var info = field.Info;
        if (info?.ResultContainer is not { } container)
        {
            return false;
        }

        list = container;
        if (info.Simple is not null)
        {
            to = list.Count;
            return true;
        }

        if (!info.IsComplete || info.SeparateRun is null || info.EndRun is null)
        {
            return false;
        }

        var separate = list.IndexOf(info.SeparateRun);
        var end = list.IndexOf(info.EndRun);
        if (separate < 0 || end <= separate)
        {
            return false;
        }

        // A separate or end character that shares its run with other content (old result text) is not split: refused, never a mix of old and new text.
        if (info.SeparateRun.Content is not [FieldChar] || info.EndRun.Content is not [FieldChar])
        {
            return false;
        }

        from = separate + 1;
        to = end;
        return true;
    }

    /// <summary>
    /// Replaces the result of the field with one run that holds <paramref name="text"/> and keeps the formatting of the first result run (a nested field's runs are replaced with the rest).
    /// False, and nothing changed, when the result already reads <paramref name="text"/> (as one plain run), or when the field has no result position
    /// (no separate character, not closed, begin and end in different lists).
    /// </summary>
    public static bool SetResult(Paragraph paragraph, FieldSpan field, string text)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(text);
        if (!Locate(field, out var list, out var from, out var to))
        {
            return false;
        }

        // Only the inlines that carry the result are replaced; bookmark and comment markers, unknown inlines and content controls stay where they are.
        var carrying = new List<int>();
        var current = new StringBuilder();
        Run? first = null;
        for (var i = from; i < to; i++)
        {
            if (list[i] is Run or Hyperlink or SimpleField or TrackedChange)
            {
                carrying.Add(i);
                Collect(list[i], current, ref first);
            }
        }

        var plain = carrying.Count == 1 && list[carrying[0]] is Run { Content.Count: <= 1 };
        if (current.ToString() == text && (plain || (carrying.Count == 0 && text.Length == 0)))
        {
            return false;
        }

        var run = first is null ? new Run() : first.Clone();
        run.Content.Clear();
        if (text.Length > 0)
        {
            run.Content.Add(new TextContent(text));
        }

        var at = carrying.Count > 0 ? carrying[0] : from;
        for (var i = carrying.Count - 1; i >= 0; i--)
        {
            list.RemoveAt(carrying[i]);
        }

        list.Insert(at, run);
        return true;
    }

    /// <summary>The runs of a complex field: begin, one instruction run, separate, the result in one run, end. No <c>w:dirty</c>. <paramref name="like"/> gives the run properties of every run (a clone of its properties; its content is not used).</summary>
    public static IReadOnlyList<Inline> Make(string instruction, string result, Run? like)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(result);
        var text = Piece(like, null);
        if (result.Length > 0)
        {
            text.Content.Add(new TextContent(result));
        }

        return
        [
            Piece(like, new FieldChar(FieldCharKind.Begin)),
            Piece(like, new InstructionText(" " + instruction.Trim() + " ")),
            Piece(like, new FieldChar(FieldCharKind.Separate)),
            text,
            Piece(like, new FieldChar(FieldCharKind.End)),
        ];
    }

    private static Run Piece(Run? like, RunContent? content)
    {
        var run = like is null ? new Run() : like.Clone();
        run.Content.Clear();
        if (content is not null)
        {
            run.Content.Add(content);
        }

        return run;
    }

    private static void Collect(Inline inline, StringBuilder text, ref Run? first)
    {
        switch (inline)
        {
            case Run run:
                first ??= run;
                foreach (var content in run.Content)
                {
                    if (content is TextContent t)
                    {
                        text.Append(t.Text);
                    }
                }

                break;
            case SimpleField simple:
                foreach (var child in simple.Inlines)
                {
                    Collect(child, text, ref first);
                }

                break;
            case Hyperlink link:
                foreach (var child in link.Inlines)
                {
                    Collect(child, text, ref first);
                }

                break;
            case TrackedChange change:
                foreach (var child in change.Inlines)
                {
                    Collect(child, text, ref first);
                }

                break;
        }
    }
}
