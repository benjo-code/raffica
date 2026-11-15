using System.Diagnostics;
using System.Globalization;
using System.Text;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;

namespace WriteLayoutBench;

/// <summary>The keystrokes of one case in one view.</summary>
/// <param name="Case">The name of the case, or <c>first</c> for a first-edit row.</param>
/// <param name="View"><c>page</c> or <c>writing</c>.</param>
/// <param name="When">For a first-edit row <c>after-open</c> or <c>after-switch</c>; empty otherwise.</param>
/// <param name="Millis">One value per keystroke, in milliseconds.</param>
/// <param name="Edits">The edits the layout counted for the case.</param>
/// <param name="Restarts">Of them, how many restarted the layout from page 1 (<see cref="EditStats"/>).</param>
/// <param name="HitMicros">The median of one <see cref="DocumentLayout.HitTest"/> at the caret after the case, in microseconds; null when there was no caret box.</param>
internal sealed record EditRow(string Case, string View, string When, double[] Millis, int Edits, int Restarts, double? HitMicros)
{
    public bool IsFirst => When.Length > 0;

    public double Median => Percentile(Millis, 0.5);

    public double P95 => Percentile(Millis, 0.95);

    public double Max => Millis.Max();

    /// <summary>Nearest rank for the 95th percentile, the mean of the two middle values for the median of an even count.</summary>
    internal static double Percentile(double[] values, double fraction)
    {
        var sorted = values.Order().ToArray();
        if (fraction == 0.5)
        {
            var mid = sorted.Length / 2;
            return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
        }

        return sorted[(int)Math.Ceiling(fraction * sorted.Length) - 1];
    }
}

/// <summary>
/// <c>edit</c>: typing on a document through the path the app uses, without a window. Each keystroke is <see cref="TextEditor.InsertText"/> (with its
/// history step) followed by <see cref="DocumentLayout.ApplyEdit"/> for every block change of the result, in the layout of the view: the pages layout, then
/// (on the same, edited model) the writing view's strips. The pages the app makes right before it returns are the pages through the caret's page and the last
/// page in view; here that is the caret's page and the one after it (a page and a half of window), taken from the caret of the keystroke before (the view
/// keeps its caret page the same way). The timed span is the editor call and the layout call; the caret lookup that sets the next through-page is outside it, and so is the background layout of the pages beyond the edit, which runs to the end between two cases (as it would while nobody types).
/// The first keystroke after the open and the first after the switch to the writing view are rows of their own, reported without a limit. The limit of the
/// milestone (16 ms, median and 95th percentile of every case) is judged on the app's numbers (<c>--measure-typing</c>), not on these.
/// </summary>
internal static class EditCommand
{
    private const int DefaultKeystrokes = 200;
    private const int HitTestCalls = 2000;
    private const int Seed = 4242;

    public static int Run(Options options, TextWriter output, TextWriter error)
    {
        if (ChildProcess.RefuseJit(error))
        {
            return 2;
        }

        var file = options.FileOrThesis();
        if (!File.Exists(file))
        {
            return Program.Fail(error, "the file does not exist");
        }

        var rows = Measure(file, Math.Max(1, options.Int("--runs", DefaultKeystrokes)), error, options.Flag("--phases") ? output : null);
        output.Write(Table(rows));
        if (options.Value("--csv") is { } path)
        {
            File.WriteAllText(path, Csv(rows));
        }

        return 0;
    }

    /// <summary>Types <paramref name="keystrokes"/> letters at every case in both views and returns the rows (first-edit rows included). Not a timing guard: a JIT build is refused by <see cref="Run"/>.</summary>
    internal static List<EditRow> Measure(string file, int keystrokes, TextWriter error, TextWriter? phases = null)
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

        var random = new Random(Seed);
        var rows = new List<EditRow>();
        var current = pages;
        var editor = new TextEditor(model, (story, paragraph) => current.ProjectionHost(story, paragraph));

        foreach (var (view, mode) in new[] { ("page", LayoutMode.Pages), ("writing", LayoutMode.Flow) })
        {
            if (mode == LayoutMode.Flow)
            {
                // The switch: the writing view's layout is made on the model as it is now and laid out whole before the first edit in it.
                current = LayOut(model, fonts, images, LayoutMode.Flow);
            }

            var layout = current;
            var first = true;
            foreach (var c in cases)
            {
                // The background layout of the app finishes the pages beyond the edit while the user is not typing; here it runs between the cases (outside every timing), so that no case
                // meets the layout cut off by the case before it.
                Complete(layout);
                var position = new TextPosition(c.Story, c.Paragraph, c.Offset);
                var pageHint = layout.Locate(c.HintBlock)?.Page ?? 0;
                if (first)
                {
                    // The first keystroke of the view is a row of its own at the first case.
                    var one = Type(editor, layout, c, ref position, ref pageHint, 1, random, 0);
                    rows.Add(new EditRow("first", view, mode == LayoutMode.Pages ? "after-open" : "after-switch", one.Millis, one.Edits, one.Restarts, null));
                    first = false;
                }

                var result = Type(editor, layout, c, ref position, ref pageHint, keystrokes, random, 1);
                rows.Add(new EditRow(c.Name, view, string.Empty, result.Millis, result.Edits, result.Restarts, HitTest(layout, position, pageHint)));
                if (phases is not null && mode == LayoutMode.Pages && c.Name is "header" or "footnote")
                {
                    Sweep(editor, layout, c, position, pageHint, random, phases);
                }
            }
        }

