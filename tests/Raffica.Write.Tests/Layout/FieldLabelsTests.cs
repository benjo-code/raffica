using Raffica.Write.Docx;
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
}
