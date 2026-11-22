using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Editing;

public sealed class OutlineLevelTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static string Styles()
    {
        var text = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>"
            + "<w:style w:type=\"paragraph\" w:styleId=\"Chapter\"><w:name w:val=\"Chapter\"/><w:basedOn w:val=\"Heading1\"/></w:style>"
            + "<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:tblPr/></w:style>";
        for (var i = 1; i <= 9; i++)
        {
            text += $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{i}\"><w:name w:val=\"heading {i}\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:outlineLvl w:val=\"{i - 1}\"/></w:pPr></w:style>";
        }

        return LayoutModel.StylesPart(text);
    }

    private static string H(int level, string text) => $"<w:p><w:pPr><w:pStyle w:val=\"Heading{level}\"/></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>";

    private static string Body(string text) => $"<w:p><w:r><w:t>{text}</w:t></w:r></w:p>";

    private static string Direct(int level, string text, string? style = null) =>
        $"<w:p><w:pPr>{(style is null ? string.Empty : $"<w:pStyle w:val=\"{style}\"/>")}<w:outlineLvl w:val=\"{level}\"/></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>";

    private static string Custom(string text) => $"<w:p><w:pPr><w:pStyle w:val=\"Chapter\"/></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>";

    private static (WordDocument Document, TextEditor Editor) Setup(string body, string? numbering = null)
    {
        var document = LayoutModel.Document(body, Styles(), numbering);
        return (document, new TextEditor(document, (_, _) => ProjectionHost.Plain, format: new LayoutEffectiveFormat(document)));
    }

    private static Paragraph P(WordDocument document, int index) => (Paragraph)document.Body.Blocks[index];

    private static string?[] Styled(WordDocument document) => document.Body.Blocks.OfType<Paragraph>().Select(p => p.Properties.StyleId).ToArray();

    private static List<string> Snapshot(WordDocument document) =>
        document.Body.Blocks.Select(b => b is Paragraph p ? string.Concat(p.Properties.Children.Select(c => c.ToString(SaveOptions.DisableFormatting))) : b.GetType().Name).ToList();

    [Fact]
    public void PromotingShiftsTheHeadingAndItsSubHeadingsByOne()
    {
        var (document, editor) = Setup(H(2, "A") + H(3, "B") + H(4, "C") + H(2, "D"));

        var result = editor.ShiftOutlineLevels(0, 2, -1);

        Assert.NotNull(result);
        Assert.Single(result.Changes);
        Assert.True(result.KeepSelection);
        Assert.Equal(["Heading1", "Heading2", "Heading3", "Heading2"], Styled(document));
    }

    [Fact]
    public void DemotingShiftsTheRangeDown()
    {
        var (document, editor) = Setup(H(1, "A") + H(2, "B"));

        Assert.NotNull(editor.ShiftOutlineLevels(0, 1, 1));

        Assert.Equal(["Heading2", "Heading3"], Styled(document));
    }

    [Fact]
    public void DemotingAHeading9IsRefused()
    {
        var (document, editor) = Setup(H(8, "A") + H(9, "B"));
        var before = Snapshot(document);
        var revision = document.Edits.Revision;

        Assert.Null(editor.ShiftOutlineLevels(0, 1, 1));

        Assert.Equal(before, Snapshot(document));
        Assert.Equal(revision, document.Edits.Revision);
        Assert.Equal(new OutlineShiftFacts(false, OutlineShiftReason.OutOfRange), editor.ReadOutlineShift(0, 1, 1));
    }

    [Fact]
    public void PromotingAHeading1IsRefused()
    {
        var (document, editor) = Setup(H(1, "A") + H(2, "B"));
        var before = Snapshot(document);

        Assert.Null(editor.ShiftOutlineLevels(0, 1, -1));

        Assert.Equal(before, Snapshot(document));
        Assert.Equal(new OutlineShiftFacts(false, OutlineShiftReason.OutOfRange), editor.ReadOutlineShift(0, 1, -1));
        Assert.Equal(new OutlineShiftFacts(true, OutlineShiftReason.None), editor.ReadOutlineShift(1, 1, -1));
    }

    [Fact]
    public void ADirectOutlineLevelIsRewritten()
    {
        var (document, editor) = Setup(Direct(2, "A") + Direct(3, "B"));

        Assert.NotNull(editor.ShiftOutlineLevels(0, 1, -1));

        Assert.Equal("1", P(document, 0).Properties.Get(W + "outlineLvl")!.Attribute(W + "val")!.Value);
        Assert.Equal("2", P(document, 1).Properties.Get(W + "outlineLvl")!.Attribute(W + "val")!.Value);
        Assert.Null(P(document, 0).Properties.StyleId);
    }

    [Fact]
    public void ADirectLevelThatWinsOverTheStyleIsTheOneRewritten()
    {
        var (document, editor) = Setup(Direct(4, "A", "Heading1"));

        Assert.NotNull(editor.ShiftOutlineLevels(0, 0, -1));

        Assert.Equal("3", P(document, 0).Properties.Get(W + "outlineLvl")!.Attribute(W + "val")!.Value);
        Assert.Equal("Heading1", P(document, 0).Properties.StyleId);
    }

    [Fact]
    public void AMixedSubtreeOfStyleAndDirectLevelsShifts()
    {
        var (document, editor) = Setup(H(2, "A") + Direct(2, "B") + Body("text") + H(4, "C"));

        Assert.NotNull(editor.ShiftOutlineLevels(0, 3, -1));

        Assert.Equal("Heading1", P(document, 0).Properties.StyleId);
        Assert.Equal("1", P(document, 1).Properties.Get(W + "outlineLvl")!.Attribute(W + "val")!.Value);
        Assert.Equal("Heading3", P(document, 3).Properties.StyleId);
    }

    [Fact]
    public void ACustomStyleLevelSourceIsRefusedBeforeAnyChange()
    {
        var (document, editor) = Setup(H(2, "A") + Custom("B") + H(3, "C"));
        var before = Snapshot(document);
        var revision = document.Edits.Revision;

        Assert.Null(editor.ShiftOutlineLevels(0, 2, 1));

        Assert.Equal(before, Snapshot(document));
        Assert.Equal(revision, document.Edits.Revision);
        Assert.Equal(0, editor.History.UndoCount);
        Assert.Equal(new OutlineShiftFacts(false, OutlineShiftReason.NotAHeadingStyle), editor.ReadOutlineShift(0, 2, 1));
    }

    [Fact]
    public void AMissingTargetStyleIsRefusedBeforeAnyChange()
    {
        var document = LayoutModel.Document(
            H(1, "A"),
            LayoutModel.StylesPart(
                "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>"
                + "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:pPr><w:outlineLvl w:val=\"0\"/></w:pPr></w:style>"));
        var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain, format: new LayoutEffectiveFormat(document));

        Assert.Null(editor.ShiftOutlineLevels(0, 0, 1));

        Assert.Equal("Heading1", P(document, 0).Properties.StyleId);
        Assert.Equal(OutlineShiftReason.NotAHeadingStyle, editor.ReadOutlineShift(0, 0, 1).Reason);
    }

    [Fact]
    public void OneUndoStepRestoresEveryHeading()
    {
        var (document, editor) = Setup(Body("intro") + H(2, "A") + Direct(3, "B") + H(3, "C"));
        var before = Snapshot(document);

        var result = editor.ShiftOutlineLevels(0, 3, -1);
        Assert.NotNull(result);
        var after = Snapshot(document);
        Assert.NotEqual(before, after);
        Assert.Equal(1, editor.History.UndoCount);

        Assert.NotNull(editor.History.Undo());
        Assert.Equal(before, Snapshot(document));
        Assert.NotNull(editor.History.Redo());
        Assert.Equal(after, Snapshot(document));
    }

    [Fact]
    public void BodyTextInTheRangeIsUntouched()
    {
        var (document, editor) = Setup(H(2, "A") + Body("one") + Body("two"));
        var before = Snapshot(document);

        Assert.NotNull(editor.ShiftOutlineLevels(0, 2, -1));

        Assert.Equal(before[1], Snapshot(document)[1]);
        Assert.Equal(before[2], Snapshot(document)[2]);
        Assert.Equal("Heading1", P(document, 0).Properties.StyleId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-2)]
    public void ADeltaOtherThanOneStepThrows(int delta)
    {
        var (_, editor) = Setup(H(2, "A"));

        Assert.Throws<ArgumentOutOfRangeException>(() => editor.ShiftOutlineLevels(0, 0, delta));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.ReadOutlineShift(0, 0, delta));
        Assert.Equal(0, editor.History.UndoCount);
    }

    [Fact]
    public void ARangeWithNoHeadingHasNothingToShift()
    {
        var (document, editor) = Setup(Body("one") + Body("two"));
        var revision = document.Edits.Revision;

        Assert.Null(editor.ShiftOutlineLevels(0, 1, 1));

        Assert.Equal(revision, document.Edits.Revision);
        Assert.Equal(new OutlineShiftFacts(false, OutlineShiftReason.NothingToShift), editor.ReadOutlineShift(0, 1, 1));
        Assert.Null(editor.ShiftOutlineLevels(-1, 0, 1));
        Assert.Null(editor.ShiftOutlineLevels(0, 5, 1));
    }

    [Fact]
    public void AHeadingInATableCellAndInAContentControlShifts()
    {
        var table = $"<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"4000\"/></w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w=\"4000\" w:type=\"dxa\"/></w:tcPr>{H(3, "cell")}</w:tc></w:tr></w:tbl>";
        var control = $"<w:sdt><w:sdtPr/><w:sdtContent>{H(3, "control")}</w:sdtContent></w:sdt>";
        var (document, editor) = Setup(Body("a") + table + control + Body("z"));

        var result = editor.ShiftOutlineLevels(1, 2, -1);

        Assert.NotNull(result);
        Assert.Equal(0, result.Changes[0].First);
        Assert.Equal(3, result.Changes[0].Removed);
        Assert.Same(P(document, 0), result.Caret.Paragraph);
        Assert.Equal(1, editor.History.UndoCount);
        var cell = ((TableCell)((TableRow)((Table)document.Body.Blocks[1]).Rows[0]).Items[0]).Blocks.OfType<Paragraph>().Single();
        Assert.Equal("Heading2", cell.Properties.StyleId);
        var inControl = ((ContentControlBlock)document.Body.Blocks[2]).Content.OfType<Paragraph>().Single();
        Assert.Equal("Heading2", inControl.Properties.StyleId);

        Assert.NotNull(editor.History.Undo());
        Assert.Equal("Heading3", ((TableCell)((TableRow)((Table)document.Body.Blocks[1]).Rows[0]).Items[0]).Blocks.OfType<Paragraph>().Single().Properties.StyleId);
        Assert.Equal("Heading3", ((ContentControlBlock)document.Body.Blocks[2]).Content.OfType<Paragraph>().Single().Properties.StyleId);
    }

    [Fact]
    public void UndoAndRedoPlaceTheCaretInTheBodyAndNotAtBlockZero()
    {
        var (document, editor) = Setup(Body("a") + Body("b") + H(2, "C") + Body("d"));

        Assert.NotNull(editor.ShiftOutlineLevels(2, 2, -1));
        var undone = editor.History.Undo();
        Assert.NotNull(undone);
        Assert.Same(P(document, 2), undone.Caret.Paragraph);

        var redone = editor.History.Redo();
        Assert.NotNull(redone);
        Assert.Same(P(document, 2), redone.Caret.Paragraph);
    }

    [Fact]
    public async Task WithHeadingNumbersOnTheLayoutAndTheLabelsEqualTheReferenceAfterAShift()
    {
        var (document, editor) = Setup(H(1, "A") + H(2, "B") + H(2, "C") + H(1, "D") + H(2, "E"));
        Assert.NotNull(editor.SetHeadingNumbering(true));
        var layout = DocumentLayout.Create(document, PageTestKit.Fonts, new LayoutOptions());
        await layout.LayOutRestAsync(null, CancellationToken.None);

        async Task Check(EditResult? result, string label)
        {
            Assert.NotNull(result);
            foreach (var change in result.Changes)
            {
                layout.ApplyEdit(change, 0);
            }

            await layout.LayOutRestAsync(null, CancellationToken.None);
            LayoutOracle.AssertEqualToFull(layout, document, label: label);
        }

        await Check(editor.ShiftOutlineLevels(1, 2, -1), "promote");
        Assert.Equal(["1.", "2.", "3.", "4.", "4.1."], ReferenceNumbering.Labels(document).Select(l => l.Label).ToArray());
        await Check(editor.History.Undo(), "undone");
        Assert.Equal(["1.", "1.1.", "1.2.", "2.", "2.1."], ReferenceNumbering.Labels(document).Select(l => l.Label).ToArray());
        await Check(editor.ShiftOutlineLevels(3, 4, 1), "demote");
        Assert.Equal(["1.", "1.1.", "1.2.", "1.3.", "1.3.1."], ReferenceNumbering.Labels(document).Select(l => l.Label).ToArray());
        await Check(editor.History.Undo(), "demote undone");
        Assert.Equal(["1.", "1.1.", "1.2.", "2.", "2.1."], ReferenceNumbering.Labels(document).Select(l => l.Label).ToArray());
        await Check(editor.History.Redo(), "demote redone");
        Assert.Equal(["1.", "1.1.", "1.2.", "1.3.", "1.3.1."], ReferenceNumbering.Labels(document).Select(l => l.Label).ToArray());
    }
}