        return rows;
    }

    private static DocumentLayout LayOut(WordDocument model, FontCatalog fonts, PackageImages images, LayoutMode mode)
    {
        var layout = DocumentLayout.Create(model, fonts, new LayoutOptions(mode, ImageBytes: images.Read));
        layout.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        if (!layout.IsComplete)
        {
            throw new InvalidOperationException("the layout did not complete");
        }

        return layout;
    }

    /// <summary>
    /// <c>--phases</c>: where the time of a keystroke goes, for the cases that restart. The same case is typed 30 times with the pages through a forced page, and the editor call and the layout
    /// call are timed apart (medians), to show how the cost follows the page the layout must be right through.
    /// </summary>
    private static void Sweep(TextEditor editor, DocumentLayout layout, EditCase c, TextPosition position, int pageHint, Random random, TextWriter output)
    {
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"phases of '{c.Name}' (page view, caret page {pageHint}, 30 keystrokes each): editor call, layout call, restarts"));
        foreach (var through in new[] { 0, 25, 50, 100, 150, 250, 400 })
        {
            Complete(layout);
            var limit = Math.Min(through, Math.Max(0, layout.LaidOutPages - 1));
            var editorMs = new double[30];
            var layoutMs = new double[30];
            var before = layout.Stats;
            for (var i = 0; i < 30; i++)
            {
                var start = Stopwatch.GetTimestamp();
                var result = editor.InsertText(new TextRange(position, position), EditCases.KeyText(random, i + 1));
                var mid = Stopwatch.GetTimestamp();
                foreach (var change in result.Changes)
                {
                    layout.ApplyEdit(change, limit);
                }

                layoutMs[i] = Stopwatch.GetElapsedTime(mid).TotalMilliseconds;
                editorMs[i] = Stopwatch.GetElapsedTime(start, mid).TotalMilliseconds;
                position = result.Caret;
            }

            var restarts = layout.Stats.Restarts - before.Restarts;
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  through page {limit,3}: editor {EditRow.Percentile(editorMs, 0.5),7:0.000} ms, layout {EditRow.Percentile(layoutMs, 0.5),7:0.000} ms, restarts {restarts}"));
        }
    }

    private static void Complete(DocumentLayout layout)
    {
        if (!layout.IsComplete)
        {
            layout.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        }
    }

    private static (double[] Millis, int Edits, int Restarts) Type(TextEditor editor, DocumentLayout layout, EditCase c, ref TextPosition position, ref int pageHint, int count, Random random, int firstIndex)
    {
        var millis = new double[count];
        var before = layout.Stats;
        for (var i = 0; i < count; i++)
        {
            var text = EditCases.KeyText(random, firstIndex + i);
            var through = Math.Min(Math.Max(0, layout.LaidOutPages - 1), pageHint + 1);
            var start = Stopwatch.GetTimestamp();
            var result = editor.InsertText(new TextRange(position, position), text);
            foreach (var change in result.Changes)
            {
                layout.ApplyEdit(change, through);
            }

            millis[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            position = result.Caret;
            pageHint = layout.Caret(position, pageHint)?.Page ?? pageHint;
        }

        var after = layout.Stats;
        return (millis, after.Edits - before.Edits, after.Restarts - before.Restarts);
    }

    private static double? HitTest(DocumentLayout layout, TextPosition position, int pageHint)
    {
        if (layout.Caret(position, pageHint) is not { } box)
        {
            return null;
        }

        var x = box.X + 1;
        var y = box.Top + (box.Height / 2);
        var micros = new double[HitTestCalls];
        for (var i = 0; i < micros.Length; i++)
        {
            var start = Stopwatch.GetTimestamp();
            _ = layout.HitTest(box.Page, x, y);
            micros[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds * 1000;
        }

        return EditRow.Percentile(micros, 0.5);
    }

    private static string Table(List<EditRow> rows)
    {
        var text = new StringBuilder();
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{"case",-20}{"view",-9}{"n",5}{"median ms",11}{"p95 ms",9}{"max ms",9}{"edits",7}{"restarts",9}{"hit us",9}"));
        foreach (var row in rows)
        {
            var name = row.IsFirst ? "first:" + row.When : row.Case;
            var hit = row.HitMicros is { } h ? h.ToString("0.00", CultureInfo.InvariantCulture) : "-";
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{name,-20}{row.View,-9}{row.Millis.Length,5}{row.Median,11:0.000}{row.P95,9:0.000}{row.Max,9:0.000}{row.Edits,7}{row.Restarts,9}{hit,9}"));
        }

        text.AppendLine("first-edit rows have no limit; the 16 ms limit is judged on the app (--measure-typing).");
        return text.ToString();
    }

    private static string Csv(List<EditRow> rows)
    {
        var text = new StringBuilder("case,view,when,n,median_ms,p95_ms,max_ms,edits,restarts,hit_us\n");
        foreach (var row in rows)
        {
            var hit = row.HitMicros is { } h ? h.ToString("0.00", CultureInfo.InvariantCulture) : string.Empty;
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{row.Case},{row.View},{row.When},{row.Millis.Length},{row.Median:0.000},{row.P95:0.000},{row.Max:0.000},{row.Edits},{row.Restarts},{hit}"));
        }

        return text.ToString();
    }
}
