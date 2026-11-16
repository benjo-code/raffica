using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Editing;

public sealed class UpdateStyleToMatchTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private const string Styles =
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>"
        + "<w:style w:type=\"paragraph\" w:styleId=\"Body\"><w:name w:val=\"Body\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Body\"/><w:link w:val=\"BodyChar\"/><w:pPr><w:spacing w:before=\"40\"/></w:pPr><w:rPr><w:sz w:val=\"20\"/></w:rPr></w:style>"
        + "<w:style w:type=\"paragraph\" w:styleId=\"Other\"><w:name w:val=\"Other\"/></w:style>";

    private const string TwoBody =
        "<w:p><w:pPr><w:pStyle w:val=\"Body\"/><w:jc w:val=\"center\"/><w:keepNext/></w:pPr>"
        + "<w:r><w:rPr><w:b/><w:color w:val=\"FF0000\"/><w:i/></w:rPr><w:t>one </w:t></w:r><w:r><w:rPr><w:b/><w:color w:val=\"FF0000\"/></w:rPr><w:t>two</w:t></w:r></w:p>"
        + "<w:p><w:pPr><w:pStyle w:val=\"Body\"/><w:jc w:val=\"right\"/></w:pPr><w:r><w:rPr><w:b/></w:rPr><w:t>three</w:t></w:r></w:p>";

    private static (WordDocument Document, TextEditor Editor) Setup(string body, string? styles = Styles)
    {
        var document = LayoutModel.Document(body, styles is null ? null : LayoutModel.StylesPart(styles));
        return (document, new TextEditor(document, (_, _) => ProjectionHost.Plain));
    }

    private static Paragraph P(WordDocument document, int index) => (Paragraph)document.Body.Blocks[index];

    private static int Length(Paragraph p) => ParagraphProjection.Build(p, ProjectionHost.Plain).Text.Length;

    private static TextRange Whole(WordDocument document, int first, int last) =>
        new(new TextPosition(StoryRef.Body, P(document, first), 0), new TextPosition(StoryRef.Body, P(document, last), Length(P(document, last))));

    [Fact]
    public void DirectOverridesMoveIntoTheStyleAndLeaveTheParagraph()
    {
        var (document, editor) = Setup(TwoBody);
        var result = editor.UpdateStyleToMatch(Whole(document, 0, 0));
        Assert.NotNull(result);
        Assert.NotNull(result.Style);
        var style = document.Styles!.Find("Body")!;
        Assert.Equal("center", (string?)style.ParagraphProperties.Get(W + "jc")?.Attribute(W + "val"));
        Assert.NotNull(style.ParagraphProperties.Get(W + "keepNext"));
        Assert.Equal("40", (string?)style.ParagraphProperties.Get(W + "spacing")?.Attribute(W + "before"));
        Assert.Null(P(document, 0).Properties.Get(W + "jc"));
        Assert.Null(P(document, 0).Properties.Get(W + "keepNext"));
        Assert.Equal("Body", P(document, 0).Properties.StyleId);
        Assert.Equal("20", (string?)style.RunProperties.Get(W + "sz")?.Attribute(W + "val"));
    }

    [Fact]
    public void OnlyCharacterOverridesOnEveryRunMove()
    {
        var (document, editor) = Setup(TwoBody);
        Assert.NotNull(editor.UpdateStyleToMatch(Whole(document, 0, 0)));
        var style = document.Styles!.Find("Body")!;
        Assert.NotNull(style.RunProperties.Get(W + "b"));
        Assert.Equal("FF0000", (string?)style.RunProperties.Get(W + "color")?.Attribute(W + "val"));
        Assert.Null(style.RunProperties.Get(W + "i"));
        var runs = P(document, 0).Inlines.OfType<Run>().ToList();
        Assert.All(runs, r => Assert.Null(r.Properties.Get(W + "b")));
        Assert.All(runs, r => Assert.Null(r.Properties.Get(W + "color")));
        Assert.NotNull(runs[0].Properties.Get(W + "i"));
    }

    [Fact]
    public void OtherParagraphsOfTheStyleKeepTheirDirectFormatting()
    {
        var (document, editor) = Setup(TwoBody);
        Assert.NotNull(editor.UpdateStyleToMatch(Whole(document, 0, 0)));
        Assert.Equal("right", (string?)P(document, 1).Properties.Get(W + "jc")?.Attribute(W + "val"));
        Assert.NotNull(((Run)P(document, 1).Inlines[0]).Properties.Get(W + "b"));
    }

    [Fact]
    public void BasedOnNextAndLinkAreUntouched()
    {
        var (document, editor) = Setup(TwoBody);
        Assert.NotNull(editor.UpdateStyleToMatch(Whole(document, 0, 0)));
        var style = document.Styles!.Find("Body")!;
        Assert.Equal("Normal", style.BasedOn);
        Assert.Equal("Body", style.Next);
        Assert.Equal("BodyChar", style.Link);
        Assert.Empty(document.Styles.Find("Other")!.ParagraphProperties.Children);
    }

    [Fact]
    public void TheStyleKeepsItsPlaceInTheSheet()
    {
        var (document, editor) = Setup(TwoBody);
        var index = document.Styles!.Styles.ToList().FindIndex(s => s.Id == "Body");
        Assert.NotNull(editor.UpdateStyleToMatch(Whole(document, 0, 0)));
        Assert.Equal(index, document.Styles.Styles.ToList().FindIndex(s => s.Id == "Body"));
    }

    [Fact]
    public void AMixedStyleSelectionIsRefused()
    {
        var (document, editor) = Setup(TwoBody + "<w:p><w:pPr><w:pStyle w:val=\"Other\"/></w:pPr><w:r><w:t>x</w:t></w:r></w:p>");
        var revision = document.Edits.Revision;
        Assert.Null(editor.UpdateStyleToMatch(Whole(document, 0, 2)));
        Assert.Equal(revision, document.Edits.Revision);
        Assert.NotNull(P(document, 0).Properties.Get(W + "jc"));
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void AnUndefinedStyleIsRefused()
    {
        var (document, editor) = Setup("<w:p><w:pPr><w:pStyle w:val=\"Nope\"/><w:jc w:val=\"center\"/></w:pPr><w:r><w:t>x</w:t></w:r></w:p>");
        Assert.Null(editor.UpdateStyleToMatch(Whole(document, 0, 0)));
        Assert.NotNull(P(document, 0).Properties.Get(W + "jc"));
    }

    [Fact]
    public void AFileWithoutAStylesPartIsRefused()
    {
        var (document, editor) = Setup("<w:p><w:pPr><w:jc w:val=\"center\"/></w:pPr><w:r><w:t>x</w:t></w:r></w:p>", null);
        Assert.Null(editor.UpdateStyleToMatch(Whole(document, 0, 0)));
        Assert.NotNull(P(document, 0).Properties.Get(W + "jc"));
    }

    [Fact]
    public void UndoPutsBackTheStyleAndTheParagraphInOneStep()
    {
        var (document, editor) = Setup(TwoBody);
        Assert.NotNull(editor.UpdateStyleToMatch(Whole(document, 0, 0)));
        Assert.Equal(1, editor.History.UndoCount);
        Assert.NotNull(editor.History.Undo());
        Assert.Null(document.Styles!.Find("Body")!.ParagraphProperties.Get(W + "jc"));
        Assert.NotNull(P(document, 0).Properties.Get(W + "jc"));
        Assert.NotNull(editor.History.Redo());
        Assert.NotNull(document.Styles.Find("Body")!.ParagraphProperties.Get(W + "jc"));
        Assert.Null(P(document, 0).Properties.Get(W + "jc"));
    }

    private const string RoundTripBody =
        "<w:p><w:pPr><w:pStyle w:val=\"Body\"/><w:jc w:val=\"center\"/><w:keepNext/></w:pPr><w:r><w:rPr><w:b/></w:rPr><w:t>one</w:t></w:r></w:p>"
        + "<w:p><w:pPr><w:pStyle w:val=\"Body\"/><w:jc w:val=\"right\"/><w:keepNext/></w:pPr><w:r><w:rPr><w:i/></w:rPr><w:t>two</w:t></w:r></w:p>";

    private static string? Val(PropertyBag bag, string name) => (string?)bag.Get(W + name)?.Attribute(W + "val");

    // jc, keepNext and the run flags of the live style, then of the two paragraphs and their runs.
    private static string State(WordDocument document)
    {
        var style = document.Styles!.Find("Body")!;
        string Of(int index)
        {
            var p = P(document, index);
            var run = (Run)p.Inlines[0];
            return $"jc={Val(p.Properties, "jc")} kn={p.Properties.Get(W + "keepNext") is not null} b={run.Properties.Get(W + "b") is not null} i={run.Properties.Get(W + "i") is not null}";
        }

        return $"style[jc={Val(style.ParagraphProperties, "jc")} kn={style.ParagraphProperties.Get(W + "keepNext") is not null} b={style.RunProperties.Get(W + "b") is not null} i={style.RunProperties.Get(W + "i") is not null}] p0[{Of(0)}] p1[{Of(1)}]";
    }

    [Fact]
    public void UpdateUpdateUndoUndoRedoRedoOnTheSameStyle()
    {
        var (document, editor) = Setup(RoundTripBody);
        var start = State(document);
        Assert.NotNull(editor.UpdateStyleToMatch(Whole(document, 0, 0)));
        var first = State(document);
        Assert.Equal("style[jc=center kn=True b=True i=False] p0[jc= kn=False b=False i=False] p1[jc=right kn=True b=False i=True]", first);
        Assert.NotNull(editor.UpdateStyleToMatch(Whole(document, 1, 1)));
        var second = State(document);
        Assert.Equal("style[jc=right kn=True b=True i=True] p0[jc= kn=False b=False i=False] p1[jc= kn=False b=False i=False]", second);
        Assert.Equal(2, editor.History.UndoCount);

        Assert.NotNull(editor.History.Undo());
        Assert.Equal(first, State(document));
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(start, State(document));
        Assert.Equal(["Normal", "Body", "Other"], document.Styles!.Styles.Select(s => s.Id));

        Assert.NotNull(editor.History.Redo());
        Assert.Equal(first, State(document));
        Assert.NotNull(editor.History.Redo());
        Assert.Equal(second, State(document));
    }

    [Fact]
    public void TheSnapshotsDoNotAliasTheLiveStyle()
    {
        var (document, editor) = Setup(RoundTripBody);
        Assert.NotNull(editor.UpdateStyleToMatch(Whole(document, 0, 0)));
        var first = State(document);
        Assert.NotNull(editor.UpdateStyleToMatch(Whole(document, 1, 1)));
        var second = State(document);

        var undone = editor.History.Undo();
        Assert.Equal(first, State(document));
        var live = document.Styles!.Find("Body")!;
        Assert.NotSame(undone!.Style!.Old, live);
        Assert.NotSame(undone.Style.New, live);

        // The live style changes after the undo (not an edit of the history's document revision): a redo still puts back the recorded New.
        live.ParagraphProperties.Set(W + "jc", new XElement(W + "jc", new XAttribute(W + "val", "left")));
        live.RunProperties.Set(W + "u", new XElement(W + "u", new XAttribute(W + "val", "single")));
        var redone = editor.History.Redo();
        Assert.NotNull(redone);
        Assert.Equal(second, State(document));
        Assert.Null(document.Styles.Find("Body")!.RunProperties.Get(W + "u"));
        var liveAfter = document.Styles.Find("Body")!;
        Assert.NotSame(redone.Style!.New, liveAfter);
        Assert.NotSame(redone.Style.Old, liveAfter);

        // Changing the live style also leaves the snapshot of the step as recorded: the undo after it restores the recorded Old.
        liveAfter.ParagraphProperties.Set(W + "jc", new XElement(W + "jc", new XAttribute(W + "val", "justify")));
        Assert.Equal("right", Val(redone.Style.New.ParagraphProperties, "jc"));
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(first, State(document));
    }

    [Fact]
    public void ARealUpdateRaisesTheRevisionOnce()
    {
        var (document, editor) = Setup(RoundTripBody);
        var before = document.Edits.Revision;
        Assert.NotNull(editor.UpdateStyleToMatch(Whole(document, 0, 0)));
        Assert.Equal(before + 1, document.Edits.Revision);
    }

    [Fact]
    public void UndoOfARealUpdateToTheSavedRevisionIsCleanAgain()
    {
        var (document, editor) = Setup(RoundTripBody);
        document.Edits.MarkSaved();
        Assert.NotNull(editor.UpdateStyleToMatch(Whole(document, 0, 0)));
        Assert.True(document.Edits.IsDirty);
        Assert.NotNull(editor.History.Undo());
        Assert.False(document.Edits.IsDirty);
        Assert.NotNull(editor.History.Redo());
        Assert.True(document.Edits.IsDirty);
        Assert.NotNull(editor.History.Undo());
        Assert.False(document.Edits.IsDirty);
    }

    [Fact]
    public void ARealUpdateIsClearedWhenTheDocumentChangesBehindTheHistory()
    {
        var (document, editor) = Setup(RoundTripBody);
        Assert.NotNull(editor.UpdateStyleToMatch(Whole(document, 0, 0)));
        var updated = State(document);

        // A model edit and a style change that bypass the history, then the revision rises as every real edit would.
        P(document, 1).Properties.SetFlag(W + "contextualSpacing", true);
        document.Styles!.Find("Body")!.ParagraphProperties.Set(W + "jc", new XElement(W + "jc", new XAttribute(W + "val", "both")));
        document.Edits.Raise();
        var changed = State(document);

        Assert.False(editor.History.CanUndo);
        Assert.Null(editor.History.Undo());
        Assert.Null(editor.History.Redo());
        Assert.Equal(0, editor.History.UndoCount);
        Assert.Equal(changed, State(document));
        Assert.NotEqual(updated, changed);
        Assert.Equal("both", Val(document.Styles.Find("Body")!.ParagraphProperties, "jc"));
        Assert.NotNull(P(document, 1).Properties.Get(W + "contextualSpacing"));
    }
}
