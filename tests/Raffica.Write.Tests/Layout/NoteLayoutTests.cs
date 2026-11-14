using System.Text;
using Raffica.Write.Layout;
using Xunit;
using static Raffica.Write.Tests.Layout.ChromeTestKit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Layout;

public sealed class NoteLayoutTests
{
    private const string Break = "<w:pageBreakBefore/>";

    private static string RefLine(string text, string reference) => Para(Text(text) + reference);

    private static string Words(string word, int count)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            builder.Append(word).Append(' ');
        }

        return builder.ToString().TrimEnd();
    }

    private static int CountWord(DocumentLayout layout, string word) =>
        Pages(layout).Sum(p => p.Text.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries).Count(w => w == word));

    [Fact]
    public void FootnoteSitsAtTheBottomOfThePageOfItsReference()
    {
        var body = Body("line", 1) + RefLine("line 2", FootnoteRef(5)) + Body("more", 20) + Tall(8);
        var withNote = Laid(Document(body, footnotes: FootnotesPart(Footnote(5, "Note text one"))));
        var without = Laid(Document(Body("line", 1) + Body("line", 1) + Body("more", 20) + Tall(8)));

        var page = withNote.Page(0);
        var noteLine = "1 Note text one";
        Assert.Equal(noteLine, LastLine(page));
        Assert.Contains("line 21", Lines(page));

        // The note's room was reserved when the line was placed: fewer body lines than a page without a note.
        var bodyLines = CountLines(page, "line") + CountLines(page, "more");
        var plainLines = CountLines(without.Page(0), "line") + CountLines(without.Page(0), "more");
        Assert.True(bodyLines < plainLines);
        Assert.True(bodyLines >= plainLines - 3);

        // The note is below every body line and inside the bottom margin line; a short rule separates it.
        var baselines = Baselines(page);
        var noteBaseline = baselines[^1];
        var rule = Assert.Single(Rules(page));
        Assert.InRange(rule.Width, 143f, 145f);
        var lastBody = baselines.Where(b => b < rule.Y).Max();
        Assert.InRange(rule.Y, lastBody, noteBaseline);
        Assert.True(noteBaseline <= page.Height - 72f + 0.01f);

        // The reference mark is a small raised number.
        var runs = Runs(page);
        var mark = runs.First(r => r.Size < runs[0].Size - 1f);
        var text = runs.First(r => r.Size > mark.Size + 1f && r.Baseline > mark.Baseline && r.Baseline - mark.Baseline < 6f);
        Assert.True(mark.Baseline < text.Baseline - 1f);
        Assert.True(mark.X > text.X);
    }

    [Fact]
    public void ANoteNeverLeavesItsReferencePage()
    {
        var builder = new StringBuilder();
        var notes = new List<string>();
        for (var i = 1; i <= 40; i++)
        {
            builder.Append(i % 4 == 0 ? RefLine($"refpara {i}", FootnoteRef(i)) : Line($"plain {i}"));
            if (i % 4 == 0)
            {
                notes.Add(Footnote(i, $"note for {i}"));
            }
        }

        var layout = Laid(Document(builder + Tall(9), footnotes: FootnotesPart([.. notes])));
        Assert.True(layout.LaidOutPages >= 3);
        var total = 0;
        foreach (var page in Pages(layout))
        {
            var refs = CountLines(page, "refpara");
            var noteLines = Lines(page).Count(l => l.Contains(" note for ", StringComparison.Ordinal));
            Assert.Equal(refs, noteLines);
            total += refs;
        }

        Assert.Equal(10, total);
    }

    [Fact]
    public void FootnoteWithSeveralLinesContinuesOnTheNextPage()
    {
        var note = Footnote(2, Words("word", 400));
        var body = RefLine("first", FootnoteRef(2)) + Body("then", 14) + Tall(10);
        var layout = Laid(Document(body, footnotes: FootnotesPart(note)));
        Assert.InRange(layout.LaidOutPages, 2, 12);
        Assert.Equal(400, CountWord(layout, "word"));

        var page0 = layout.Page(0);
        Assert.Contains(Lines(page0), l => l.StartsWith("1 word", StringComparison.Ordinal));
        Assert.Equal(144f, Assert.Single(Rules(page0)).Width, 1);

        // The next page continues the note under a separator as wide as the text, and still has body text of its own.
        var page1 = layout.Page(1);
        Assert.StartsWith("word", LastLine(page1), StringComparison.Ordinal);
        Assert.Equal(468f, Assert.Single(Rules(page1)).Width, 1);
        Assert.True(CountLines(page1, "then") > 0);
        Assert.DoesNotContain(Lines(page1), l => l.StartsWith("1 word", StringComparison.Ordinal));
    }

    [Fact]
    public void ANoteBiggerThanTheDocumentStillEnds()
    {
        var note = Footnote(2, Words("word", 3000));
        var body = RefLine("only", FootnoteRef(2)) + Tall(6);
        var layout = Laid(Document(body, footnotes: FootnotesPart(note)));
        Assert.True(layout.IsComplete);
        Assert.InRange(layout.LaidOutPages, 2, 200);
        Assert.Equal(3000, CountWord(layout, "word"));
    }

    [Fact]
    public void EndnotesCollectAtTheEndOfTheDocument()
    {
        var body = RefLine("body 1", EndnoteRef(1)) + Body("body", 3) + RefLine("body 5", EndnoteRef(2)) + Body("fill", 3) + Tall(3);
        var layout = Laid(Document(body, endnotes: EndnotesPart(Endnote(1, "end one"), Endnote(2, "end two"))));
        var pages = Pages(layout);
        Assert.True(pages.Count >= 3);
        foreach (var page in pages.Take(pages.Count - 1))
        {
            Assert.DoesNotContain("end one", page.Text, StringComparison.Ordinal);
        }

        var last = pages[^1];
        var lines = Lines(last);
        Assert.Equal("i end one", lines[^2]);
        Assert.Equal("ii end two", lines[^1]);
        Assert.True(Rules(last).Any());

        // The body text comes first on that page, or on an earlier page: the notes are after everything.
        Assert.True(Array.IndexOf(lines, "i end one") > Array.FindLastIndex(lines, l => l.StartsWith("fill", StringComparison.Ordinal)));
    }

    [Fact]
    public void FootnoteNumbersCountPerDocument()
    {
        var body = RefLine("a", FootnoteRef(7)) + Line("b") + RefLine("c", FootnoteRef(3)) + Line("d") + RefLine("e", FootnoteRef(9)) + Tall(3);
        var layout = Laid(Document(body, footnotes: FootnotesPart(Footnote(3, "three"), Footnote(7, "seven"), Footnote(9, "nine"))));
        var notes = Pages(layout).SelectMany(Lines).Where(l => l.Contains(' ', StringComparison.Ordinal) && char.IsDigit(l[0])).ToArray();
        Assert.Equal(["1 seven", "2 three", "3 nine"], notes);
        Assert.True(layout.LaidOutPages >= 2);
    }

    [Fact]
    public void FootnotesInColumnsKeepEveryColumnAboveTheNoteArea()
    {
        var builder = new StringBuilder();
        var notes = new List<string>();
        for (var i = 1; i <= 60; i++)
        {
            builder.Append(i % 5 == 0 ? RefLine($"refpara {i}", FootnoteRef(i)) : Line($"plain {i}"));
            if (i % 5 == 0)
            {
                notes.Add(Footnote(i, $"note for {i}"));
            }
        }

        var layout = Laid(Document(builder + Tall(12, "<w:cols w:num=\"2\" w:space=\"720\"/>"), footnotes: FootnotesPart([.. notes])));
        Assert.True(layout.IsComplete);
        var total = 0;
        foreach (var page in Pages(layout))
        {
            var refs = CountLines(page, "refpara");
            Assert.Equal(refs, Lines(page).Count(l => l.Contains(" note for ", StringComparison.Ordinal)));
            total += refs;
            if (refs == 0)
            {
                continue;
            }

            var rule = Assert.Single(Rules(page));
            var bodyLines = CountLines(page, "refpara") + CountLines(page, "plain");
            var bodyBaselines = Baselines(page).Where(b => b < rule.Y).ToList();
            Assert.True(bodyBaselines.Count >= bodyLines / 2);
            Assert.DoesNotContain(Runs(page), r => r.Baseline > rule.Y && r.Baseline < rule.Y + 1f);
        }

        Assert.Equal(12, total);
    }

    [Fact]
    public void AReferenceToAMissingNoteStillShowsItsNumber()
    {
        var plain = Laid(Document(Body("line", 3) + Sect()));
        var layout = Laid(Document(RefLine("line 1", FootnoteRef(99)) + Body("line", 2) + Sect()));
        Assert.Equal(1, layout.LaidOutPages);
        Assert.Contains("line 11", Lines(layout.Page(0)));
        Assert.Empty(Rules(layout.Page(0)));
        Assert.Equal(Lines(plain.Page(0)).Length, Lines(layout.Page(0)).Length);
    }
}
