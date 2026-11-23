using System.Diagnostics;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;

namespace WriteLayoutBench;

/// <summary>
/// The sections part of <c>edit</c>: the cases of <see cref="SectionCases.Names"/> (Number pages from page 3, a prefixed numbering, the removal of a section marker, the page setup of the whole document and of one
/// section, a page number in the footer) through the editor and the layout, without a window: <see cref="TextEditor.NumberPagesFrom"/>, <see cref="TextEditor.RemoveSectionBreak"/>,
/// <see cref="TextEditor.ApplyPageSetup"/> and <see cref="TextEditor.InsertPageNumber"/>, then <see cref="DocumentLayout.Resection"/> through the page after the first one (what the app's edit session does for a
/// result with section properties). Every run is undone outside the timing and the layout completed, so each run starts from the state of the file. The rows are for the record: the section commands are judged
/// on the app (<c>--measure-sections</c>, the metric <c>sections</c>, which has no limit yet). <see cref="SectionCases"/> is a near copy of the app's <c>SectionPlaces</c> (the bench cannot reference the app): change
/// them together.
/// </summary>
internal static partial class EditCommand
{
    private const int SectionRuns = 20;

    /// <summary>The rows of the sections measurement (one per case that the document allows).</summary>
    internal static List<EditRow> MeasureSections(string file, int count, TextWriter error)
    {
        var bytes = File.ReadAllBytes(file);
        using var docx = DocxFile.Open(bytes);
        var model = docx.Model;
        var layout = LayOut(model, FontCatalog.CreateShipped(), new PackageImages(bytes), LayoutMode.Pages);
        var format = new RefreshingFormat(model);
        var editor = new TextEditor(model, (story, paragraph) => layout.ProjectionHost(story, paragraph), format: format);
        var blocks = model.Body.Blocks;
        var rows = new List<EditRow>();
        var picks = EditCases.Pick(model, layout);

        void Apply(EditResult? result)
        {
            if (result is null)
            {
                throw new InvalidOperationException("an operation of the sections measurement was refused");
            }

            var through = Math.Min(Math.Max(0, layout.LaidOutPages - 1), 1);
            if (result.Sections is not null)
            {
                layout.Resection(through);
            }
            else if (result.Style is not null)
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

        // One case: the timed operation (made from the block of its place, read again for every run because an undo may replace paragraph objects), then every change undone.
        double[] Run(int block, Func<TextPosition, EditResult?> timed)
        {
            var millis = new double[count];
            for (var i = 0; i < count; i++)
            {
                Complete(layout);
                var caret = new TextPosition(StoryRef.Body, (Paragraph)blocks[block], 0);
                var start = Stopwatch.GetTimestamp();
                Apply(timed(caret));
                millis[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                Complete(layout);
                Apply(editor.History.Undo());
                Complete(layout);
            }

            return millis;
        }

        EditRow Row(string name, double[] millis) => new(name, "page", string.Empty, millis, 0, 0, null);

        // The block of a place of the typing run (null when the document has none), and the first text paragraph after a section marker for the removal.
        int? PlaceBlock(string place) => picks.FirstOrDefault(c => c.Name == place) is { } pick && blocks[pick.HintBlock] is Paragraph ? pick.HintBlock : null;
        int? AfterMarker()
        {
            foreach (var i in Enumerable.Range(0, Math.Max(0, blocks.Count - 1)).Where(i => blocks[i] is Paragraph { SectionBreak: not null }).OrderBy(i => Math.Abs(i - (blocks.Count / 2))))
            {
                if (blocks[i + 1] is Paragraph { SectionBreak: null } next && ParagraphProjection.Build(next, layout.ProjectionHost(StoryRef.Body, next)).Text.Length > 0)
                {
                    return i + 1;
                }
            }

            return null;
        }

        void Add(string name, int? block, Func<TextPosition, EditResult?> timed)
        {
            if (block is { } at)
            {
                rows.Add(Row(name, Run(at, timed)));
            }
            else
            {
                error.WriteLine($"write-layout-bench: the document has no place for the case '{name}'");
            }
        }

        EditResult? Orientation(TextPosition at, ApplyScope scope)
        {
            var facts = editor.ReadSectionFacts(at);
            var current = SectionPageSetup.Read(SectionList.Of(model)[facts.Section].Properties);
            return editor.ApplyPageSetup(at, new PageSetupChange(current.WithLandscape(!current.Landscape), PageSetupParts.Orientation), scope);
        }

        var middle = PlaceBlock("middle");
        Add(SectionCases.NumberFromPage3, middle, at => editor.NumberPagesFrom(at, new PageNumbering(3, PageNumberFormat.Arabic, string.Empty)));
        Add(SectionCases.AppendixPrefix, PlaceBlock("chapter-end"), at => editor.NumberPagesFrom(at, new PageNumbering(1, PageNumberFormat.Arabic, "A-")));
        Add(SectionCases.RemoveMarker, AfterMarker(), at => editor.RemoveSectionBreak(editor.ReadSectionFacts(at).Section));
        Add(SectionCases.PageSetupWhole, middle, at => Orientation(at, ApplyScope.WholeDocument));
        Add(SectionCases.PageSetupSection, middle, at => Orientation(at, ApplyScope.ThisSection));
        Add(SectionCases.InsertPageNumber, middle, at => editor.InsertPageNumber(at, PageNumberLook.PageOfTotal, HeaderFooterKind.Footer, PageNumberAlign.Center, new PageNumberWords("Page", "of"), "1", Math.Max(1, layout.LaidOutPages).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return rows;
    }
}

/// <summary>The cases of the sections measurement (<c>SectionPlaces.Names</c> of the Write app and <c>WriteRun.SectionsNames</c> of measure.cs, near copies: change the three together).</summary>
internal static class SectionCases
{
    public const string NumberFromPage3 = "number-from-page-3";
    public const string AppendixPrefix = "appendix-prefix";
    public const string RemoveMarker = "remove-marker";
    public const string PageSetupWhole = "page-setup-whole";
    public const string PageSetupSection = "page-setup-section";
    public const string InsertPageNumber = "insert-page-number";

    /// <summary>The cases in the order of the rows.</summary>
    public static readonly string[] Names = [NumberFromPage3, AppendixPrefix, RemoveMarker, PageSetupWhole, PageSetupSection, InsertPageNumber];
}
