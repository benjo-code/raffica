using System.Diagnostics;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;

namespace WriteLayoutBench;

/// <summary>
/// The structure part of <c>edit</c>: the cases of <see cref="StructureCases.Names"/> (the chapter moves, the heading numbering switch and a numbered list toggle) through the editor and the layout, without a
/// window: <see cref="TextEditor.MoveBlocks"/>, <see cref="TextEditor.SetHeadingNumbering"/> and <see cref="TextEditor.ToggleList"/>, then <see cref="DocumentLayout.ApplyEdit"/> for every block change (or
/// <see cref="DocumentLayout.Restyle"/> for a style change) through the page after the first one. Every run is undone outside the timing and the layout completed, so each run starts from the state of
/// the file. The rows are for the record: the 100 ms aim of a chapter move is judged on the app (<c>--measure-structure</c>). <see cref="StructureCases"/> is a near copy of the app's <c>StructurePlaces</c>
/// and the planning of a move a simplification of its <c>OutlinePlanner</c> (the bench cannot reference the app): change them together.
/// </summary>
internal static partial class EditCommand
{
    private const int StructureRuns = 20;

    /// <summary>The rows of the structure measurement (one per case that the document allows).</summary>
    internal static List<EditRow> MeasureStructure(string file, int count, TextWriter error)
    {
        var bytes = File.ReadAllBytes(file);
        using var docx = DocxFile.Open(bytes);
        var model = docx.Model;
        var layout = LayOut(model, FontCatalog.CreateShipped(), new PackageImages(bytes), LayoutMode.Pages);
        var format = new RefreshingFormat(model);
        var editor = new TextEditor(model, (story, paragraph) => layout.ProjectionHost(story, paragraph), format: format);
        var resolver = new StyleResolver(model.Styles, model.Settings, model.Numbering);
        var blocks = model.Body.Blocks;
        var rows = new List<EditRow>();

        // The heading rows of the body (any outline level): the index of the block and the level as the outline counts it (plus one).
        var headings = new List<(int Block, int Level)>();
        for (var i = 0; i < blocks.Count; i++)
        {
            if (blocks[i] is Paragraph p && resolver.ForParagraph(p).OutlineLevel is { } level and >= 0 and <= 8
                && ParagraphProjection.Build(p, layout.ProjectionHost(StoryRef.Body, p)).Text.Length > 0)
            {
                headings.Add((i, level + 1));
            }
        }

        var chapters = headings.Where(h => h.Level == 1).ToList();

        void Apply(EditResult? result)
        {
            if (result is null)
            {
                throw new InvalidOperationException("an operation of the structure measurement was refused");
            }

            var through = Math.Min(Math.Max(0, layout.LaidOutPages - 1), 1);
            if (result.Style is not null)
            {
                format.Invalidate();
                layout.Restyle(through);
            }
            else
            {
                foreach (var change in result.Changes)
                {
                    layout.ApplyEdit(change, through);
                }
            }
        }

        // One case: the preparation (null when the state is right already) and the timed operation, then every change undone.
        double[] Run(Func<EditResult?> prepare, Func<EditResult?> timed)
        {
            var millis = new double[count];
            for (var i = 0; i < count; i++)
            {
                Complete(layout);
                var undos = 0;
                if (prepare() is { } before)
                {
                    Apply(before);
                    Complete(layout);
                    undos++;
                }

                var start = Stopwatch.GetTimestamp();
                Apply(timed());
                millis[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                undos++;
                for (; undos > 0; undos--)
                {
                    Apply(editor.History.Undo());
                    Complete(layout);
                }
            }

            return millis;
        }

        EditResult? Nothing() => null;
        EditRow Row(string name, double[] millis) => new(name, "page", string.Empty, millis, 0, 0, null);

        if (chapters.Count >= 2)
        {
            var first = chapters[0].Block;
            var last = chapters[^1].Block;
            var second = chapters[1].Block;
            rows.Add(Row("move-first-to-last", Run(Nothing, () => editor.MoveBlocks(first, second - first, blocks.Count))));
            if (chapters.Count >= 3)
            {
                // One row down, as the outline command does: the chapter lands behind the heading row that follows the next chapter's own heading.
                var middle = chapters[chapters.Count / 2];
                var neighbour = headings.IndexOf(middle) + 1;
                while (neighbour < headings.Count && headings[neighbour].Level > 1)
                {
                    neighbour++;
                }

                var target = neighbour + 1 < headings.Count ? headings[neighbour + 1].Block : blocks.Count;
                var next = chapters[(chapters.Count / 2) + 1].Block;
                rows.Add(Row("move-middle", Run(Nothing, () => editor.MoveBlocks(middle.Block, next - middle.Block, target))));
            }

            rows.Add(Row("move-last-to-first", Run(Nothing, () => editor.MoveBlocks(last, blocks.Count - last, first))));
        }
        else
        {
            error.WriteLine("write-layout-bench: the document has no two chapters to move");
        }

        // An undo may replace the paragraph objects: the caret is made again from the body block for every run.
        if (headings.Count > 0 && blocks[headings[0].Block] is Paragraph)
        {
            TextPosition caret() => new(StoryRef.Body, (Paragraph)blocks[headings[0].Block], 0);
            EditResult? Switch(bool on) => HeadingNumbering.StateOf(model) == (on ? HeadingNumberingState.On : HeadingNumberingState.Off) ? null : editor.SetHeadingNumbering(on, caret());
            rows.Add(Row("numbering-on", Run(() => Switch(false), () => editor.SetHeadingNumbering(true, caret()))));
            rows.Add(Row("numbering-off", Run(() => Switch(true), () => editor.SetHeadingNumbering(false, caret()))));
        }

        if (EditCases.Pick(model, layout).FirstOrDefault(c => c.Name == "middle") is { } pick && blocks[pick.HintBlock] is Paragraph)
        {
            rows.Add(Row("toggle-list", Run(Nothing, () =>
            {
                var position = new TextPosition(StoryRef.Body, (Paragraph)blocks[pick.HintBlock], 0);
                return editor.ToggleList(new TextRange(position, position), ListKind.Numbered);
            })));
        }

        return rows;
    }
}

/// <summary>The cases of the structure measurement (<c>StructurePlaces.Names</c> of the Write app, a near copy: change both together).</summary>
internal static class StructureCases
{
    public static readonly string[] Names = ["move-first-to-last", "move-middle", "move-last-to-first", "numbering-on", "numbering-off", "toggle-list"];
}
