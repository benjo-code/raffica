using System.Xml.Linq;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Model;

public sealed class ParagraphProjectionTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static Run TextRun(string text)
    {
        var run = new Run();
        run.Content.Add(new TextContent(text));
        return run;
    }

    private static Run Of(params RunContent[] content)
    {
        var run = new Run();
        foreach (var c in content)
        {
            run.Content.Add(c);
        }

        return run;
    }

    private static Paragraph Para(params Inline[] inlines)
    {
        var paragraph = new Paragraph();
        foreach (var inline in inlines)
        {
            paragraph.Inlines.Add(inline);
        }

        return paragraph;
    }

    private static Inline[] ComplexField(string instruction, string result) =>
    [
        Of(new FieldChar(FieldCharKind.Begin)),
        Of(new InstructionText(" " + instruction + " ")),
        Of(new FieldChar(FieldCharKind.Separate)),
        TextRun(result),
        Of(new FieldChar(FieldCharKind.End)),
    ];

    private sealed class FakeHost : IProjectionHost
    {
        public Dictionary<string, string> Values { get; } = [];

        public HashSet<Run> Hidden { get; } = [];

        public string? FieldValue(string instruction) => Values.GetValueOrDefault(instruction.Trim());

        public string? NoteMarkText(RunContent reference) => null;

        public bool IsHidden(Run run) => Hidden.Contains(run);
    }

    [Fact]
    public void PiecesCoverRunsInOrder()
    {
        var hello = TextRun("Hello");
        var tab = Of(new TabContent());
        var world = TextRun("world");
        var projection = ParagraphProjection.Build(Para(hello, tab, world), ProjectionHost.Plain);

        Assert.Equal("Hello\tworld", projection.Text);
        Assert.Collection(
            projection.Pieces,
            p =>
            {
                Assert.IsType<TextPiece>(p);
                Assert.Same(hello, p.Run);
                Assert.Equal((0, 5), (p.Start, p.Length));
            },
            p =>
            {
                Assert.IsType<TabPiece>(p);
                Assert.Same(tab, p.Run);
                Assert.Equal((5, 1), (p.Start, p.Length));
            },
            p =>
            {
                Assert.IsType<TextPiece>(p);
                Assert.Same(world, p.Run);
                Assert.Equal((6, 5), (p.Start, p.Length));
            });
    }

    [Fact]
    public void HiddenTextHasNoLength()
    {
        var one = TextRun("one");
        var two = TextRun("two");
        var three = TextRun("three");
        var host = new FakeHost();
        host.Hidden.Add(two);
        var projection = ParagraphProjection.Build(Para(one, two, three), host);

        Assert.Equal("onethree", projection.Text);
        Assert.Equal(0, projection.Pieces[1].Length);
        Assert.Same(two, projection.Pieces[1].Run);
        Assert.Equal(3, projection.Pieces[2].Start);
    }

    [Fact]
    public void FieldResultIsOneAtomicUnit()
    {
        var inlines = new List<Inline> { TextRun("Page ") };
        inlines.AddRange(ComplexField("PAGE", "7"));
        inlines.Add(TextRun("!"));
        var paragraph = Para([.. inlines]);

        var plain = ParagraphProjection.Build(paragraph, ProjectionHost.Plain);
        Assert.Equal("Page 7!", plain.Text);
        Assert.Equal((5, 6), plain.AtomicAt(5));
        Assert.Null(plain.AtomicAt(2));
        Assert.Null(plain.AtomicAt(6));

        var host = new FakeHost();
        host.Values["PAGE"] = "123";
        var valued = ParagraphProjection.Build(paragraph, host);
        Assert.Equal("Page 123!", valued.Text);
        Assert.Equal((5, 8), valued.AtomicAt(5));
        Assert.Equal((5, 8), valued.AtomicAt(6));
        Assert.Equal((5, 8), valued.AtomicAt(7));
        Assert.Null(valued.AtomicAt(8));
    }

    [Fact]
    public void PictureBreakNoteReferenceAndCommentReferenceAreAtomic()
    {
        var picture = new Picture(new XElement(W + "drawing", new XElement(XName.Get("inline", "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"))));
        var projection = ParagraphProjection.Build(
            Para(
                Of(picture),
                Of(new BreakContent(BreakKind.Page)),
                Of(new FootnoteReference(2)),
                Of(new CommentReference(3)),
                Of(new UnknownRunContent(new UnknownContent(new XElement(W + "object"))))),
            ProjectionHost.Plain);

        Assert.Equal(5, projection.Text.Length);
        Assert.All(projection.Pieces, p => Assert.Equal(1, p.Length));
        Assert.Equal(AtomicKind.Picture, Assert.IsType<AtomicPiece>(projection.Pieces[0]).Atomic);
        Assert.Equal(BreakKind.Page, Assert.IsType<BreakPiece>(projection.Pieces[1]).Break);
        Assert.Equal(AtomicKind.NoteReference, Assert.IsType<AtomicPiece>(projection.Pieces[2]).Atomic);
        Assert.Equal(AtomicKind.CommentReference, Assert.IsType<AtomicPiece>(projection.Pieces[3]).Atomic);
        Assert.Equal(AtomicKind.Placeholder, Assert.IsType<AtomicPiece>(projection.Pieces[4]).Atomic);
        Assert.Equal((2, 3), projection.AtomicAt(2));
    }

    [Fact]
    public void TrackedInsertionIsInTrackedDeletionIsOut()
    {
        var inserted = new TrackedChange(TrackedChangeKind.Insert, new RevisionInfo(1, "a", null));
        inserted.Inlines.Add(TextRun("new"));
        var deleted = new TrackedChange(TrackedChangeKind.Delete, new RevisionInfo(2, "a", null));
        var gone = new Run();
        gone.Content.Add(new DeletedText("old"));
        deleted.Inlines.Add(gone);
        var projection = ParagraphProjection.Build(Para(TextRun("a"), inserted, deleted, TextRun("b")), ProjectionHost.Plain);

        Assert.Equal("anewb", projection.Text);
    }

    [Fact]
    public void HyperlinkAndContentControlAreFlattened()
    {
        var link = new Hyperlink();
        link.Inlines.Add(TextRun("link"));
        var control = new ContentControlInline();
        control.Inlines.Add(TextRun("ctl"));
        var projection = ParagraphProjection.Build(Para(TextRun("x"), link, control), ProjectionHost.Plain);

        Assert.Equal("xlinkctl", projection.Text);
    }

    [Fact]
    public void BookmarkAndCommentRangeMarkersAddNoText()
    {
        var projection = ParagraphProjection.Build(
            Para(new BookmarkStart(1, "b"), new CommentRangeStart(4), TextRun("hi"), new CommentRangeEnd(4), new BookmarkEnd(1)),
            ProjectionHost.Plain);

        Assert.Equal("hi", projection.Text);
        Assert.Single(projection.Pieces);
    }

    [Fact]
    public void PieceAtReturnsPieceAndInnerOffset()
    {
        var hello = TextRun("Hello");
        var tab = Of(new TabContent());
        var world = TextRun("world");
        var projection = ParagraphProjection.Build(Para(hello, tab, world), ProjectionHost.Plain);

        for (var offset = 0; offset <= projection.Text.Length; offset++)
        {
            var piece = projection.PieceAt(offset);
            Assert.NotNull(piece);
            var expected = offset < 5 ? hello : offset == 5 ? tab : world;
            Assert.Same(expected, piece.Run);
            Assert.InRange(offset - piece.Start, 0, piece.Length);
        }

        Assert.Null(ParagraphProjection.Build(Para(), ProjectionHost.Plain).PieceAt(0));
    }

    private sealed class NoRoomHost : IProjectionHost
    {
        public string? FieldValue(string instruction) => null;

        public string? NoteMarkText(RunContent reference) => null;

        public bool IsHidden(Run run) => false;

        public bool HasText(RunContent content) => content is not CommentReference;
    }

    [Fact]
    public void ContentTheHostGivesNoRoomKeepsItsPieceWithLengthZero()
    {
        var projection = ParagraphProjection.Build(Para(TextRun("ab"), Of(new CommentReference(3)), TextRun("cd")), new NoRoomHost());
        Assert.Equal("abcd", projection.Text);
        Assert.Equal(0, projection.Pieces[1].Length);
        Assert.Equal(AtomicKind.CommentReference, Assert.IsType<AtomicPiece>(projection.Pieces[1]).Atomic);
    }

    [Fact]
    public void AFieldWithoutAnEndMarkIsClosedWithTheParagraph()
    {
        var projection = ParagraphProjection.Build(
            Para(TextRun("x"), Of(new FieldChar(FieldCharKind.Begin)), Of(new InstructionText(" FOO ")), Of(new FieldChar(FieldCharKind.Separate)), TextRun("stored")),
            ProjectionHost.Plain);
        Assert.Equal("xstored", projection.Text);
        Assert.Equal((1, 7), projection.AtomicAt(3));
        Assert.Null(projection.AtomicAt(0));
    }
}
