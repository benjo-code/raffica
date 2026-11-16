using Raffica.Write.Layout;
using Raffica.Write.Model;
using Xunit;
using static Raffica.Write.Tests.Layout.ChromeTestKit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Layout;

/// <summary>The transient preedit of an input method composition (Task 18 of milestone 4.3a): laid out inside the paragraph, never in the model.</summary>
public sealed class PreeditLayoutTests
{
    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("lorem ipsum dolor", count));

    private static (WordDocument Document, DocumentLayout Layout) Lay(string body) =>
        LayWith(LayoutModel.Document(body + Sect()));

    private static (WordDocument Document, DocumentLayout Layout) LayWith(WordDocument document) =>
        (document, Finish(DocumentLayout.Create(document, Fonts, new LayoutOptions())));

    private static Paragraph Para(WordDocument document, int index) => (Paragraph)document.Body.Blocks[index];

    private static TextPosition At(Paragraph paragraph, int offset) => new(StoryRef.Body, paragraph, offset);

    [Fact]
    public void PreeditIsLaidOutInsideTheParagraphAndTheModelIsUntouched()
    {
        var (document, layout) = Lay(P("alpha beta gamma"));
        var paragraph = Para(document, 0);
        var revision = document.Edits.Revision;
        var edits = layout.Stats.Edits;

        Assert.True(layout.SetPreedit(At(paragraph, 6), "XYZ"));

        Assert.Contains("alpha XYZbeta gamma", layout.Page(0).Text, StringComparison.Ordinal);
        Assert.Equal(revision, document.Edits.Revision);
        Assert.Equal(edits, layout.Stats.Edits);
        Assert.Equal("alpha beta gamma", ParagraphProjection.Build(paragraph, layout.ProjectionHost(StoryRef.Body, paragraph)).Text);
        Assert.Equal("XYZ", layout.PreeditText);
        Assert.Equal(At(paragraph, 6), layout.PreeditAt);
    }

    [Fact]
    public void ClearingThePreeditGivesTheLayoutOfTheModel()
    {
        var (document, layout) = Lay(P(Words(40)) + P("second") + P(Words(60)));
        var paragraph = Para(document, 0);

        layout.SetPreedit(At(paragraph, 20), "composition of some length " + Words(8));
        Assert.NotEqual(Finish(DocumentLayout.Create(document, Fonts, new LayoutOptions())).Page(0).Text, layout.Page(0).Text);
        layout.SetPreedit(At(paragraph, 20), null);

        Assert.Null(layout.PreeditText);
        LayoutOracle.AssertEqualToFull(layout, document, label: "after the preedit was cleared");
    }

    [Fact]
    public void ALongPreeditReflowsTheParagraphAndThePagesAfterIt()
    {
        var (document, layout) = Lay(Repeat("<w:p><w:r><w:t>" + Words(12) + "</w:t></w:r></w:p>", 40));
        var before = Pages(layout).Sum(p => p.TextLines.Length);
        var paragraph = Para(document, 0);

        layout.SetPreedit(At(paragraph, 3), Words(30));
        var during = Pages(Finish(layout)).Sum(p => p.TextLines.Length);
        layout.SetPreedit(At(paragraph, 3), null);

        Assert.True(during > before);
        Assert.Equal(before, Pages(Finish(layout)).Sum(p => p.TextLines.Length));
        LayoutOracle.AssertEqualToFull(layout, document, label: "reflow");
    }

    [Fact]
    public void ThePreeditIsUnderlinedAndTheRestIsNot()
    {
        var (document, layout) = Lay(P("alpha beta gamma"));
        Assert.Empty(Rules(layout.Page(0)));

        layout.SetPreedit(At(Para(document, 0), 6), "XYZ");
        Assert.NotEmpty(Rules(layout.Page(0)));

        layout.SetPreedit(At(Para(document, 0), 6), null);
        Assert.Empty(Rules(layout.Page(0)));
    }

    [Fact]
    public void OffsetsAfterThePreeditAreModelOffsets()
    {
        var (document, layout) = Lay(P("alpha beta gamma"));
        var paragraph = Para(document, 0);
        var plain = layout.Caret(At(paragraph, 11), 0)!.Value;

        layout.SetPreedit(At(paragraph, 6), "XYZXYZ");

        var moved = layout.Caret(At(paragraph, 11), 0)!.Value;
        Assert.True(moved.X > plain.X + 10f);
        var hit = layout.HitTest(0, moved.X + 0.5f, moved.Top + (moved.Height / 2f))!.Value;
        Assert.Equal(11, hit.Position.Offset);

        // A point on the preedit itself is a position next to it, never an offset inside it.
        var preeditX = layout.PreeditCaret(3, 0)!.Value.X;
        var on = layout.HitTest(0, preeditX, moved.Top + (moved.Height / 2f))!.Value;
        Assert.InRange(on.Position.Offset, 5, 6);
    }

    [Fact]
    public void ThePreeditCaretFollowsTheCursorInsideThePreedit()
    {
        var (document, layout) = Lay(P("alpha beta gamma"));
        var paragraph = Para(document, 0);
        layout.SetPreedit(At(paragraph, 6), "XYZXYZ");

        var first = layout.PreeditCaret(0, 0)!.Value;
        var middle = layout.PreeditCaret(3, 0)!.Value;
        var last = layout.PreeditCaret(6, 0)!.Value;

        Assert.True(first.X < middle.X && middle.X < last.X);
        Assert.Equal(first.Top, last.Top);
        // Beyond the text clamps to its end; no preedit gives no caret.
        Assert.Equal(last, layout.PreeditCaret(99, 0)!.Value);
        layout.SetPreedit(At(paragraph, 6), null);
        Assert.Null(layout.PreeditCaret(0, 0));
    }

    [Fact]
    public void ThePreeditCaretIsOnTheLaterLineAfterAWrap()
    {
        var (document, layout) = Lay(P("start"));
        var paragraph = Para(document, 0);
        layout.SetPreedit(At(paragraph, 5), Words(30));

        var first = layout.PreeditCaret(0, 0)!.Value;
        var end = layout.PreeditCaret(Words(30).Length, 0)!.Value;

        Assert.True(end.Top > first.Top);
    }

    [Fact]
    public void AMovedPreeditCleansTheParagraphItLeft()
    {
        var (document, layout) = Lay(P(Words(30)) + P(Words(30)));
        layout.SetPreedit(At(Para(document, 0), 4), "AAA");
        layout.SetPreedit(At(Para(document, 1), 4), "BBB");

        Assert.DoesNotContain("AAA", layout.Page(0).Text, StringComparison.Ordinal);
        Assert.Contains("BBB", layout.Page(0).Text, StringComparison.Ordinal);
        layout.SetPreedit(At(Para(document, 1), 4), null);
        LayoutOracle.AssertEqualToFull(layout, document, label: "moved preedit");
    }

    [Fact]
    public void ThePreeditStandsAtTheEndAndInAnEmptyParagraph()
    {
        var (document, layout) = Lay(P("abc") + "<w:p/>" + P("tail"));
        layout.SetPreedit(At(Para(document, 0), 3), "END");
        layout.SetPreedit(At(Para(document, 1), 0), "EMPTY");

        var text = layout.Page(0).Text;
        Assert.Contains("EMPTY", text, StringComparison.Ordinal);
        Assert.DoesNotContain("abcEND", text, StringComparison.Ordinal);
        layout.SetPreedit(At(Para(document, 1), 0), null);
        layout.SetPreedit(At(Para(document, 0), 3), "END");
        Assert.Contains("abcEND", layout.Page(0).Text, StringComparison.Ordinal);
        layout.SetPreedit(At(Para(document, 0), 3), null);
        LayoutOracle.AssertEqualToFull(layout, document, label: "end and empty");
    }

    [Fact]
    public void ThePreeditShowsInATableCell()
    {
        var cell = (string text) => $"<w:tc><w:tcPr><w:tcW w:w=\"3000\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:tc>";
        var table = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"3000\"/><w:gridCol w:w=\"3000\"/></w:tblGrid><w:tr>" + cell("alpha") + cell("beta") + "</w:tr></w:tbl>";
        var document = LayoutModel.Document(P("before") + table + P("after") + Sect());
        var layout = Finish(DocumentLayout.Create(document, Fonts, new LayoutOptions()));
        var beta = layout.Page(0).TextLines.First(l => l.Projection.Text == "beta");

        Assert.True(layout.SetPreedit(new TextPosition(beta.Story, beta.Paragraph, 4), "XY"));
        Assert.Contains("betaXY", layout.Page(0).Text, StringComparison.Ordinal);
        layout.SetPreedit(new TextPosition(beta.Story, beta.Paragraph, 4), null);

        LayoutOracle.AssertEqualToFull(layout, document, label: "table cell");
    }

    [Fact]
    public void ThePreeditShowsInAHeader()
    {
        var parts = new Dictionary<string, string> { ["hD"] = HeaderPart(Line("HEADER")) };
        var section = Tall(4, Header("default", "hD"));
        var document = Document(Repeat(Line("body"), 3) + PEnd("body last", section), parts);
        var layout = Laid(document);
        var header = layout.Page(0).TextLines.First(l => l.Story.Kind == StoryKind.HeaderFooter);
        var position = new TextPosition(header.Story, header.Paragraph, 6);

        Assert.True(layout.SetPreedit(position, "IME"));
        Assert.Contains("HEADERIME", layout.Page(0).Text, StringComparison.Ordinal);
        layout.SetPreedit(position, null);

        LayoutOracle.AssertEqualToFull(layout, document, label: "header");
    }

    [Fact]
    public void APositionOutsideTheModelIsRefused()
    {
        var (_, layout) = Lay(P("alpha"));
        var other = LayoutModel.Document(P("other") + Sect());

        Assert.False(layout.SetPreedit(At(Para(other, 0), 0), "X"));
        Assert.Null(layout.PreeditText);
    }

    [Fact]
    public void ControlCharactersAreNeverLaidOut()
    {
        var (document, layout) = Lay(P("alpha"));

        layout.SetPreedit(At(Para(document, 0), 5), "a\nb\tc\u0007");

        Assert.Equal("abc", layout.PreeditText);
        Assert.Contains("alphaabc", layout.Page(0).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyPreeditIsNoPreedit()
    {
        var (document, layout) = Lay(P("alpha"));
        var revision = layout.Revision;

        Assert.False(layout.SetPreedit(At(Para(document, 0), 2), ""));
        Assert.False(layout.SetPreedit(At(Para(document, 0), 2), null));

        Assert.Equal(revision, layout.Revision);
    }
}
