using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>The labels and note marks the field refresh reads from the layout's own definitions equal the independent reference count (milestone 4.5b, task 6).</summary>
public sealed class FieldLabelsTests
{
    private static string Root => Path.Combine(Raffica.TestSupport.TestData.Root, "testdata", "write-corpus");

    private static string P(string inner) => $"<w:p>{inner}</w:p>";

    [Fact]
    public void TheLabelsEqualTheReferenceNumberingOnEveryCorpusFileWithAList()
    {
        var files = 0;
        var labelled = 0;
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            using var file = DocxFile.Open(Path.Combine(Root, entry.Id, entry.Id + ".docx"));
            var expected = ReferenceNumbering.Labels(file.Model);
            if (expected.Count == 0)
            {
                continue;
            }

            var labels = new FieldLabels(file.Model);
            foreach (var (paragraph, label) in expected)
            {
                Assert.True(label == labels.NumberLabel(paragraph), $"{entry.Id}: the label of a numbered paragraph differs");
                labelled++;
            }

            files++;
        }

        Assert.True(files > 0 && labelled > 0, "the corpus has files with lists");
    }

    [Fact]
    public void AParagraphInNoListIsAnsweredWithoutWalkingTheBody()
    {
        var model = LayoutModel.Document(P("<w:r><w:t>one</w:t></w:r>") + P("<w:r><w:t>two</w:t></w:r>") + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>");
        var labels = new FieldLabels(model);

        Assert.Null(labels.NumberLabel((Paragraph)model.Body.Blocks[1]));
        Assert.Equal(0, labels.LabelBuilds);
    }

    [Fact]
    public void NoteMarksCountPerKindInReadingOrderInTheFormatOfTheDocument()
    {
        var model = LayoutModel.Document(
            P("<w:r><w:footnoteReference w:id=\"5\"/></w:r>") + P("<w:r><w:endnoteReference w:id=\"3\"/></w:r>") + P("<w:r><w:footnoteReference w:id=\"2\"/></w:r><w:r><w:endnoteReference w:id=\"9\"/></w:r>")
                + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>");
        var labels = new FieldLabels(model);

        Assert.Equal("1", labels.NoteMarkText(5, NoteType.Footnote));
        Assert.Equal("2", labels.NoteMarkText(2, NoteType.Footnote));
        Assert.Equal("i", labels.NoteMarkText(3, NoteType.Endnote));
        Assert.Equal("ii", labels.NoteMarkText(9, NoteType.Endnote));
        Assert.Null(labels.NoteMarkText(5, NoteType.Endnote));
        Assert.Null(labels.NoteMarkText(77, NoteType.Footnote));
    }

    [Fact]
    public void AnEditMakesTheMarksBeWorkedOutAgain()
    {
        var model = LayoutModel.Document(P("<w:r><w:footnoteReference w:id=\"5\"/></w:r>") + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>");
        var labels = new FieldLabels(model);
        Assert.Equal("1", labels.NoteMarkText(5, NoteType.Footnote));

        // A reference in front of it: the revision rises and the mark of the old one moves.
        var front = LayoutModel.Document(P("<w:r><w:footnoteReference w:id=\"8\"/></w:r>")).Body.Blocks[0].Clone();
        model.Body.Blocks.Insert(0, front);
        model.Edits.Raise();

        Assert.Equal("1", labels.NoteMarkText(8, NoteType.Footnote));
        Assert.Equal("2", labels.NoteMarkText(5, NoteType.Footnote));
    }

    private sealed class LabelsHost(FieldLabels labels) : IFieldHost
    {
        public bool IsComplete => true;

        public string? NumberLabel(Paragraph paragraph) => labels.NumberLabel(paragraph);

        public (int PageIndex, string PageText)? PageOf(TextPosition position) => null;

        public string? NoteMarkText(int noteId, NoteType type) => labels.NoteMarkText(noteId, type);
    }

    // A numbered Heading1 and a paragraph with STYLEREF 1 \s whose stored result is wrong.
    private static WordDocument NumberedHeadings()
    {
        var styles = LayoutModel.StylesPart(
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>"
            + "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:pPr><w:numPr><w:numId w:val=\"1\"/></w:numPr><w:outlineLvl w:val=\"0\"/></w:pPr></w:style>");
        var numbering = LayoutModel.NumberingPart(
            "<w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>");
        var field = "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> STYLEREF 1 \\s </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>7</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";
        return LayoutModel.Document(
            "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:t>Chapter</w:t></w:r></w:p>" + P("<w:r><w:t xml:space=\"preserve\">In </w:t></w:r>" + field) + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>",
            styles,
            numbering);
    }

    [Fact]
    public void TheLabelsOfAnUndoneEditAreFoundForTheParagraphsTheUndoPutBack()
    {
        var model = NumberedHeadings();
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain, format: new LayoutEffectiveFormat(model));
        var labels = new FieldLabels(model);
        var host = new LabelsHost(labels);
        Assert.True(editor.RefreshFields(host, RefreshScope.OrderBased).Written > 0);
        Assert.Equal("1.", labels.NumberLabel((Paragraph)model.Body.Blocks[0]));

        // An edit of the heading and its undo before any refresh looked: the undo puts a clone of the heading back, and the revision is the one the labels were worked out for.
        var heading = (Paragraph)model.Body.Blocks[0];
        var caret = new TextPosition(StoryRef.Body, heading, 0);
        Assert.NotNull(editor.InsertText(new TextRange(caret, caret), "x"));
        Assert.NotNull(editor.History.Undo());
        Assert.NotSame(heading, model.Body.Blocks[0]);

        Assert.Equal("1.", labels.NumberLabel((Paragraph)model.Body.Blocks[0]));
        var refreshed = editor.RefreshFields(host, RefreshScope.OrderBased);
        Assert.Equal(0, refreshed.NotUpdated);
    }
}
