using System.Diagnostics;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;

namespace WriteLayoutBench;

/// <summary>
/// The fields part of <c>edit</c>: the cases of <see cref="FieldCases.Names"/> (a caption, a cross-reference, a table of contents and a bookmark inserted; the field refresh after a paragraph became a heading, after a
/// pagination change and as the first look of a new host at a document with a stale table of contents) through the editor and the layout, without a window: the operations of <see cref="TextEditor"/>,
/// <see cref="TextEditor.RefreshFields"/> and <see cref="DocumentLayout.ApplyEdit"/> for every block change through the page after the first one. The document is prepared as the app's run prepares it (heading numbering
/// on, a table of contents of the levels 1 and 2 with page numbers, one caption and one page cross-reference). Every run is undone outside the timing and the refresh and the layout brought to rest. The rows are for the
/// record: the limit of the fields cases is judged on the app (<c>--measure-fields</c>). <see cref="FieldCases"/> is a near copy of the app's <c>FieldPlaces</c> and the choice of the places a simplification of the
/// app's (the bench cannot reference the app): change them together.
/// </summary>
internal static partial class EditCommand
{
    private const int FieldRuns = 8;
    private const int FieldPagesParagraphs = 60;

    // What the refresh asks of the app, over the layout the bench edits: the labels and note marks of the layout's own counter and the pages of the pages layout.
    private sealed class BenchFieldHost(WordDocument document, DocumentLayout layout, FontCatalog fonts) : IFieldHost
    {
        private readonly FieldLabels _labels = new(document);
        private readonly FieldPages _pages = new(document, layout);
        private readonly LineMeasure _measure = new(document, fonts);

        public bool IsComplete => layout.IsComplete;

        public string? NumberLabel(Paragraph paragraph) => _labels.NumberLabel(paragraph);

        public string? ChapterNumber(Paragraph paragraph) => _labels.ChapterNumber(paragraph);

        public bool LabelEndsInTab(Paragraph paragraph) => _labels.LabelEndsInTab(paragraph);

        public LineGeometry MeasureLine(Paragraph paragraph, string text) => _measure.Measure(paragraph, text);

        public (int PageIndex, string PageText)? PageOf(TextPosition position) => position.Story.Kind == StoryKind.Body ? _pages.PageOf(position.Paragraph) : null;

        public long? PageSignature => _pages.Signature();

        public string? NoteMarkText(int noteId, NoteType type) => _labels.NoteMarkText(noteId, type);
    }

