using System.Text;
using Raffica.Write.Docx;
using Raffica.Write.Docx.Xml;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;
using static Raffica.Write.Tests.Layout.ChromeTestKit;

namespace Raffica.Write.Tests.Editing;

/// <summary>Insert and delete a footnote or an endnote: the reference, the note, the styles, the ids, the refusals and the history (one step, the reference and its note come and go together).</summary>
public sealed class NoteEditingTests
{
    private const string Normal = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>";

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private static MemoryStream Utf8(string xml) => new(Encoding.UTF8.GetBytes(xml));

    private static WordDocument WithStyles(string body, string styles = Normal, string? footnotes = null, string? endnotes = null)
    {
        var document = Document(body, footnotes: footnotes, endnotes: endnotes);
        using var stream = Utf8(LayoutModel.StylesPart(styles));
        document.Styles = StylesXml.Read(stream);
        return document;
    }

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain);

    private static TextPosition At(WordDocument document, int block, int offset) => new(StoryRef.Body, (Paragraph)document.Body.Blocks[block], offset);

    private static string Raw(Paragraph paragraph) =>
        string.Concat(paragraph.Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<TextContent>().Select(t => t.Text));

    private static List<RunContent> Refs(Paragraph paragraph) =>
        [.. paragraph.Inlines.OfType<Run>().SelectMany(r => r.Content).Where(c => c is FootnoteReference or EndnoteReference)];

    [Fact]
    public void InsertFootnoteAddsReferenceNoteAndStyles()
    {
        var document = WithStyles(Line("hello world"));
        var editor = Editor(document);

        var result = editor.InsertNote(At(document, 0, 5), NoteType.Footnote);

        Assert.NotNull(result);
        var paragraph = (Paragraph)document.Body.Blocks[0];
        Assert.Equal("hello world", Raw(paragraph));
        var runs = paragraph.Inlines.OfType<Run>().ToList();
        Assert.Equal(3, runs.Count);
        var reference = Assert.IsType<FootnoteReference>(Assert.Single(runs[1].Content));
        Assert.Equal(1, reference.Id);
        Assert.Equal("FootnoteReference", runs[1].Properties.StyleId);

        var notes = document.Footnotes!;
        Assert.Equal([-1, 0, 1], notes.Notes.Select(n => n.Id).ToArray());
        var note = notes.Find(1)!;
        Assert.Equal(NoteKind.Normal, note.Kind);
        var noteParagraph = (Paragraph)Assert.Single(note.Content.Blocks);
        Assert.Equal("FootnoteText", noteParagraph.Properties.StyleId);
        Assert.Contains(noteParagraph.Inlines.OfType<Run>().SelectMany(r => r.Content), c => c is UnknownRunContent { Content.Element.Name.LocalName: "footnoteRef" });

        Assert.NotNull(document.Styles!.Find("FootnoteReference"));
        Assert.NotNull(document.Styles.Find("FootnoteText"));
        Assert.Equal(new StoryRef(StoryKind.Note, "1"), result.Caret.Story);
        Assert.Same(noteParagraph, result.Caret.Paragraph);
        Assert.Single(result.Objects!.NotesAdded);
        Assert.Same(note, result.Objects.NotesAdded[0]);
        Assert.Empty(result.Objects.NotesRemoved);
    }

    [Fact]
    public void InsertEndnoteAddsAnEndnoteInTheEndnotesPart()
    {
        var document = WithStyles(Line("hello world"));

        var result = Editor(document).InsertNote(At(document, 0, 11), NoteType.Endnote);

        Assert.NotNull(result);
        Assert.Null(document.Footnotes);
        Assert.Equal([-1, 0, 1], document.Endnotes!.Notes.Select(n => n.Id).ToArray());
        Assert.IsType<EndnoteReference>(Assert.Single(Refs((Paragraph)document.Body.Blocks[0])));
        Assert.Equal(new StoryRef(StoryKind.Note, "endnote:1"), result.Caret.Story);
        Assert.NotNull(document.Styles!.Find("EndnoteReference"));
        Assert.NotNull(document.Styles.Find("EndnoteText"));
        Assert.Null(document.Styles.Find("FootnoteText"));
    }

    [Fact]
    public void TheSecondNoteGetsAFreshId()
    {
        var document = WithStyles(Line("one two three"), footnotes: FootnotesPart(Footnote(4, "existing")));
        var editor = Editor(document);

        var first = editor.InsertNote(At(document, 0, 3), NoteType.Footnote);
        var second = editor.InsertNote(At(document, 0, 9), NoteType.Footnote);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(5, first.Objects!.NotesAdded[0].Id);
        Assert.Equal(6, second.Objects!.NotesAdded[0].Id);
        Assert.Equal([5, 6], Refs((Paragraph)document.Body.Blocks[0]).Cast<FootnoteReference>().Select(r => r.Id).ToArray());
        Assert.Equal([-1, 0, 4, 5, 6], document.Footnotes!.Notes.Select(n => n.Id).ToArray());
        Assert.Null(second.Style);
    }

    [Fact]
    public void UndoRemovesNoteAndReferenceTogether()
    {
        var document = WithStyles(Line("hello world"));
        var editor = Editor(document);
        var result = editor.InsertNote(At(document, 0, 5), NoteType.Footnote)!;
        var note = result.Objects!.NotesAdded[0];
        var revision = document.Edits.Revision;

        var undone = editor.History.Undo();

        Assert.NotNull(undone);
        Assert.Empty(Refs((Paragraph)document.Body.Blocks[0]));
        Assert.Equal("hello world", Raw((Paragraph)document.Body.Blocks[0]));
        Assert.Null(document.Footnotes!.Find(1));
        Assert.Same(note, Assert.Single(undone.Objects!.NotesRemoved));
        Assert.Empty(undone.Objects.NotesAdded);
        Assert.True(document.Edits.Revision < revision);
        Assert.Equal(StoryRef.Body, undone.Caret.Story);

        var redone = editor.History.Redo();

        Assert.NotNull(redone);
        Assert.Same(note, document.Footnotes.Find(1));
        Assert.Same(note, Assert.Single(redone.Objects!.NotesAdded));
        Assert.Equal(1, Assert.IsType<FootnoteReference>(Assert.Single(Refs((Paragraph)document.Body.Blocks[0]))).Id);
        Assert.Equal([-1, 0, 1], document.Footnotes.Notes.Select(n => n.Id).ToArray());
    }

    [Fact]
    public async Task DeletingTheReferenceDeletesTheNote()
    {
        var document = WithStyles(Line("hello world"));
        var layout = Laid(document);
        var editor = new TextEditor(document, (s, p) => layout.ProjectionHost(s, p));
        var inserted = editor.InsertNote(At(document, 0, 5), NoteType.Footnote)!;
        var note = inserted.Objects!.NotesAdded[0];
        layout.Restyle(5);
        await layout.LayOutRestAsync(null, Cancel);
        var paragraph = (Paragraph)document.Body.Blocks[0];

        // The layout numbered the reference: it is one character "1" at offset 5.
        var result = editor.Delete(new TextRange(new TextPosition(StoryRef.Body, paragraph, 4), new TextPosition(StoryRef.Body, paragraph, 7)));

        Assert.Empty(Refs((Paragraph)document.Body.Blocks[0]));
        Assert.Null(document.Footnotes!.Find(1));
        Assert.Same(note, Assert.Single(result.Objects!.NotesRemoved));

        var undone = editor.History.Undo();
        Assert.NotNull(undone);
        Assert.Same(note, document.Footnotes.Find(1));
        Assert.Equal(1, Assert.IsType<FootnoteReference>(Assert.Single(Refs((Paragraph)document.Body.Blocks[0]))).Id);

        var redone = editor.History.Redo();
        Assert.NotNull(redone);
        Assert.Null(document.Footnotes.Find(1));
        Assert.Empty(Refs((Paragraph)document.Body.Blocks[0]));
    }

    [Fact]
    public async Task DeletingOneOfTwoReferencesToTheSameNoteKeepsTheNote()
    {
        // A foreign file can hold two references to one note id; removing the note would leave the other reference dangling (Word repairs such a file).
        var document = WithStyles(Line("hello") + "<w:p><w:r><w:t>a</w:t></w:r>" + ChromeTestKit.FootnoteRef(1) + "<w:r><w:t>b</w:t></w:r></w:p>" + "<w:p><w:r><w:t>c</w:t></w:r>" + ChromeTestKit.FootnoteRef(1) + "</w:p>",
            footnotes: FootnotesPart(Footnote(1, "note")));
        var layout = Laid(document);
        var editor = new TextEditor(document, (s, p) => layout.ProjectionHost(s, p));
        await layout.LayOutRestAsync(null, Cancel);
        var first = (Paragraph)document.Body.Blocks[1];

        // The first reference is the one character after "a".
        var result = editor.Delete(new TextRange(new TextPosition(StoryRef.Body, first, 1), new TextPosition(StoryRef.Body, first, 2)));

        Assert.NotNull(result);
        Assert.Empty(Refs((Paragraph)document.Body.Blocks[1]));
        Assert.Single(Refs((Paragraph)document.Body.Blocks[2]));
        Assert.NotNull(document.Footnotes!.Find(1));
        Assert.True(result.Objects is null || result.Objects.NotesRemoved.Count == 0);
    }

    [Fact]
    public void ANotesPartWithTheHighestPossibleIdTakesNoMoreNotesAndNothingOverflows()
    {
        var document = WithStyles(Line("hello world"), footnotes: FootnotesPart(Footnote(int.MaxValue, "hostile")));
        var editor = Editor(document);
        var revision = document.Edits.Revision;

        Assert.True(editor.NoteIdsUsedUp(NoteType.Footnote));
        Assert.False(editor.NoteIdsUsedUp(NoteType.Endnote));
        Assert.Null(editor.InsertNote(At(document, 0, 5), NoteType.Footnote));

        Assert.Equal(revision, document.Edits.Revision);
        Assert.Empty(Refs((Paragraph)document.Body.Blocks[0]));
        Assert.Equal([-1, 0, int.MaxValue], document.Footnotes!.Notes.Select(n => n.Id).ToArray());
        Assert.False(editor.History.CanUndo);

        // An endnote still gets its own id.
        Assert.NotNull(editor.InsertNote(At(document, 0, 5), NoteType.Endnote));
    }

    [Fact]
    public async Task ReplacingASelectionThatHoldsAReferenceDeletesItsNote()
    {
        var document = WithStyles(Line("hello world"));
        var layout = Laid(document);
        var editor = new TextEditor(document, (s, p) => layout.ProjectionHost(s, p));
        editor.InsertNote(At(document, 0, 5), NoteType.Footnote);
        layout.Restyle(5);
        await layout.LayOutRestAsync(null, Cancel);
        var paragraph = (Paragraph)document.Body.Blocks[0];

        var result = editor.InsertText(new TextRange(new TextPosition(StoryRef.Body, paragraph, 2), new TextPosition(StoryRef.Body, paragraph, 9)), "X");

        Assert.Null(document.Footnotes!.Find(1));
        Assert.Single(result.Objects!.NotesRemoved);
        Assert.Empty(Refs((Paragraph)document.Body.Blocks[0]));
        Assert.NotNull(editor.History.Undo());
        Assert.NotNull(document.Footnotes.Find(1));
    }

    [Fact]
    public void AFootnoteInATableCellIsAllowed()
    {
        var table = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"4000\"/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w=\"4000\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>cell text</w:t></w:r></w:p></w:tc></w:tr></w:tbl>" + Line("after");
        var document = WithStyles(table);
        var cell = ((Table)document.Body.Blocks[0]).Rows.OfType<TableRow>().First().Items.OfType<TableCell>().First();
        var paragraph = (Paragraph)cell.Blocks[0];

        var result = Editor(document).InsertNote(new TextPosition(StoryRef.Body, paragraph, 4), NoteType.Footnote);

        Assert.NotNull(result);
        Assert.Single(Refs(paragraph));
        Assert.Equal("cell text", Raw(paragraph));
    }

    [Fact]
    public void RefusedInHeaderFooterNoteCommentAndRestrictedControls()
    {
        var restricted = "<w:sdt><w:sdtPr><w:text/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>plain</w:t></w:r></w:p></w:sdtContent></w:sdt>";
        var rich = "<w:sdt><w:sdtPr/><w:sdtContent><w:p><w:r><w:t>rich</w:t></w:r></w:p></w:sdtContent></w:sdt>";
        var inline = "<w:p><w:r><w:t>a</w:t></w:r><w:sdt><w:sdtPr><w:dropDownList/></w:sdtPr><w:sdtContent><w:r><w:t>pick</w:t></w:r></w:sdtContent></w:sdt><w:r><w:t>z</w:t></w:r></w:p>";
        var document = WithStyles(Line("body") + restricted + rich + inline, footnotes: FootnotesPart(Footnote(1, "n")));
        document.HeadersFooters["rId9"] = StoryXml.ReadPart(Utf8(FooterPart(Para(Text("foot")))));
        var editor = Editor(document);
        var footer = (Paragraph)document.HeadersFooters["rId9"].Content.Blocks[0];
        var notePara = (Paragraph)document.Footnotes!.Find(1)!.Content.Blocks[0];
        var plainPara = (Paragraph)((ContentControlBlock)document.Body.Blocks[1]).Content[0];
        var richPara = (Paragraph)((ContentControlBlock)document.Body.Blocks[2]).Content[0];
        var inlinePara = (Paragraph)document.Body.Blocks[3];

        Assert.Equal(NoteRefusal.HeaderOrFooter, editor.RefusalForNote(new TextPosition(new StoryRef(StoryKind.HeaderFooter, "rId9"), footer, 0)));
        Assert.Equal(NoteRefusal.Note, editor.RefusalForNote(new TextPosition(new StoryRef(StoryKind.Note, "1"), notePara, 0)));
        Assert.Equal(NoteRefusal.Comment, NoteRules.Refusal(document, new TextPosition(new StoryRef(StoryKind.Comment, "3"), footer, 0)));
        Assert.Equal(NoteRefusal.RestrictedControl, editor.RefusalForNote(new TextPosition(StoryRef.Body, plainPara, 2)));
        Assert.Null(editor.RefusalForNote(new TextPosition(StoryRef.Body, richPara, 2)));
        Assert.Null(editor.RefusalForNote(new TextPosition(StoryRef.Body, inlinePara, 0)));
        Assert.Equal(NoteRefusal.RestrictedControl, editor.RefusalForNote(new TextPosition(StoryRef.Body, inlinePara, 2)));

        var revision = document.Edits.Revision;
        Assert.Null(editor.InsertNote(new TextPosition(new StoryRef(StoryKind.HeaderFooter, "rId9"), footer, 0), NoteType.Footnote));
        Assert.Null(editor.InsertNote(new TextPosition(new StoryRef(StoryKind.Note, "1"), notePara, 0), NoteType.Footnote));
        Assert.Null(editor.InsertNote(new TextPosition(StoryRef.Body, plainPara, 2), NoteType.Endnote));
        Assert.Null(editor.InsertNote(new TextPosition(StoryRef.Body, inlinePara, 2), NoteType.Footnote));
        Assert.Equal(revision, document.Edits.Revision);
        Assert.Equal([-1, 0, 1], document.Footnotes.Notes.Select(n => n.Id).ToArray());
        Assert.Null(document.Endnotes);
        Assert.NotNull(editor.InsertNote(new TextPosition(StoryRef.Body, richPara, 2), NoteType.Footnote));
    }

    [Fact]
    public void PrefixDocumentRefuses()
    {
        var document = WithStyles(Line("hello"));
        var editor = Editor(document);
        var caret = At(document, 0, 2);

        Assert.Equal(NoteRefusal.PrefixDocument, editor.RefusalForNote(caret, prefixDocument: true));
        Assert.Null(editor.InsertNote(caret, NoteType.Footnote, prefixDocument: true));
        Assert.Null(document.Footnotes);
        Assert.NotNull(editor.InsertNote(caret, NoteType.Footnote));
    }

    [Fact]
    public void TheStylesAreCompanionsOfTheStep()
    {
        var document = WithStyles(Line("hello world"));
        var editor = Editor(document);

        var first = editor.InsertNote(At(document, 0, 5), NoteType.Footnote)!;

        Assert.NotNull(first.Style);
        Assert.Equal(["FootnoteReference", "FootnoteText"], first.Style.All.Select(s => s.New.Id).ToArray());
        Assert.All(first.Style.All, s => Assert.Equal(s.Old.Id, s.New.Id));
        var undone = editor.History.Undo()!;
        Assert.NotNull(undone.Style);
        Assert.Equal(2, undone.Style.All.Count());
        Assert.Equal(0, editor.History.UndoCount);
    }

    [Fact]
    public void StylesThatExistAreKeptAsTheyAre()
    {
        var existing = "<w:style w:type=\"character\" w:styleId=\"FootnoteReference\"><w:name w:val=\"footnote reference\"/><w:rPr><w:color w:val=\"FF0000\"/></w:rPr></w:style>";
        var document = WithStyles(Line("hello world"), Normal + existing);
        var before = document.Styles!.Find("FootnoteReference");

        var result = Editor(document).InsertNote(At(document, 0, 5), NoteType.Footnote)!;

        Assert.Same(before, document.Styles.Find("FootnoteReference"));
        Assert.Equal(["FootnoteText"], result.Style!.All.Select(s => s.New.Id).ToArray());
    }

    [Fact]
    public void ADocumentWithoutAStyleSheetGetsADirectSuperscript()
    {
        var document = Document(Line("hello world"));

        var result = Editor(document).InsertNote(At(document, 0, 5), NoteType.Footnote)!;

        Assert.Null(result.Style);
        var run = ((Paragraph)document.Body.Blocks[0]).Inlines.OfType<Run>().Single(r => r.Content.Any(c => c is FootnoteReference));
        Assert.Null(run.Properties.StyleId);
        Assert.Equal("superscript", (string?)run.Properties.Get(System.Xml.Linq.XName.Get("vertAlign", LayoutModel.W))?.Attribute(System.Xml.Linq.XName.Get("val", LayoutModel.W)));
    }

    [Fact]
    public async Task ABlankDocumentWithAFootnoteSavesAndReopens()
    {
        using var folder = new TempFolder();
        var target = folder.File("note.docx");
        using (var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4)))
        {
            var editor = new TextEditor(file.Model, (_, _) => ProjectionHost.Plain);
            var paragraph = file.Model.Body.Blocks.OfType<Paragraph>().First();
            var typed = editor.InsertText(new TextRange(new TextPosition(StoryRef.Body, paragraph, 0), new TextPosition(StoryRef.Body, paragraph, 0)), "Some text");
            Assert.NotNull(editor.InsertNote(new TextPosition(StoryRef.Body, typed.Caret.Paragraph, 4), NoteType.Footnote));
            await file.SaveAsync(target, Cancel);
        }

        using var reopened = DocxFile.Open(target);
        Assert.Equal([-1, 0, 1], reopened.Model.Footnotes!.Notes.Select(n => n.Id).ToArray());
        Assert.NotNull(reopened.Model.Styles!.Find("FootnoteReference"));
        Assert.NotNull(reopened.Model.Styles.Find("FootnoteText"));
        Assert.Single(Refs(reopened.Model.Body.Blocks.OfType<Paragraph>().First(p => Refs(p).Count > 0)));
    }
}
