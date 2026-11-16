using System.Diagnostics;
using System.Xml.Linq;
using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests.Editing;

[Collection(WallClockGroup.Name)]
public sealed class ParagraphFormattingTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static string Root => Path.Combine(TestData.Root, "testdata", "write-corpus");

    private static (WordDocument Document, TextEditor Editor) Setup(string body, string styles = "")
    {
        var part = LayoutModel.StylesPart(
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>"
            + "<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:tblPr/></w:style>" + styles);
        var document = LayoutModel.Document(body, part);
        return (document, new TextEditor(document, (_, _) => ProjectionHost.Plain, format: new LayoutEffectiveFormat(document)));
    }

    private static string Run(string text, string props = "") =>
        $"<w:r>{(props.Length == 0 ? string.Empty : $"<w:rPr>{props}</w:rPr>")}<w:t>{text}</w:t></w:r>";

    private static string Para(string text, string props = "") =>
        $"<w:p>{(props.Length == 0 ? string.Empty : $"<w:pPr>{props}</w:pPr>")}{Run(text)}</w:p>";

    private static Paragraph P(WordDocument document, int index) => (Paragraph)document.Body.Blocks[index];

    private static TextPosition At(Paragraph paragraph, int offset, StoryRef? story = null) => new(story ?? StoryRef.Body, paragraph, offset);

    private static TextRange Sel(Paragraph first, Paragraph last, StoryRef? story = null) => new(At(first, 0, story), At(last, 1, story));

    private static string Props(PropertyBag bag) =>
        string.Join(",", bag.Children.Select(c => c.Name.LocalName + (c.HasAttributes ? "(" + string.Join(" ", c.Attributes().Select(a => a.Name.LocalName + "=" + a.Value)) + ")" : string.Empty)));

    [Fact]
    public void AlignmentSetsJcOnEveryTouchedParagraph()
    {
        var (doc, editor) = Setup(Para("one") + Para("two") + Para("three"));
        var result = editor.ApplyParagraphChanges(Sel(P(doc, 0), P(doc, 1)), ParagraphChanges.Alignment(ParagraphJustification.Center));
        Assert.Single(result.Changes);
        Assert.Equal("jc(val=center)", Props(P(doc, 0).Properties));
        Assert.Equal("jc(val=center)", Props(P(doc, 1).Properties));
        Assert.Equal(string.Empty, Props(P(doc, 2).Properties));
        Assert.Equal(1, editor.History.UndoCount);

        editor.ApplyParagraphChanges(Sel(P(doc, 0), P(doc, 0)), ParagraphChanges.Alignment(ParagraphJustification.Justify));
        Assert.Equal("jc(val=both)", Props(P(doc, 0).Properties));
    }

    [Fact]
    public void IndentKeepsTheAttributesItDoesNotName()
    {
        var (doc, editor) = Setup(Para("one", "<w:ind w:left=\"720\" w:right=\"100\" w:firstLine=\"360\"/>"));
        editor.ApplyParagraphChanges(Sel(P(doc, 0), P(doc, 0)), ParagraphChanges.Indent(36f, null, null, null));
        Assert.Equal("ind(left=720 right=100 firstLine=360)", Props(P(doc, 0).Properties));
        editor.ApplyParagraphChanges(Sel(P(doc, 0), P(doc, 0)), ParagraphChanges.Indent(18.5f, 2f, null, null));
        Assert.Equal("ind(left=370 right=40 firstLine=360)", Props(P(doc, 0).Properties));
    }

    [Fact]
    public void HangingAndFirstLineExcludeEachOther()
    {
        Assert.Throws<ArgumentException>(() => ParagraphChanges.Indent(null, null, 10f, 10f));

        var (doc, editor) = Setup(Para("one", "<w:ind w:left=\"720\" w:firstLine=\"360\"/>"));
        editor.ApplyParagraphChanges(Sel(P(doc, 0), P(doc, 0)), ParagraphChanges.Indent(null, null, null, 18f));
        Assert.Equal("ind(left=720 hanging=360)", Props(P(doc, 0).Properties));
        editor.ApplyParagraphChanges(Sel(P(doc, 0), P(doc, 0)), ParagraphChanges.Indent(null, null, 9f, null));
        Assert.Equal("ind(left=720 firstLine=180)", Props(P(doc, 0).Properties));
    }

    [Fact]
    public void SpacingWritesTwipsAndLineRule()
    {
        var (doc, editor) = Setup(Para("one"));
        editor.ApplyParagraphChanges(Sel(P(doc, 0), P(doc, 0)), ParagraphChanges.Spacing(6f, 12f, 1.5f, LineRuleKind.Auto));
        Assert.Equal("spacing(before=120 after=240 line=360 lineRule=auto)", Props(P(doc, 0).Properties));

        editor.ApplyParagraphChanges(Sel(P(doc, 0), P(doc, 0)), ParagraphChanges.Spacing(null, 0f, 14f, LineRuleKind.Exact));
        Assert.Equal("spacing(before=120 after=0 line=280 lineRule=exact)", Props(P(doc, 0).Properties));

        // A rule alone changes only the rule: the number stays as it is.
        editor.ApplyParagraphChanges(Sel(P(doc, 0), P(doc, 0)), ParagraphChanges.Spacing(null, null, null, LineRuleKind.AtLeast));
        Assert.Equal("spacing(before=120 after=0 line=280 lineRule=atLeast)", Props(P(doc, 0).Properties));
    }

    [Fact]
    public void ALineValueNeedsItsRule()
    {
        Assert.Throws<ArgumentException>(() => ParagraphChanges.Spacing(null, null, 14f, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => ParagraphChanges.Spacing(-1f, null, null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => ParagraphChanges.Spacing(null, null, -1f, LineRuleKind.Exact));
    }

    [Fact]
    public void ANegativeIndentIsWritableAndTheCapStaysForPoints()
    {
        var (doc, editor) = Setup(Para("one"));
        editor.ApplyParagraphChanges(Sel(P(doc, 0), P(doc, 0)), ParagraphChanges.Indent(-18f, null, -9f, null));
        Assert.Equal("ind(left=-360 firstLine=-180)", Props(P(doc, 0).Properties));
        Assert.Throws<ArgumentOutOfRangeException>(() => ParagraphChanges.Indent(40000f, null, null, null));
        Assert.Equal("spacing(line=2400 lineRule=auto)", Props(ParagraphChangesElement(ParagraphChanges.Spacing(null, null, 10f, LineRuleKind.Auto))));
    }

    private static PropertyBag ParagraphChangesElement(IReadOnlyList<PropertyChange> changes)
    {
        var bag = new PropertyBag(PropertyKind.Paragraph);
        bag.Set(changes[0].Name, changes[0].Value!);
        return bag;
    }

    [Fact]
    public void LegacyIndentSpellingsAreReplacedByTheNewValue()
    {
        var (doc, editor) = Setup(Para("one", "<w:ind w:start=\"720\" w:end=\"100\" w:leftChars=\"50\" w:rightChars=\"5\"/>"));
        editor.ApplyParagraphChanges(Sel(P(doc, 0), P(doc, 0)), ParagraphChanges.Indent(36f, 2f, null, null));
        Assert.Equal("ind(left=720 right=40)", Props(P(doc, 0).Properties));
    }

    [Fact]
    public void FirstLineReplacesHangingInEveryItsSpelling()
    {
        var (doc, editor) = Setup(Para("one", "<w:ind w:left=\"720\" w:hanging=\"360\" w:hangingChars=\"20\"/>"));
        editor.ApplyParagraphChanges(Sel(P(doc, 0), P(doc, 0)), ParagraphChanges.Indent(null, null, 18f, null));
        Assert.Equal("ind(left=720 firstLine=360)", Props(P(doc, 0).Properties));
    }

    [Fact]
    public void ASpacingValueReplacesItsAutospacingAndLinesSpelling()
    {
        var (doc, editor) = Setup(Para("one", "<w:spacing w:before=\"1\" w:beforeLines=\"50\" w:beforeAutospacing=\"1\" w:after=\"9\" w:afterLines=\"5\" w:afterAutospacing=\"1\"/>"));
        editor.ApplyParagraphChanges(Sel(P(doc, 0), P(doc, 0)), ParagraphChanges.Spacing(6f, 12f, null, null));
        Assert.Equal("spacing(before=120 after=240)", Props(P(doc, 0).Properties));
    }

    [Fact]
    public void SwitchesWriteTheElementOnAndAnExplicitOffOtherwise()
    {
        var (doc, editor) = Setup(Para("one"));
        var range = Sel(P(doc, 0), P(doc, 0));
        editor.ApplyParagraphChanges(range, ParagraphChanges.Switch(ParagraphSwitch.KeepNext, true));
        editor.ApplyParagraphChanges(range, ParagraphChanges.Switch(ParagraphSwitch.WidowControl, false));
        Assert.Equal("keepNext,widowControl(val=0)", Props(P(doc, 0).Properties));
    }

    [Fact]
    public void ChoosingTheStyleValueStillWritesTheOverride()
    {
        var (doc, editor) = Setup(
            Para("one", "<w:pStyle w:val=\"Centered\"/>"),
            "<w:style w:type=\"paragraph\" w:styleId=\"Centered\"><w:pPr><w:jc w:val=\"center\"/></w:pPr></w:style>");
        editor.ApplyParagraphChanges(Sel(P(doc, 0), P(doc, 0)), ParagraphChanges.Alignment(ParagraphJustification.Center));
        Assert.Equal("pStyle(val=Centered),jc(val=center)", Props(P(doc, 0).Properties));
    }

    [Fact]
    public void ApplyParagraphStyleKeepsDirectOverrides()
    {
        var (doc, editor) = Setup(
            Para("one", "<w:jc w:val=\"right\"/>") + Para("two"),
            "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/></w:style>");
        editor.ApplyParagraphStyle(Sel(P(doc, 0), P(doc, 1)), "Heading1");
        Assert.Equal("pStyle(val=Heading1),jc(val=right)", Props(P(doc, 0).Properties));
        Assert.Equal("Heading1", P(doc, 1).Properties.StyleId);

        editor.ApplyParagraphStyle(Sel(P(doc, 0), P(doc, 1)), null);
        Assert.Equal("jc(val=right)", Props(P(doc, 0).Properties));
        Assert.Null(P(doc, 1).Properties.StyleId);
        Assert.Equal(2, editor.History.UndoCount);
    }

    [Fact]
    public void ApplyParagraphStyleToAnUndefinedIdIsAllowedAndKept()
    {
        var (doc, editor) = Setup(Para("one"));
        var result = editor.ApplyParagraphStyle(Sel(P(doc, 0), P(doc, 0)), "NoSuchStyle");
        Assert.Single(result.Changes);
        Assert.Equal("NoSuchStyle", P(doc, 0).Properties.StyleId);
        Assert.Throws<ArgumentException>(() => editor.ApplyParagraphStyle(Sel(P(doc, 0), P(doc, 0)), string.Empty));
    }

    [Fact]
    public void ApplyCharacterStyleNoneRemovesRStyle()
    {
        var (doc, editor) = Setup("<w:p>" + Run("abcd") + "</w:p>", "<w:style w:type=\"character\" w:styleId=\"Emph\"><w:rPr><w:i/></w:rPr></w:style>");
        var range = new TextRange(At(P(doc, 0), 1), At(P(doc, 0), 3));
        editor.ApplyCharacterStyle(range, "Emph");
        var runs = P(doc, 0).Inlines.OfType<Run>().ToArray();
        Assert.Equal(3, runs.Length);
        Assert.Equal("Emph", runs[1].Properties.StyleId);
        Assert.Null(runs[0].Properties.StyleId);

        editor.ApplyCharacterStyle(range, null);
        runs = [.. P(doc, 0).Inlines.OfType<Run>()];
        Assert.Single(runs);
        Assert.Null(runs[0].Properties.StyleId);
        Assert.Equal(2, editor.History.UndoCount);
    }

    [Fact]
    public void ParagraphsInCellsNotesAndHeadersFormat()
    {
        var (doc, editor) = Setup(
            Para("body") + "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"3000\"/></w:tblGrid><w:tr><w:tc>" + Para("cell")
            + "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"3000\"/></w:tblGrid><w:tr><w:tc>" + Para("nested") + "</w:tc></w:tr></w:tbl></w:tc></w:tr></w:tbl>" + Para("end"));
        var table = (Table)doc.Body.Blocks[1];
        var cell = (TableCell)((TableRow)table.Rows[0]).Items[0];
        var cellParagraph = (Paragraph)cell.Blocks[0];
        var nested = (Paragraph)((TableCell)((TableRow)((Table)cell.Blocks[1]).Rows[0]).Items[0]).Blocks[0];

        // Across the table: first to last body paragraph.
        editor.ApplyParagraphChanges(Sel(P(doc, 0), P(doc, 2)), ParagraphChanges.Alignment(ParagraphJustification.Right));
        Assert.Equal("jc(val=right)", Props(cellParagraph.Properties));
        Assert.Equal("jc(val=right)", Props(nested.Properties));
        Assert.Equal(1, editor.History.UndoCount);
        editor.History.Undo();

        // Undo puts the old table object back: read the paragraphs again.
        var inCell = (Paragraph)((TableCell)((TableRow)((Table)doc.Body.Blocks[1]).Rows[0]).Items[0]).Blocks[0];
        Assert.Equal(string.Empty, Props(inCell.Properties) + Props(P(doc, 0).Properties));
        editor.ApplyParagraphChanges(Sel(inCell, inCell), ParagraphChanges.Alignment(ParagraphJustification.Center));
        Assert.Equal("jc(val=center)", Props(inCell.Properties));

        // A footnote and a header.
        var note = new Paragraph();
        note.Inlines.Add(NewRun("note"));
        var noteStory = new Story(new OoxmlRoot(W + "footnote", []));
        noteStory.Blocks.Add(note);
        doc.Footnotes = new NoteCollection(new OoxmlRoot(W + "footnotes", []));
        doc.Footnotes.Notes.Add(new Note(5, NoteKind.Normal, noteStory));
        var noteRef = new StoryRef(StoryKind.Note, "5");
        editor.ApplyParagraphChanges(Sel(note, note, noteRef), ParagraphChanges.Alignment(ParagraphJustification.Right));
        Assert.Equal("jc(val=right)", Props(note.Properties));

        var head = new Paragraph();
        head.Inlines.Add(NewRun("head"));
        var headStory = new Story(new OoxmlRoot(W + "hdr", []));
        headStory.Blocks.Add(head);
        doc.HeadersFooters["rIdH"] = new HeaderFooterPart(headStory);
        var headRef = new StoryRef(StoryKind.HeaderFooter, "rIdH");
        editor.ApplyParagraphStyle(Sel(head, head, headRef), "Header");
        Assert.Equal("Header", head.Properties.StyleId);
    }

    private static Run NewRun(string text)
    {
        var run = new Run();
        run.Content.Add(new TextContent(text));
        return run;
    }

    [Fact]
    public void AFileWithoutAStylesPartDoesNotThrow()
    {
        var document = LayoutModel.Document(Para("one") + Para("two"));
        Assert.Null(document.Styles);
        var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain);
        var range = new TextRange(At(P(document, 0), 0), At(P(document, 1), 3));
        editor.ApplyParagraphStyle(range, "Heading1");
        editor.ApplyParagraphChanges(range, ParagraphChanges.Alignment(ParagraphJustification.Center));
        editor.ApplyCharacterStyle(range, "Strong");
        Assert.Equal("Heading1", P(document, 0).Properties.StyleId);
        Assert.Equal("jc(val=center)", Props(P(document, 1).Properties).Replace("pStyle(val=Heading1),", string.Empty));
        Assert.Equal("Strong", P(document, 1).Inlines.OfType<Run>().Single().Properties.StyleId);
    }

    // The bound stays at 5 s (the run is about 0.3 s alone): the class sits in WallClockGroup so no other test class loads the machine beside it.
    [Fact]
    public void SelectAllBoldOnTheThesisIsOneUndoStepAndFast()
    {
        var entry = CorpusManifest.Load(Root).Files.Single(e => e.Id == "thesis");
        var path = CorpusCheck.Resolve(Root, entry.Path);
        Assert.NotNull(path);
        using var file = DocxFile.Open(path);
        var model = file.Model;
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain, format: new LayoutEffectiveFormat(model));
        var first = model.Body.Blocks.OfType<Paragraph>().First();
        var last = model.Body.Blocks.OfType<Paragraph>().Last();
        var range = new TextRange(At(first, 0), At(last, ParagraphProjection.Build(last, ProjectionHost.Plain).Text.Length));
        var before = Shape(model);
        var revision = model.Edits.Revision;

        var watch = Stopwatch.StartNew();
        var result = editor.ToggleCharacter(range, CharacterToggle.Bold);
        watch.Stop();
        var message = "select-all bold on the thesis took " + watch.ElapsedMilliseconds + " ms";

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), message);
        // The step (old and new blocks) fits the history limit, so nothing is refused: the whole thesis is one ordinary step.
        Assert.True(2 * EditHistory.SizeOf(result.Changes[0].OldBlocks) < HistoryLimits.Default.MaxBytes);
        Assert.Single(result.Changes);
        Assert.Equal(1, editor.History.UndoCount);
        var format = new LayoutEffectiveFormat(model);
        Assert.True(model.Body.Blocks.OfType<Paragraph>().All(p => p.Inlines.OfType<Run>().Where(r => r.Content.Any(c => c is TextContent)).All(r => format.ForRun(StoryRef.Body, p, r).Bold)));

        editor.History.Undo();
        Assert.Equal(before, Shape(model));
    }

    // Run count and bold run count of the top-level paragraphs: what select-all bold changes and undo must restore.
    private static (int Runs, int Bold) Shape(WordDocument model)
    {
        var runs = model.Body.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<Run>()).ToList();
        return (runs.Count, runs.Count(r => r.Properties.GetFlag(W + "b")));
    }
}
