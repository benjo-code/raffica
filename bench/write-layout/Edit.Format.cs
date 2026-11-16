using System.Diagnostics;
using System.Globalization;
using System.Text;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;

namespace WriteLayoutBench;

/// <summary>
/// The formatting part of <c>edit</c>: the <c>format-word</c> and <c>format-paragraph</c> cases at the places of the typing cases, the style update and the select-all bold. The 16 ms limit of a
/// formatting edit is judged on the app (<c>--measure-format</c>); these rows are the engine alone and are for the record.
/// </summary>
internal static partial class EditCommand
{
    /// <summary>The style update, repetition by repetition: the editor call, the time from the start of the update to the pages in view (editor and <see cref="DocumentLayout.Restyle"/> through the window) and to the full layout.</summary>
    internal sealed record StyleUpdateRow(string Case, double[] EditorMillis, double[] ViewMillis, double[] FullMillis);

    /// <summary>The rows of the format measurement and the style update (null when the document has no paragraph with a style update to measure).</summary>
    internal sealed record FormatReport(List<EditRow> Rows, StyleUpdateRow? StyleUpdate);

    /// <summary>The times of select all, bold, undo in milliseconds: with the plain host and no layout, then with the layout's host and <see cref="DocumentLayout.ApplyEdit"/> (the editor call, the layout call, the undo, the layout call of the undo).</summary>
    internal sealed record SelectAllResult(double PlainToggleMs, double PlainUndoMs, double LayoutToggleMs, double LayoutApplyMs, double LayoutUndoMs, double LayoutUndoApplyMs);

    /// <summary>
    /// The formatting cases (<see cref="EditCases.FormatNames"/>) at every place of <see cref="EditCases.Pick"/>, in both views, through the path the app uses: <see cref="TextEditor.ToggleCharacter"/> (bold on one
    /// word, toggled again each time) or <see cref="TextEditor.ApplyParagraphChanges"/> (the alignment, centered and right in turn), then <see cref="DocumentLayout.ApplyEdit"/> for every block change of the result. The row
    /// case is <c>&lt;format case&gt;/&lt;place&gt;</c>. The report also holds the style update (<see cref="MeasureStyleUpdate"/>), which is recorded and never judged.
    /// </summary>
    internal static FormatReport MeasureFormat(string file, int count, TextWriter error)
    {
        var bytes = File.ReadAllBytes(file);
        using var docx = DocxFile.Open(bytes);
        var model = docx.Model;
        var fonts = FontCatalog.CreateShipped();
        var images = new PackageImages(bytes);

        var pages = LayOut(model, fonts, images, LayoutMode.Pages);
        var cases = EditCases.Pick(model, pages);
        foreach (var name in EditCases.Names.Where(n => cases.All(c => c.Name != n)))
        {
            error.WriteLine($"write-layout-bench: the document has no case '{name}'");
        }

        var current = pages;
        var editor = new TextEditor(model, (story, paragraph) => current.ProjectionHost(story, paragraph), format: new LayoutEffectiveFormat(model));
        var rows = new List<EditRow>();
        foreach (var (view, mode) in new[] { ("page", LayoutMode.Pages), ("writing", LayoutMode.Flow) })
        {
            if (mode == LayoutMode.Flow)
            {
                current = LayOut(model, fonts, images, LayoutMode.Flow);
            }

            var layout = current;
            foreach (var kind in EditCases.FormatNames)
            {
                foreach (var c in cases)
                {
                    Complete(layout);
                    var pageHint = layout.Locate(c.HintBlock)?.Page ?? 0;
                    var result = Format(editor, layout, c, kind, pageHint, count);
                    rows.Add(new EditRow($"{kind}/{c.Name}", view, string.Empty, result.Millis, result.Edits, result.Restarts, null));
                }
            }
        }

        return new FormatReport(rows, MeasureStyleUpdate(file, count, error));
    }

