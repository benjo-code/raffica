using System.Diagnostics;
using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using WriteCorpus;
using Xunit;
using static Raffica.Write.Tests.Layout.ChromeTestKit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Layout;

/// <summary>Hit testing, caret geometry and caret movement over the text lines of the pages.</summary>
public sealed class TextGeometryTests
{
    private static readonly Lazy<(DocxFile Docx, DocumentLayout Layout)> s_thesis = new(() =>
    {
        var docx = DocxFile.Open(Path.Combine(TestData.Root, "testdata", "write-corpus", "thesis", "thesis.docx"));
        var layout = DocumentLayout.Create(docx.Model, Fonts, new LayoutOptions());
        layout.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        return (docx, layout);
    });

    private static TextPosition Pos(Paragraph paragraph, int offset) => new(StoryRef.Body, paragraph, offset);

    private static (WordDocument Document, DocumentLayout Layout) Lay(string body) => LayWith(LayoutModel.Document(body + Sect()));

    private static (WordDocument Document, DocumentLayout Layout) LayWith(WordDocument document) =>
        (document, Finish(DocumentLayout.Create(document, Fonts, new LayoutOptions())));

    private static Paragraph[] Paragraphs(WordDocument document) => [.. document.Body.Blocks.OfType<Paragraph>()];

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("lorem ipsum dolor", count));

    // Every text line of the layout with its page, in reading order.
    private static List<(int Page, TextLine Line)> AllLines(DocumentLayout layout)
    {
        var all = new List<(int, TextLine)>();
        for (var i = 0; i < layout.LaidOutPages; i++)
        {
            all.AddRange(layout.Page(i).TextLines.Select(l => (i, l)));
        }

        return all;
    }

    // Seeded points that lie on the text of a line: x between the first and the last caret stop.
    private static List<(int Page, float X, float Y, TextLine Line)> TextPoints(DocumentLayout layout, int count, int seed)
    {
        var random = new Random(seed);
        var points = new List<(int, float, float, TextLine)>();
        while (points.Count < count)
        {
            var page = random.Next(layout.LaidOutPages);
            var lines = layout.Page(page).TextLines;
            if (lines.Length == 0)
            {
                continue;
            }

            var line = lines[random.Next(lines.Length)];
            var stops = line.Stops;
            var left = stops.Xs[0];
            var right = stops.XAt(stops.VisibleEnd);
            if (right - left < 1f)
            {
                continue;
            }

            points.Add((page, left + ((float)random.NextDouble() * (right - left)), line.Top + ((float)random.NextDouble() * line.Height), line));
        }

        return points;
    }

    [Fact]
    public void StoryKeysFollowTheirConvention()
    {
        var parts = new Dictionary<string, string> { ["rIdHead"] = HeaderPart(Line("head text")) };
        var section = Tall(8, Header("default", "rIdHead"));
        var document = Document(Para(Text("one") + FootnoteRef(5) + EndnoteRef(5)) + PEnd("two", section), parts, FootnotesPart(Footnote(5, "foot")), EndnotesPart(Endnote(5, "end")));
        var layout = Laid(document);
        var stories = AllLines(layout).Select(l => l.Line.Story).Distinct().ToList();

        Assert.Contains(StoryRef.Body, stories);
        Assert.Contains(new StoryRef(StoryKind.Note, "5"), stories);
        Assert.Contains(new StoryRef(StoryKind.Note, "endnote:5"), stories);
        Assert.Contains(new StoryRef(StoryKind.HeaderFooter, "rIdHead"), stories);
        Assert.Equal(4, stories.Count);
    }

    [Fact]
    public void HitTestThenCaretRoundTrips()
    {
        var layout = s_thesis.Value.Layout;
        var points = TextPoints(layout, 1000, 4242);
        var checkedSamples = 0;
        foreach (var (page, x, y, line) in points)
        {
            var hit = layout.HitTest(page, x, y);
            Assert.NotNull(hit);
            Assert.Equal(page, hit.Value.Page);
            Assert.Same(line.Paragraph, hit.Value.Position.Paragraph);
            var caret = layout.Caret(hit.Value.Position, page);
            Assert.NotNull(caret);

            // A position where a line wrapped without a space belongs to the next line (no affinity): not a sample.
            if (hit.Value.Position.Offset == line.End && line.End < line.Projection.Text.Length)
            {
                continue;
            }

            checkedSamples++;
            Assert.Equal(line.Top, caret.Value.Top);
            Assert.Equal(line.Height, caret.Value.Height);
            Assert.Equal(page, caret.Value.Page);
            var gaps = new List<float>();
            foreach (var move in new[] { CaretMove.Left, CaretMove.Right })
            {
                var next = layout.Move(hit.Value.Position, move, null, page);
                var other = next is { } p ? layout.Caret(p, page) : null;
                if (other is { } c && c.Top == caret.Value.Top)
                {
                    gaps.Add(Math.Abs(c.X - caret.Value.X));
                }
            }

            var allowed = (gaps.Count == 0 ? line.Height : gaps.Max()) / 2f;
            Assert.True(Math.Abs(caret.Value.X - x) <= allowed + 0.05f, $"page {page}: x {x}, caret {caret.Value.X}, allowed {allowed}");
        }

        Assert.True(checkedSamples > 900, $"only {checkedSamples} samples checked");
    }

    [Fact]
    public void HitTestOutsideTheTextFindsTheNearestLine()
    {
        var (document, layout) = Lay(P("first line") + P("second line"));
        var paragraphs = Paragraphs(document);
        var lines = layout.Page(0).TextLines;

        var above = layout.HitTest(0, 100f, 0f);
        Assert.Same(paragraphs[0], above!.Value.Position.Paragraph);
        var below = layout.HitTest(0, 100f, 790f);
        Assert.Same(paragraphs[1], below!.Value.Position.Paragraph);

        // Far right of a line: its end, trailing; far left: its start.
        var mid = lines[0].Top + (lines[0].Height / 2f);
        var right = layout.HitTest(0, 600f, mid)!.Value;
        Assert.Equal(10, right.Position.Offset);
        Assert.True(right.Trailing);
        var left = layout.HitTest(0, 0f, mid)!.Value;
        Assert.Equal(0, left.Position.Offset);
        Assert.False(left.Trailing);
        Assert.Null(layout.HitTest(5, 0f, 0f));
        Assert.Null(layout.HitTest(-1, 0f, 0f));
    }

    [Fact]
    public void RightThenLeftIsIdentity()
    {
        var emoji = "x\U0001F44D\U0001F3FDy";
        var combining = "aéb";
        var body = P("the office offers a different effort") + P(emoji) + P(combining) + Para(Text("Page ") + Field("PAGE", "12345") + Text(" end"));
        var (document, layout) = Lay(body);
        foreach (var paragraph in Paragraphs(document))
        {
            var positions = new List<TextPosition> { Pos(paragraph, 0) };
            while (positions.Count < 200)
            {
                var next = layout.Move(positions[^1], CaretMove.Right, null, 0)!.Value;
                if (!ReferenceEquals(next.Paragraph, paragraph) || next.Offset == positions[^1].Offset)
                {
                    break;
                }

                positions.Add(next);
            }

            Assert.True(positions.Count > 2);
            for (var i = 1; i < positions.Count; i++)
            {
                Assert.Equal(positions[i - 1], layout.Move(positions[i], CaretMove.Left, null, 0)!.Value);
            }
        }

        // One visible character is one step: the emoji with its skin tone, the letter with its accent, and the whole field result.
        var paragraphs = Paragraphs(document);
        Assert.Equal(1, layout.Move(Pos(paragraphs[1], 0), CaretMove.Right, null, 0)!.Value.Offset);
        Assert.Equal(1 + "\U0001F44D\U0001F3FD".Length, layout.Move(Pos(paragraphs[1], 1), CaretMove.Right, null, 0)!.Value.Offset);
        Assert.Equal(1 + 2, layout.Move(Pos(paragraphs[2], 1), CaretMove.Right, null, 0)!.Value.Offset);
        Assert.Equal(1, layout.Move(Pos(paragraphs[2], 3), CaretMove.Left, null, 0)!.Value.Offset);
        Assert.Equal(10, layout.Move(Pos(paragraphs[3], 5), CaretMove.Right, null, 0)!.Value.Offset);
        Assert.Equal(5, layout.Move(Pos(paragraphs[3], 10), CaretMove.Left, null, 0)!.Value.Offset);
    }

    [Fact]
    public void UpAndDownKeepTheGoalColumn()
    {
        var (document, layout) = Lay(P(Words(40)) + P("ab") + P(Words(40)));
        var paragraphs = Paragraphs(document);
        var lines = layout.Page(0).TextLines;
        var firstLong = lines.Where(l => ReferenceEquals(l.Paragraph, paragraphs[0])).ToList();
        var secondLong = lines.Where(l => ReferenceEquals(l.Paragraph, paragraphs[2])).ToList();
        Assert.True(firstLong.Count > 3);

        var start = Pos(paragraphs[0], firstLong[1].Start + 12);
        var goal = layout.Caret(start, 0)!.Value.X;
        var down = layout.Move(start, CaretMove.Down, goal, 0)!.Value;
        Assert.Equal(firstLong[2].Top, layout.Caret(down, 0)!.Value.Top);
        Assert.InRange(Math.Abs(layout.Caret(down, 0)!.Value.X - goal), 0f, 6f);

        // Through the short paragraph and out of it: the goal survives, the column of the short line does not.
        var position = Pos(paragraphs[0], firstLong[^1].Start + 12);
        var x = layout.Caret(position, 0)!.Value.X;
        var intoShort = layout.Move(position, CaretMove.Down, x, 0)!.Value;
        Assert.Same(paragraphs[1], intoShort.Paragraph);
        Assert.Equal(2, intoShort.Offset);
        var past = layout.Move(intoShort, CaretMove.Down, x, 0)!.Value;
        Assert.Same(paragraphs[2], past.Paragraph);
        Assert.Equal(secondLong[0].Top, layout.Caret(past, 0)!.Value.Top);
        Assert.InRange(Math.Abs(layout.Caret(past, 0)!.Value.X - x), 0f, 6f);

        var back = layout.Move(past, CaretMove.Up, x, 0)!.Value;
        Assert.Same(paragraphs[1], back.Paragraph);
        var upAgain = layout.Move(back, CaretMove.Up, x, 0)!.Value;
        Assert.Same(paragraphs[0], upAgain.Paragraph);
        Assert.Equal(firstLong[^1].Top, layout.Caret(upAgain, 0)!.Value.Top);

        // No line above the first: the start of the line; none below the last: the end of the text.
        Assert.Equal(0, layout.Move(Pos(paragraphs[0], 5), CaretMove.Up, null, 0)!.Value.Offset);
        var lastLine = secondLong[^1];
        Assert.Equal(paragraphs[2], layout.Move(Pos(paragraphs[2], lastLine.Start + 3), CaretMove.Down, null, 0)!.Value.Paragraph);
        Assert.Equal(lastLine.Projection.Text.Length, layout.Move(Pos(paragraphs[2], lastLine.Start + 3), CaretMove.Down, null, 0)!.Value.Offset);
    }

    [Fact]
    public void LineStartAndLineEndStayOnTheLine()
    {
        var (document, layout) = Lay(P(Words(60)));
        var paragraph = Paragraphs(document)[0];
        var lines = layout.Page(0).TextLines;
        Assert.True(lines.Length > 4);
        for (var k = 0; k < lines.Length; k++)
        {
            var line = lines[k];
            var inside = Pos(paragraph, line.Start + ((line.End - line.Start) / 2));
            var start = layout.Move(inside, CaretMove.LineStart, null, 0)!.Value;
            var end = layout.Move(inside, CaretMove.LineEnd, null, 0)!.Value;
            Assert.Equal(line.Start, start.Offset);
            Assert.Equal(line.Top, layout.Caret(start, 0)!.Value.Top);
            Assert.Equal(line.Top, layout.Caret(end, 0)!.Value.Top);
            if (k < lines.Length - 1)
            {
                // The line ends before the space that wrapped.
                Assert.Equal(line.End - 1, end.Offset);
                Assert.Equal(' ', line.Projection.Text[line.End - 1]);
            }
            else
            {
                Assert.Equal(line.Projection.Text.Length, end.Offset);
                Assert.Equal(line.Projection.Text.Length, line.End);
            }
        }

        // The end of the last line is a real position to the right of its last glyph.
        var last = lines[^1];
        var endCaret = layout.Caret(Pos(paragraph, last.Projection.Text.Length), 0)!.Value;
        var lastRun = last.Runs.Last(r => !r.IsLeader && !r.IsHyphen);
        Assert.InRange(endCaret.X, last.X + lastRun.X + lastRun.Width - 0.5f, last.X + lastRun.X + lastRun.Width + 0.5f);
    }

    [Fact]
    public void WordRightFromHelloWorldGoesToSix()
    {
        var (document, layout) = Lay(P("Hello world, it's a test."));
        var paragraph = Paragraphs(document)[0];
        int Move(int from, CaretMove move) => layout.Move(Pos(paragraph, from), move, null, 0)!.Value.Offset;

        Assert.Equal(6, Move(0, CaretMove.WordRight));
        Assert.Equal(11, Move(6, CaretMove.WordRight));
        Assert.Equal(13, Move(11, CaretMove.WordRight));
        Assert.Equal(6, Move(11, CaretMove.WordLeft));
        Assert.Equal(0, Move(6, CaretMove.WordLeft));
        Assert.Equal(0, Move(3, CaretMove.WordLeft));
        Assert.Equal(6, Move(8, CaretMove.WordLeft));
        Assert.Equal(24, Move(20, CaretMove.WordRight));
        Assert.Equal(25, Move(24, CaretMove.WordRight));
        Assert.Equal(0, Move(7, CaretMove.ParagraphStart));
        Assert.Equal(25, Move(7, CaretMove.ParagraphEnd));
    }

    [Fact]
    public void MoveNeverLeavesTheStory()
    {
        var section = Tall(8);
        var document = Document(Para(Text("body text") + FootnoteRef(3)) + PEnd("more body", section), footnotes: FootnotesPart(Footnote(3, "note words here")));
        var layout = Laid(document);
        var noteLines = layout.Page(0).TextLines.Where(l => l.Story.Kind == StoryKind.Note).ToList();
        Assert.NotEmpty(noteLines);
        var note = noteLines[0];
        var story = note.Story;

        var position = new TextPosition(story, note.Paragraph, 0);
        for (var i = 0; i < 60; i++)
        {
            position = layout.Move(position, CaretMove.Right, null, 0)!.Value;
            Assert.Equal(story, position.Story);
        }

        Assert.Equal(note.Projection.Text.Length, position.Offset);
        Assert.Equal(position, layout.Move(position, CaretMove.Right, null, 0)!.Value);
        Assert.Equal(position, layout.Move(position, CaretMove.Down, null, 0)!.Value);
        Assert.Equal(position, layout.Move(position, CaretMove.DocumentEnd, null, 0)!.Value);
        var start = layout.Move(position, CaretMove.DocumentStart, null, 0)!.Value;
        Assert.Equal(0, start.Offset);
        Assert.Equal(story, start.Story);
        Assert.Equal(start, layout.Move(start, CaretMove.Left, null, 0)!.Value);
        Assert.Equal(start, layout.Move(start, CaretMove.Up, null, 0)!.Value);

        // The body is not reachable from the note and the note not from the body.
        var bodyEnd = layout.Move(new TextPosition(StoryRef.Body, Paragraphs(document)[0], 0), CaretMove.DocumentEnd, null, 0)!.Value;
        Assert.Equal(StoryRef.Body, bodyEnd.Story);
        Assert.Same(Paragraphs(document)[1], bodyEnd.Paragraph);
        Assert.Equal("more body".Length, bodyEnd.Offset);
    }

    [Fact]
    public void MoveNeverLeavesATableCell()
    {
        var cell = (string text) => $"<w:tc><w:tcPr><w:tcW w:w=\"3000\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:tc>";
        var table = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"3000\"/><w:gridCol w:w=\"3000\"/></w:tblGrid><w:tr>" + cell("alpha") + cell("beta") + "</w:tr></w:tbl>";
        var document = LayoutModel.Document(P("before") + table + P("after") + Sect());
        var layout = Finish(DocumentLayout.Create(document, Fonts, new LayoutOptions()));
        var lines = layout.Page(0).TextLines;
        var alpha = lines.First(l => l.Projection.Text == "alpha");
        var beta = lines.First(l => l.Projection.Text == "beta");
        Assert.NotNull(alpha.Cell);
        Assert.NotSame(alpha.Cell, beta.Cell);

        TextPosition At(TextLine line, int offset) => new(line.Story, line.Paragraph, offset);
        Assert.Equal(At(alpha, 5), layout.Move(At(alpha, 5), CaretMove.Right, null, 0)!.Value);
        Assert.Equal(At(beta, 0), layout.Move(At(beta, 0), CaretMove.Left, null, 0)!.Value);
        Assert.Equal(At(alpha, 5), layout.Move(At(alpha, 2), CaretMove.DocumentEnd, null, 0)!.Value);
        Assert.Equal(At(alpha, 0), layout.Move(At(alpha, 2), CaretMove.DocumentStart, null, 0)!.Value);
        Assert.Equal(At(alpha, 5), layout.Move(At(alpha, 2), CaretMove.Down, null, 0)!.Value);

        // From outside, a table can be entered (and the paragraph before and after are reachable on the way).
        var before = lines.First(l => l.Projection.Text == "before");
        Assert.Equal(At(alpha, 0), layout.Move(At(before, 6), CaretMove.Right, null, 0)!.Value);
        Assert.Equal(At(alpha, 0), layout.Move(At(alpha, 0), CaretMove.Left, null, 0)!.Value);
    }

    [Fact]
    public void SelectionBoxesCoverTwoLines()
    {
        var (document, layout) = Lay(P(Words(40)));
        var paragraph = Paragraphs(document)[0];
        var lines = layout.Page(0).TextLines;
        var from = Pos(paragraph, lines[0].Start + 8);
        var to = Pos(paragraph, lines[1].Start + 10);

        var boxes = layout.SelectionBoxes(new TextRange(from, to), 0);
        Assert.Equal(2, boxes.Count);
        Assert.Equal(layout.Caret(from, 0)!.Value.X, boxes[0].X, 0.01f);
        Assert.Equal(lines[0].Top, boxes[0].Y);
        Assert.Equal(lines[1].Top, boxes[1].Y);
        Assert.Equal(layout.Caret(to, 0)!.Value.X, boxes[1].X + boxes[1].Width, 0.01f);
        Assert.Equal(lines[1].X + lines[1].ContentLeft, boxes[1].X, 0.5f);
        Assert.True(boxes[0].Width > boxes[1].Width);

        // The same range given backwards, and a range on another page.
        Assert.Equal(boxes, layout.SelectionBoxes(new TextRange(to, from), 0));
        Assert.Empty(layout.SelectionBoxes(new TextRange(from, to), 3));
        Assert.Empty(layout.SelectionBoxes(new TextRange(from, from), 0));
    }

    [Fact]
    public void TheEndsOfParagraphsAreRealPositions()
    {
        var (document, layout) = Lay(P("first paragraph") + P("second one"));
        var paragraphs = Paragraphs(document);
        var lines = layout.Page(0).TextLines;

        Assert.Equal(Pos(paragraphs[1], 0), layout.Move(Pos(paragraphs[0], 15), CaretMove.Right, null, 0)!.Value);
        Assert.Equal(Pos(paragraphs[0], 15), layout.Move(Pos(paragraphs[1], 0), CaretMove.Left, null, 0)!.Value);
        Assert.Equal(Pos(paragraphs[1], 10), layout.Move(Pos(paragraphs[1], 10), CaretMove.Right, null, 0)!.Value);
        Assert.Equal(Pos(paragraphs[1], 10), layout.Move(Pos(paragraphs[0], 3), CaretMove.DocumentEnd, null, 0)!.Value);
        Assert.Equal(Pos(paragraphs[0], 0), layout.Move(Pos(paragraphs[1], 4), CaretMove.DocumentStart, null, 0)!.Value);
        Assert.Equal(Pos(paragraphs[1], 10), layout.Move(Pos(paragraphs[1], 4), CaretMove.LineEnd, null, 0)!.Value);
        Assert.Equal(Pos(paragraphs[1], 10), layout.Move(Pos(paragraphs[1], 4), CaretMove.ParagraphEnd, null, 0)!.Value);

        // The caret at the very end stands at the right edge of the last glyph, the one at the start at the left edge of the first.
        var line = lines[1];
        var run = line.Runs.First();
        var end = layout.Caret(Pos(paragraphs[1], 10), 0)!.Value;
        Assert.Equal(line.X + run.X + run.Width, end.X, 0.01f);
        Assert.Equal(line.X + run.X, layout.Caret(Pos(paragraphs[1], 0), 0)!.Value.X, 0.01f);

        // A click right of the last character is the end, trailing; left of the first the start.
        var mid = line.Top + (line.Height / 2f);
        var hit = layout.HitTest(0, 590f, mid)!.Value;
        Assert.Equal(Pos(paragraphs[1], 10), hit.Position);
        Assert.True(hit.Trailing);
        Assert.Equal(Pos(paragraphs[1], 0), layout.HitTest(0, 1f, mid)!.Value.Position);
    }

    [Fact]
    public void AParagraphThatBeginsWithAPlacedFloatKeepsItsEnds()
    {
        var (document, layout) = Lay(Para(FloatLayoutTests.TextBoxRun() + Text("text after the float and some more words")) + P("next"));
        var paragraphs = Paragraphs(document);
        var lines = layout.Page(0).TextLines.Where(l => ReferenceEquals(l.Paragraph, paragraphs[0])).ToList();
        Assert.Equal(0, lines[0].Start);
        Assert.Equal(1, paragraphs.Length - 1);
        var length = lines[0].Projection.Text.Length;
        Assert.Equal(length, lines[^1].End);
        Assert.StartsWith(ParagraphProjection.ObjectChar.ToString(), lines[0].Projection.Text, StringComparison.Ordinal);

        // The float is one unit: step over it in both directions; the caret before and after it is at the start of the text.
        Assert.Equal(1, layout.Move(Pos(paragraphs[0], 0), CaretMove.Right, null, 0)!.Value.Offset);
        Assert.Equal(0, layout.Move(Pos(paragraphs[0], 1), CaretMove.Left, null, 0)!.Value.Offset);
        var first = lines[0].Runs.First(r => !r.IsLabel && !r.IsLeader);
        var textStart = lines[0].X + first.X;
        Assert.Equal(textStart, layout.Caret(Pos(paragraphs[0], 1), 0)!.Value.X, 0.01f);
        Assert.Equal(textStart, layout.Caret(Pos(paragraphs[0], 0), 0)!.Value.X, 1f);

        // The ends: the caret at the end, the end of the last line, a click far right on the last line, the document end.
        var endCaret = layout.Caret(Pos(paragraphs[0], length), 0)!.Value;
        Assert.Equal(lines[^1].Top, endCaret.Top);
        var lastRun = lines[^1].Runs.Last(r => !r.IsLeader);
        Assert.Equal(lines[^1].X + lastRun.X + lastRun.Width, endCaret.X, 0.5f);
        Assert.Equal(length, layout.Move(Pos(paragraphs[0], 5), CaretMove.ParagraphEnd, null, 0)!.Value.Offset);
        var mid = lines[^1].Top + (lines[^1].Height / 2f);
        Assert.Equal(length, layout.HitTest(0, 590f, mid)!.Value.Position.Offset);
        Assert.Equal(Pos(paragraphs[1], 0), layout.Move(Pos(paragraphs[0], length), CaretMove.Right, null, 0)!.Value);
    }

    [Fact]
    public void AParagraphSplitOverTwoPagesHasEndsOnBothPages()
    {
        var text = Words(120);
        var document = LayoutModel.Document(P(text) + PEnd("tail", Tall(8)));
        var layout = Finish(DocumentLayout.Create(document, Fonts, new LayoutOptions()));
        Assert.True(layout.LaidOutPages >= 2);
        var paragraph = Paragraphs(document)[0];
        var chunks = AllLines(layout).Where(l => ReferenceEquals(l.Line.Paragraph, paragraph)).ToList();
        var pagesOfParagraph = chunks.Select(c => c.Page).Distinct().ToList();
        Assert.True(pagesOfParagraph.Count >= 2, "the paragraph did not split");
        Assert.Equal(0, chunks[0].Line.Start);
        Assert.Equal(text.Length, chunks[^1].Line.End);

        // The caret at every line's start is on that line's page; the position of a continuation is found without a hint, with a wrong one and with the right one.
        foreach (var (page, line) in chunks.Where(c => c.Line.Start < c.Line.Projection.Text.Length))
        {
            var position = Pos(paragraph, line.Start);
            foreach (var hint in new[] { 0, page, layout.LaidOutPages + 5, -1 })
            {
                var caret = layout.Caret(position, hint)!.Value;
                Assert.Equal(page, caret.Page);
                Assert.Equal(line.Top, caret.Top);
            }
        }

        var lastChunk = chunks[^1];
        var end = layout.Caret(Pos(paragraph, text.Length), 0)!.Value;
        Assert.Equal(lastChunk.Page, end.Page);
        Assert.Equal(lastChunk.Line.Top, end.Top);
        Assert.Equal(text.Length, layout.Move(Pos(paragraph, 0), CaretMove.ParagraphEnd, null, 0)!.Value.Offset);

        // Moving down through the page break and up again.
        var firstOnLater = chunks.First(c => c.Page > chunks[0].Page);
        var lastOnEarlier = chunks.Last(c => c.Page == chunks[0].Page);
        var above = Pos(paragraph, lastOnEarlier.Line.Start + 4);
        var below = layout.Move(above, CaretMove.Down, null, chunks[0].Page)!.Value;
        Assert.Equal(firstOnLater.Line.Top, layout.Caret(below, chunks[0].Page)!.Value.Top);
        Assert.Equal(firstOnLater.Page, layout.Caret(below, chunks[0].Page)!.Value.Page);
        Assert.Equal(lastOnEarlier.Line.Top, layout.Caret(layout.Move(below, CaretMove.Up, null, firstOnLater.Page)!.Value, 0)!.Value.Top);

        // Right from the end of the earlier page's last line steps into the later page's first.
        var wrapped = Pos(paragraph, lastOnEarlier.Line.End);
        Assert.Equal(firstOnLater.Page, layout.Caret(wrapped, 0)!.Value.Page);
        var hit = layout.HitTest(lastOnEarlier.Page, 590f, lastOnEarlier.Line.Top + 1f)!.Value;
        Assert.Equal(lastOnEarlier.Page, hit.Page);
        Assert.True(hit.Position.Offset < lastOnEarlier.Line.End);

        // The next paragraph is reachable from the end of the continuation.
        var tail = Paragraphs(document)[1];
        Assert.Equal(Pos(tail, 0), layout.Move(Pos(paragraph, text.Length), CaretMove.Right, null, 0)!.Value);
        Assert.Equal(Pos(paragraph, text.Length), layout.Move(Pos(tail, 0), CaretMove.Left, null, 0)!.Value);
    }

    [Fact]
    public void PageStepsKeepTheColumnAndTheStory()
    {
        var document = LayoutModel.Document(P(Words(260)) + PEnd("tail", Tall(8)));
        var layout = Finish(DocumentLayout.Create(document, Fonts, new LayoutOptions()));
        Assert.True(layout.LaidOutPages >= 3);
        var paragraph = Paragraphs(document)[0];
        var line = layout.Page(1).TextLines.First(l => ReferenceEquals(l.Paragraph, paragraph));
        var position = Pos(paragraph, line.Start + 6);
        var goal = layout.Caret(position, 1)!.Value.X;

        var down = layout.Move(position, CaretMove.PageDown, goal, 1)!.Value;
        var downCaret = layout.Caret(down, 2)!.Value;
        Assert.Equal(2, downCaret.Page);
        Assert.Equal(line.Top, downCaret.Top);
        Assert.InRange(Math.Abs(downCaret.X - goal), 0f, 6f);
        var up = layout.Move(position, CaretMove.PageUp, goal, 1)!.Value;
        Assert.Equal(0, layout.Caret(up, 0)!.Value.Page);

        // On the last page there is no page below: the end of the body.
        var lastPage = layout.LaidOutPages - 1;
        var lastLine = layout.Page(lastPage).TextLines.Last(l => l.Story.Kind == StoryKind.Body);
        var end = layout.Move(new TextPosition(StoryRef.Body, lastLine.Paragraph, 0), CaretMove.PageDown, null, lastPage)!.Value;
        Assert.Equal(lastLine.Projection.Text.Length, end.Offset);
    }

    [Fact]
    public void ListLabelsAndHyphensAreNeverHit()
    {
        var layout = s_thesis.Value.Layout;
        var labelled = AllLines(layout).First(l => l.Line.Runs.Any(r => r.IsLabel));
        var label = labelled.Line.Runs.First(r => r.IsLabel);
        var y = labelled.Line.Top + (labelled.Line.Height / 2f);
        var hit = layout.HitTest(labelled.Page, labelled.Line.X + label.X + (label.Width / 2f), y)!.Value;
        Assert.Equal(labelled.Line.Start, hit.Position.Offset);
        var caret = layout.Caret(hit.Position, labelled.Page)!.Value;
        Assert.True(caret.X >= labelled.Line.X + label.X + label.Width - 0.01f, "the caret stands inside the label");

        // A hyphenated line: a click on the hyphen is the break point, never a position of its own.
        var word = "<w:p><w:r><w:rPr><w:lang w:val=\"en-US\"/></w:rPr><w:t>internationalization internationalization responsibility</w:t></w:r></w:p>";
        var hyphenated = Finish(CreateWithSettings(word + Sect(), "<w:autoHyphenation/>", new LayoutOptions(LayoutMode.Flow, 80f)));
        var hyphenLine = AllLines(hyphenated).First(l => l.Line.Runs.Any(r => r.IsHyphen));
        var run = hyphenLine.Line.Runs.First(r => r.IsHyphen);
        var onHyphen = hyphenated.HitTest(hyphenLine.Page, hyphenLine.Line.X + run.X + (run.Width / 2f), hyphenLine.Line.Top + 1f)!.Value;
        Assert.Equal(hyphenLine.Line.End, onHyphen.Position.Offset);

        // The break point is also where the next line starts (there is no caret affinity): that line owns it.
        var next = hyphenated.Caret(onHyphen.Position, hyphenLine.Page)!.Value;
        Assert.True(next.Top > hyphenLine.Line.Top);
        var inside = new TextPosition(hyphenLine.Line.Story, hyphenLine.Line.Paragraph, hyphenLine.Line.End - 1);
        Assert.Equal(hyphenLine.Line.Start, hyphenated.Move(inside, CaretMove.LineStart, null, 0)!.Value.Offset);
        var text = hyphenLine.Line.Projection.Text;
        Assert.DoesNotContain('-', text[hyphenLine.Line.Start..hyphenLine.Line.End]);
    }

    [Fact]
    public void HeadersAreFoundOnTheHintedPageAndWithoutOne()
    {
        var parts = new Dictionary<string, string> { ["hD"] = HeaderPart(Line("HEADER")) };
        var section = Tall(4, Header("default", "hD"));
        var layout = Laid(Document(Body("body", 11) + PEnd("body 12", section), parts));
        Assert.Equal(3, layout.LaidOutPages);
        var header = layout.Page(1).TextLines.First(l => l.Story.Kind == StoryKind.HeaderFooter);
        var position = new TextPosition(header.Story, header.Paragraph, 3);
        for (var page = 0; page < 3; page++)
        {
            Assert.Equal(page, layout.Caret(position, page)!.Value.Page);
        }

        // Without a useful hint the first page that shows it is found.
        Assert.Equal(0, layout.Caret(position, -1)!.Value.Page);
        Assert.Equal(0, layout.Caret(position, 99)!.Value.Page);
        var right = layout.Move(position, CaretMove.Right, null, 2)!.Value;
        Assert.Equal(4, right.Offset);
        Assert.Equal(new TextPosition(header.Story, header.Paragraph, 6), layout.Move(position, CaretMove.DocumentEnd, null, 2)!.Value);
    }

    [Fact]
    public async Task AnIncompleteLayoutKnowsOnlyThePagesPublished()
    {
        var document = LayoutModel.Document(P(Words(300)) + PEnd("tail", Tall(8)));
        var layout = DocumentLayout.Create(document, Fonts, new LayoutOptions());
        layout.LayOutFirstPage();
        Assert.False(layout.IsComplete);
        var tail = Paragraphs(document)[1];
        var late = Pos(tail, 0);
        Assert.Null(layout.Caret(late, 0));
        Assert.Null(layout.Move(late, CaretMove.Right, null, 0));

        await layout.LayOutRestAsync(null, CancellationToken.None);
        Assert.NotNull(layout.Caret(late, 0));
        Assert.NotNull(layout.Caret(late, layout.LaidOutPages - 1));
    }

    [Fact]
    public void ARevisedPageKeepsItsParagraphsFindable()
    {
        var parts = new Dictionary<string, string> { ["hD"] = HeaderPart(Para(Text("Page ") + Field("NUMPAGES", "9"))) };
        var section = Tall(4, Header("default", "hD"));
        var layout = Laid(Document(Body("body", 11) + PEnd("body 12", section), parts));
        Assert.Equal(1, layout.Revision);
        var header = layout.Page(2).TextLines.First(l => l.Story.Kind == StoryKind.HeaderFooter);
        for (var page = 0; page < 3; page++)
        {
            Assert.NotNull(layout.Caret(new TextPosition(header.Story, header.Paragraph, 2), page));
        }
    }

    [Fact]
    public async Task TheNavigatorIsSafeWhileTheLayoutRuns()
    {
        var document = LayoutModel.Document(string.Concat(Enumerable.Range(0, 400).Select(i => P($"paragraph {i} " + Words(8)))) + Sect());
        var layout = DocumentLayout.Create(document, Fonts, new LayoutOptions());
        layout.LayOutFirstPage();
        var paragraphs = Paragraphs(document);
        var task = layout.LayOutRestAsync(null, CancellationToken.None);
        var found = 0;
        while (!task.IsCompleted)
        {
            foreach (var paragraph in paragraphs.Take(80))
            {
                if (layout.Caret(Pos(paragraph, 2), 0) is not null)
                {
                    found++;
                }
            }

            _ = layout.HitTest(0, 100f, 100f);
        }

        await task;
        Assert.True(found > 0);
        Assert.All(paragraphs, p => Assert.NotNull(layout.Caret(Pos(p, 2), 0)));
    }

    [Fact]
    public async Task EveryCorpusParagraphEndsWhereItsGlyphsEnd()
    {
        // The last line of a paragraph is forced to end at the projection length; this checks that it does not hide a wrong mapping: the end
        // of the last glyph run, translated through the map, may differ from the length only by characters the layout draws nothing for
        // (a placed float, a hidden break, a break at the end).
        var root = Path.Combine(TestData.Root, "testdata", "write-corpus");
        var problems = new List<string>();
        var checkedParagraphs = 0;
        foreach (var entry in CorpusManifest.Load(root).Files)
        {
            using var docx = DocxFile.Open(Path.Combine(root, entry.Id, entry.Id + ".docx"));
            var layout = DocumentLayout.Create(docx.Model, Fonts, new LayoutOptions());
            await layout.LayOutRestAsync(null, CancellationToken.None);
            foreach (var (page, line) in AllLines(layout))
            {
                var text = line.Projection.Text;
                var runs = line.Runs.Where(r => !r.IsLabel && !r.IsLeader && !r.IsHyphen && r.CaretStops.Count > 0).ToList();
                if (line.End < text.Length || runs.Count == 0)
                {
                    continue;
                }

                checkedParagraphs++;
                var mapped = line.Map.Map(runs.Max(r => r.TextEnd));
                var rest = text.AsSpan(Math.Min(mapped, text.Length));
                if (mapped > text.Length || rest.Trim("\n\r\f \t￼").Length != 0)
                {
                    problems.Add($"{entry.Id} page {page}: glyphs end at {mapped} of {text.Length}");
                }
            }
        }

        Assert.True(checkedParagraphs > 1000, $"only {checkedParagraphs} last lines");
        Assert.True(problems.Count == 0, $"{problems.Count} problems, first: {string.Join(" | ", problems.Take(10))}");
    }

    [Fact]
    public void HitTestIsFast()
    {
        var layout = s_thesis.Value.Layout;
        var points = TextPoints(layout, 10_000, 99);
        foreach (var (page, x, y, _) in points.Take(2000))
        {
            _ = layout.HitTest(page, x, y);
        }

        var ticks = new long[points.Count];
        for (var i = 0; i < points.Count; i++)
        {
            var (page, x, y, _) = points[i];
            var start = Stopwatch.GetTimestamp();
            _ = layout.HitTest(page, x, y);
            ticks[i] = Stopwatch.GetTimestamp() - start;
        }

        Array.Sort(ticks);
        var median = ticks[ticks.Length / 2] * 1_000_000d / Stopwatch.Frequency;
        Console.WriteLine($"hit test median {median:F2} us over {ticks.Length} calls");
        Assert.True(median < 50d, $"hit test median {median:F2} us");
    }
}