    /// <summary>The rows of the fields measurement (one per case that the document allows).</summary>
    internal static List<EditRow> MeasureFields(string file, int count, TextWriter error)
    {
        var bytes = File.ReadAllBytes(file);
        using var docx = DocxFile.Open(bytes);
        var model = docx.Model;
        var fonts = FontCatalog.CreateShipped();
        var layout = LayOut(model, fonts, new PackageImages(bytes), LayoutMode.Pages);
        var format = new RefreshingFormat(model);
        var editor = new TextEditor(model, (story, paragraph) => layout.ProjectionHost(story, paragraph), format: format);
        var blocks = model.Body.Blocks;
        var rows = new List<EditRow>();
        var host = new BenchFieldHost(model, layout, fonts);

        int Through() => Math.Min(Math.Max(0, layout.LaidOutPages - 1), 1);

        void Apply(EditResult? result)
        {
            if (result is null)
            {
                throw new InvalidOperationException("an operation of the fields measurement was refused");
            }

            if (result.Style is not null)
            {
                format.Invalidate();
                layout.Restyle(Through());
            }
            else
            {
                foreach (var change in result.ChangesInApplyOrder())
                {
                    layout.ApplyEdit(change, Through());
                }
            }
        }

        // One refresh as the app's timer runs it: the whole body with the page results, the layout told of the results, a structural result attached to the history.
        FieldRefreshResult Refresh(IFieldHost asked)
        {
            var result = editor.RefreshFields(asked, RefreshScope.WithPages);
            foreach (var change in result.Content)
            {
                layout.ApplyEdit(change, Through());
            }

            if (result.Structural is { } record)
            {
                editor.History.AttachDerived(record);
            }

            return result;
        }

        // The refresh brought to rest: a pass over a complete layout until it says the page results are settled (at most three, as the app's runner).
        void Settle()
        {
            for (var pass = 0; pass < 3; pass++)
            {
                Complete(layout);
                if (Refresh(host).Settled)
                {
                    break;
                }
            }

            Complete(layout);
        }

        int Length(Paragraph paragraph) => ParagraphProjection.Build(paragraph, layout.ProjectionHost(StoryRef.Body, paragraph)).Text.Length;
        TextPosition At(int block, int offset = 0) => new(StoryRef.Body, (Paragraph)blocks[block], offset);
        int FirstHeading() => Enumerable.Range(0, blocks.Count).Where(i => blocks[i] is Paragraph { Properties.StyleId: "Heading1" } && !FieldView.IsInTableOfContents(blocks, i)).DefaultIfEmpty(-1).First();
        CrossRefTarget? Target()
        {
            var targets = CrossReferences.Targets(model, host, CrossRefTargetKind.Heading);
            return targets.Count == 0 ? null : targets[targets.Count / 2];
        }

        // The preparation of the app's run, untimed.
        Settle();
        var middle = EditCases.Pick(model, layout).FirstOrDefault(c => c.Name == "middle")?.HintBlock ?? -1;
        if (middle < 0 || FirstHeading() < 0 || Target() is null)
        {
            error.WriteLine("write-layout-bench: the document has no place for the fields cases");
            return rows;
        }

        if (HeadingNumbering.StateOf(model) != HeadingNumberingState.On)
        {
            Apply(editor.SetHeadingNumbering(true, At(FirstHeading())));
            Settle();
        }

        Apply(editor.InsertToc(At(FirstHeading()), new TocOptions(1, 2, true, true, TocLeader.Dots, null), host));
        Settle();
        middle = EditCases.Pick(model, layout).First(c => c.Name == "middle").HintBlock;
        Apply(editor.InsertCaption(At(middle, Length((Paragraph)blocks[middle])), CaptionLabels.Figure, "A measured figure", CaptionPlace.Below, false, CaptionLabels.Figure, host));
        Settle();
        Apply(editor.InsertCrossReference(At(middle, Length((Paragraph)blocks[middle])), Target()!, CrossRefKind.Page, host));
        Settle();

        var baseline = editor.History.UndoCount;
        var half = blocks.Count / 2;
        var chapter = FirstHeading();
        var chapterText = Enumerable.Range(chapter + 1, blocks.Count - chapter - 1).First(i => blocks[i] is Paragraph { SectionBreak: null } p && p.Properties.StyleId?.StartsWith("Heading", StringComparison.Ordinal) != true && Length(p) > 0);
        var plain = Enumerable.Range(0, blocks.Count)
            .Where(i => i != middle && blocks[i] is Paragraph { SectionBreak: null, Properties.StyleId: null or "Normal" } p && !FieldView.IsInTableOfContents(blocks, i) && !FieldView.AnyIn([p]) && Length(p) is >= 1 and <= 400)
            .OrderBy(i => Math.Abs(i - half)).ThenBy(i => i).First();

        // A cheap fingerprint of the body: the blocks and the text of every paragraph.
        (int Blocks, int Characters) Fingerprint() => (blocks.Count, blocks.OfType<Paragraph>().Sum(p => ParagraphProjection.Build(p, ProjectionHost.Plain).Text.Length));

        // One case: the preparation (null when there is none), the timed operation, then every change undone and the refresh brought to rest.
        double[] Run(string name, Action prepare, Action timed)
        {
            var millis = new double[count];
            var before = Fingerprint();
            for (var i = 0; i < count; i++)
            {
                Complete(layout);
                prepare();
                Complete(layout);
                var start = Stopwatch.GetTimestamp();
                timed();
                millis[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                Settle();
                while (editor.History.UndoCount > baseline)
                {
                    Apply(editor.History.Undo());
                    Complete(layout);
                }

                Settle();
            }

            if (editor.History.UndoCount != baseline || Fingerprint() != before)
            {
                throw new InvalidOperationException($"the case '{name}' did not leave the document as it found it (undo steps {editor.History.UndoCount} against {baseline})");
            }

            return millis;
        }

        void Add(string name, Action prepare, Action timed) => rows.Add(new EditRow(name, "page", string.Empty, Run(name, prepare, timed), 0, 0, null));

        void Nothing()
        {
        }

        // A paragraph made a heading: the table of contents is stale until a refresh.
        void MakeHeading() => Apply(editor.ApplyParagraphStyle(new TextRange(At(plain), At(plain)), "Heading1"));

        Add(FieldCases.CaptionInsert, Nothing, () => Apply(editor.InsertCaption(At(middle, Length((Paragraph)blocks[middle])), CaptionLabels.Figure, "Measured figure", CaptionPlace.Below, false, CaptionLabels.Figure, host)));
        Add(FieldCases.XrefInsert, Nothing, () => Apply(editor.InsertCrossReference(At(middle, Length((Paragraph)blocks[middle])), Target()!, CrossRefKind.Page, host)));
        Add(FieldCases.TocInsert, Nothing, () => Apply(editor.InsertToc(At(FirstHeading()), new TocOptions(1, 3, true, true, TocLeader.Dots, null), host)));
        Add(FieldCases.BookmarkAdd, Nothing, () => Apply(editor.AddBookmark(new TextRange(At(middle), At(middle, Length((Paragraph)blocks[middle]))), "Measured")));
        Add(FieldCases.RefreshHeadingEdit, MakeHeading, () => Refresh(host));
        Add(
            FieldCases.RefreshPages,
            () => Apply(editor.InsertText(new TextRange(At(chapterText), At(chapterText)), new string('\n', FieldPagesParagraphs))),
            () => Refresh(host));
        Add(FieldCases.FirstSettle, MakeHeading, () => Refresh(new BenchFieldHost(model, layout, fonts)));
        return rows;
    }
}

/// <summary>The cases of the fields measurement (<c>FieldPlaces.Names</c> of the Write app, which also has the far-view case; <c>WriteRun.FieldsNames</c> of measure.cs: change them together).</summary>
internal static class FieldCases
{
    public const string CaptionInsert = "caption-insert";
    public const string XrefInsert = "xref-insert";
    public const string TocInsert = "toc-insert";
    public const string BookmarkAdd = "bookmark-add";
    public const string RefreshHeadingEdit = "refresh-heading-edit";
    public const string RefreshPages = "refresh-pages";
    public const string FirstSettle = "first-settle";

    /// <summary>The cases in the order of the rows.</summary>
    public static readonly string[] Names = [CaptionInsert, XrefInsert, TocInsert, BookmarkAdd, RefreshHeadingEdit, RefreshPages, FirstSettle];
}