    private static (double[] Millis, int Edits, int Restarts) Format(TextEditor editor, DocumentLayout layout, EditCase c, string kind, int pageHint, int count)
    {
        var position = new TextPosition(c.Story, c.Paragraph, c.Offset);
        TextRange range;
        if (kind == "format-word")
        {
            var projection = ParagraphProjection.Build(c.Paragraph, layout.ProjectionHost(c.Story, c.Paragraph));
            range = TextUnits.WordAt(position with { Offset = Math.Min(c.Offset, Math.Max(0, projection.Text.Length - 1)) }, projection, trailing: false);
        }
        else
        {
            range = new TextRange(position, position);
        }

        var millis = new double[count];
        var before = layout.Stats;
        for (var i = 0; i < count; i++)
        {
            var through = Math.Min(Math.Max(0, layout.LaidOutPages - 1), pageHint + 1);
            var start = Stopwatch.GetTimestamp();
            var result = kind == "format-word"
                ? editor.ToggleCharacter(range, CharacterToggle.Bold)
                : editor.ApplyParagraphChanges(range, ParagraphChanges.Alignment(i % 2 == 0 ? ParagraphJustification.Center : ParagraphJustification.Right));
            foreach (var change in result.Changes)
            {
                layout.ApplyEdit(change, through);
            }

            millis[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

            // As after a keystroke: the pages far from the edit of a header or footer wait for the background pass, which runs between two edits.
            if (layout.ChromeRefreshPending)
            {
                layout.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();
            }
        }

        var after = layout.Stats;
        return (millis, after.Edits - before.Edits, after.Restarts - before.Restarts);
    }

    /// <summary>
    /// "Update style to match" on a fresh copy of the document, <paramref name="count"/> times: a direct alignment is set on a body paragraph (outside the timing), then <see cref="TextEditor.UpdateStyleToMatch"/>
    /// moves it into the paragraph's style and <see cref="DocumentLayout.Restyle"/> lays the document out again from page 1. The timed spans: the editor call, the editor and <c>Restyle</c> through the page after the
    /// paragraph's page (what the user waits for), and that plus the layout of the rest. Recorded, never judged. Null when no body case has a style that the file defines.
    /// </summary>
    internal static StyleUpdateRow? MeasureStyleUpdate(string file, int count, TextWriter error)
    {
        var bytes = File.ReadAllBytes(file);
        using var docx = DocxFile.Open(bytes);
        var model = docx.Model;
        var layout = LayOut(model, FontCatalog.CreateShipped(), new PackageImages(bytes), LayoutMode.Pages);
        var format = new RefreshingFormat(model);
        foreach (var c in EditCases.Pick(model, layout).Where(c => c.Story.Kind == StoryKind.Body))
        {
            var editor = new TextEditor(model, (story, paragraph) => layout.ProjectionHost(story, paragraph), format: format);
            var position = new TextPosition(c.Story, c.Paragraph, c.Offset);
            var range = new TextRange(position, position);
            var pageHint = layout.Locate(c.HintBlock)?.Page ?? 0;
            var editorMs = new List<double>();
            var viewMs = new List<double>();
            var fullMs = new List<double>();
            for (var i = 0; i < count; i++)
            {
                Complete(layout);
                foreach (var change in editor.ApplyParagraphChanges(range, ParagraphChanges.Alignment(i % 2 == 0 ? ParagraphJustification.Center : ParagraphJustification.Right)).Changes)
                {
                    layout.ApplyEdit(change, pageHint + 1);
                }

                Complete(layout);
                var start = Stopwatch.GetTimestamp();
                var result = editor.UpdateStyleToMatch(range);
                if (result is not { Style: not null })
                {
                    break;
                }

                var edited = Stopwatch.GetTimestamp();
                format.Invalidate();
                layout.Restyle(Math.Min(Math.Max(0, layout.LaidOutPages - 1), pageHint + 1));
                var inView = Stopwatch.GetTimestamp();
                layout.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();
                var full = Stopwatch.GetTimestamp();
                editorMs.Add(Stopwatch.GetElapsedTime(start, edited).TotalMilliseconds);
                viewMs.Add(Stopwatch.GetElapsedTime(start, inView).TotalMilliseconds);
                fullMs.Add(Stopwatch.GetElapsedTime(start, full).TotalMilliseconds);
            }

            if (editorMs.Count == count)
            {
                return new StyleUpdateRow(c.Name, [.. editorMs], [.. viewMs], [.. fullMs]);
            }
        }

        error.WriteLine("write-layout-bench: no body case of the document has a style to update");
        return null;
    }

    // The editor's format resolver snapshots the styles when it is made: made again after a style change (the app's session does the same).
    private sealed class RefreshingFormat(WordDocument model) : IEffectiveFormat
    {
        private LayoutEffectiveFormat _inner = new(model);

        public void Invalidate() => _inner = new LayoutEffectiveFormat(model);

        public EffectiveRun ForRun(StoryRef story, Paragraph paragraph, Run run) => _inner.ForRun(story, paragraph, run);

        public EffectiveRun ForMark(StoryRef story, Paragraph paragraph) => _inner.ForMark(story, paragraph);
    }

    internal static string StyleUpdateTable(StyleUpdateRow row)
    {
        var text = new StringBuilder();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{"style update",-20}{"n",5}{"editor med",11}{"to view med",12}{"to view max",12}{"to full med",12}{"to full max",12}"));
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{row.Case,-20}{row.ViewMillis.Length,5}{EditRow.Percentile(row.EditorMillis, 0.5),11:0.000}{EditRow.Percentile(row.ViewMillis, 0.5),12:0.000}{row.ViewMillis.Max(),12:0.000}{EditRow.Percentile(row.FullMillis, 0.5),12:0.000}{row.FullMillis.Max(),12:0.000}"));
        text.AppendLine("recorded, never judged: the editor call, the time to the pages in view (editor and Restyle) and to the full layout, in ms.");
        return text.ToString();
    }

    /// <summary>
    /// Select all, bold, undo, on a fresh copy for each path: with the plain projection host and no layout (what the unit test <c>SelectAllBoldOnTheThesisIsOneUndoStepAndFast</c> does), and with the host of the pages
    /// layout and <see cref="DocumentLayout.ApplyEdit"/> of the block change through page 1 (the path of the app, without the session). Recorded, never judged.
    /// </summary>
    internal static SelectAllResult MeasureSelectAll(string file)
    {
        var bytes = File.ReadAllBytes(file);
        double plainToggle;
        double plainUndo;
        using (var docx = DocxFile.Open(bytes))
        {
            var model = docx.Model;
            var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain, format: new LayoutEffectiveFormat(model));
            var range = AllOf(model, ProjectionHost.Plain);
            var start = Stopwatch.GetTimestamp();
            _ = editor.ToggleCharacter(range, CharacterToggle.Bold);
            plainToggle = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            start = Stopwatch.GetTimestamp();
            _ = editor.History.Undo();
            plainUndo = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        using var second = DocxFile.Open(bytes);
        var model2 = second.Model;
        var layout = LayOut(model2, FontCatalog.CreateShipped(), new PackageImages(bytes), LayoutMode.Pages);
        var layoutEditor = new TextEditor(model2, (story, paragraph) => layout.ProjectionHost(story, paragraph), format: new LayoutEffectiveFormat(model2));
        var all = AllOf(model2, layout.ProjectionHost(StoryRef.Body, model2.Body.Blocks.OfType<Paragraph>().First()));

        var begin = Stopwatch.GetTimestamp();
        var toggled = layoutEditor.ToggleCharacter(all, CharacterToggle.Bold);
        var afterEditor = Stopwatch.GetTimestamp();
        foreach (var change in toggled.Changes)
        {
            layout.ApplyEdit(change, 1);
        }

        var afterApply = Stopwatch.GetTimestamp();
        Complete(layout);

        var undoBegin = Stopwatch.GetTimestamp();
        var undone = layoutEditor.History.Undo();
        var undoEditor = Stopwatch.GetTimestamp();
        foreach (var change in undone?.Changes ?? [])
        {
            layout.ApplyEdit(change, 1);
        }

        var undoApply = Stopwatch.GetTimestamp();
        return new SelectAllResult(
            plainToggle,
            plainUndo,
            Stopwatch.GetElapsedTime(begin, afterEditor).TotalMilliseconds,
            Stopwatch.GetElapsedTime(afterEditor, afterApply).TotalMilliseconds,
            Stopwatch.GetElapsedTime(undoBegin, undoEditor).TotalMilliseconds,
            Stopwatch.GetElapsedTime(undoEditor, undoApply).TotalMilliseconds);
    }

    // The range from the start of the first body paragraph to the end of the last one (a plain host measures the text of a paragraph without its layout).
    private static TextRange AllOf(WordDocument model, IProjectionHost host)
    {
        var first = model.Body.Blocks.OfType<Paragraph>().First();
        var last = model.Body.Blocks.OfType<Paragraph>().Last();
        return new TextRange(new TextPosition(StoryRef.Body, first, 0), new TextPosition(StoryRef.Body, last, ParagraphProjection.Build(last, host).Text.Length));
    }

    internal static string SelectAllTable(SelectAllResult result)
    {
        var text = new StringBuilder();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{"select-all bold",-32}{"toggle ms",11}{"apply ms",10}{"undo ms",9}{"undo apply ms",15}"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{"plain host, no layout",-32}{result.PlainToggleMs,11:0.0}{"-",10}{result.PlainUndoMs,9:0.0}{"-",15}"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{"layout host, ApplyEdit (page 1)",-32}{result.LayoutToggleMs,11:0.0}{result.LayoutApplyMs,10:0.0}{result.LayoutUndoMs,9:0.0}{result.LayoutUndoApplyMs,15:0.0}"));
        text.AppendLine("recorded, never judged (select all, bold, undo on the thesis).");
        return text.ToString();
    }
}
