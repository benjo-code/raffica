using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;
using static Raffica.Write.Tests.Layout.PageTestKit;
using static Raffica.Write.Tests.Support.TocDocuments;

namespace Raffica.Write.Tests.Layout;

/// <summary>The entries of a table of contents on Write's own page (milestone 4.5b): a short entry is one line, its title right after the number and its page number at the right edge.</summary>
public sealed class TocEntryLayoutTests
{
    // The text area of A4 with 2.5 cm margins, in points.
    private const float TextWidth = 453.6f;

    [Theory]
    [InlineData("%1.", "%1.%2.", "%1.%2.%3.", null, 1)]
    [InlineData("%1.", "%1.%2.", "%1.%2.%3.", null, 9)]
    [InlineData("%1.", "%1.%2.", "%1.%2.%3.", null, 100)]
    [InlineData("Chapter %1:", "Chapter %1.%2:", "Chapter %1.%2.%3:", null, 12)]
    [InlineData("Appendix %1", "Appendix %1.%2", "Appendix %1.%2.%3", "upperLetter", 2)]
    [InlineData("%1.", "%1.%2.", "%1.%2.%3.", "upperRoman", 18)]
    [InlineData("%1.", "%1.%2.", "%1.%2.%3.", "upperRoman", 88)]
    [InlineData("WWWWWWWWWWWW %1.", "mmmmmmmmmmmm %2.", "(%3) @@@@@@@@@@", null, 1)]
    [InlineData("%1", "%1-%2", "%1-%2-%3", "decimalZero", 1)]
    public void EveryShortEntryIsOneLineWithTheTitleRightAfterTheNumber(string first, string second, string third, string? format, int start)
    {
        var (file, editor) = Numbered([(null, "Contents."), ("Heading1", "Alpha"), ("Heading2", "Beta"), ("Heading3", "Gamma"), ("Heading1", "Delta")], [first, second, third], format, start);
        using var _ = file;
        var at = (Paragraph)file.Model.Body.Blocks[0];
        var host = new LayoutFieldHost(file.Model, Finish(DocumentLayout.Create(file.Model, Fonts, new LayoutOptions())));
        Assert.NotNull(editor.InsertToc(new TextPosition(StoryRef.Body, at, "Contents.".Length), new TocOptions(1, 3, true, true, TocLeader.Dots, null), host));

        AssertShortLines(file.Model, 4);
    }

    [Theory]
    [InlineData("upperRoman", 2, "%1.", 18)]
    [InlineData("upperRoman", 2, "Chapter %1:", 2)]
    [InlineData("decimal", 1, "Chapter %1.%1.%1:", 100)]
    public void AShortEntryKeepsItsPageNumberAtTheRightEdgeWhenItsNumberGrowsAndShrinksAgain(string format, int start, string changed, int changedStart)
    {
        var (file, editor) = Numbered([(null, "Contents."), ("Heading1", "FAQ"), ("Heading1", "A")], ["%1."], format, start);
        using var _ = file;
        var host = new FakeFieldHost(file.Model);
        var at = (Paragraph)file.Model.Body.Blocks[0];
        Assert.NotNull(editor.InsertToc(new TextPosition(StoryRef.Body, at, "Contents.".Length), new TocOptions(1, 3, true, true, TocLeader.Dots, null), host));

        foreach (var (text, from) in new[] { (changed, changedStart), ("%1.", start), (changed, changedStart), ("%1.", start) })
        {
            Renumber(file.Model, text, from);
            editor.RefreshFields(host, RefreshScope.OrderBased);

            AssertShortLines(file.Model, 2);
            Assert.Empty(editor.RefreshFields(host, RefreshScope.OrderBased).Content);
        }
    }

    [Fact]
    public void AStopTheNumberOutgrewThroughItsStyleDoesNotStayBesideTheNewOne()
    {
        // The entry style gets larger letters and loses them again, as an undone style change does: the stop of the smaller number must not stay in front of the one the larger number needed.
        var (file, editor) = Numbered([(null, "Contents."), ("Heading1", "FAQ")]);
        using var _ = file;
        var host = new FakeFieldHost(file.Model);
        var at = (Paragraph)file.Model.Body.Blocks[0];
        Assert.NotNull(editor.InsertToc(new TextPosition(StoryRef.Body, at, "Contents.".Length), new TocOptions(1, 3, true, true, TocLeader.Dots, null), host));
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var style = file.Model.Styles!.Find("TOC1")!;

        style.RunProperties.Set(w + "sz", new XElement(w + "sz", new XAttribute(w + "val", "96")));
        file.Model.Edits.Raise();
        editor.RefreshFields(host, RefreshScope.OrderBased);
        Assert.Single(Tabs(Assert.Single(Entries(file.Model))), t => t.Kind == "left");

        style.RunProperties.Remove(w + "sz");
        file.Model.Edits.Raise();
        editor.RefreshFields(host, RefreshScope.OrderBased);

        var entry = Assert.Single(Entries(file.Model));
        Assert.Single(Tabs(entry), t => t.Kind == "left");
        var layout = Finish(DocumentLayout.Create(file.Model, Fonts, new LayoutOptions()));
        var margin = layout.Caret(new TextPosition(StoryRef.Body, at, 0), 0)!.Value.X;
        var end = At(layout, entry, ParagraphProjection.Build(entry, ProjectionHost.Plain).Text.Length);
        Assert.True(end.Top == At(layout, entry, 0).Top && Math.Abs(end.X - margin - TextWidth) < 1f, $"the page number does not end at the right edge: {end.X - margin}");
    }

    [Theory]
    [InlineData(600)]
    [InlineData(1320)]
    public void AShortEntryOverAStyleWithAStopOfItsOwnKeepsItsPageNumberWhenItsNumberGrowsAndShrinks(int styleStop)
    {
        // The style's stop serves the short number. The long one passes it and gets a stop of the entry's own, which must not come back in front of the style's stop for the short number.
        var (file, editor, host) = OverAStyleStop(styleStop);
        using var _ = file;
        AssertAtTheRightEdge(file.Model);

        Renumber(file.Model, "Chapter %1.%1.%1:", 100);
        editor.RefreshFields(host, RefreshScope.OrderBased);
        AssertAtTheRightEdge(file.Model);

        Renumber(file.Model, "%1.", 1);
        editor.RefreshFields(host, RefreshScope.OrderBased);

        AssertAtTheRightEdge(file.Model);
        Assert.All(Entries(file.Model), entry => Assert.Equal("right:9072/dot", TabText(entry)));
        Assert.Empty(editor.RefreshFields(host, RefreshScope.OrderBased).Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AShortEntryOverAStyleWithAStopOfItsOwnKeepsItsPageNumberWhenItsLettersGrowAndShrink(bool renumberedAfterwards)
    {
        // The number stays the same: only the larger letters take it past the style's stop, and the stop written for them is of no use once they are small again.
        var (file, editor, host) = OverAStyleStop(600);
        using var _ = file;
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var style = file.Model.Styles!.Find("TOC1")!;
        style.RunProperties.Set(w + "sz", new XElement(w + "sz", new XAttribute(w + "val", "96")));
        file.Model.Edits.Raise();
        editor.RefreshFields(host, RefreshScope.OrderBased);
        Assert.All(Entries(file.Model), entry => Assert.Single(Tabs(entry), t => t.Kind == "left"));

        style.RunProperties.Remove(w + "sz");
        file.Model.Edits.Raise();
        editor.RefreshFields(host, RefreshScope.OrderBased);
        if (renumberedAfterwards)
        {
            Renumber(file.Model, "%1)", 3);
            editor.RefreshFields(host, RefreshScope.OrderBased);
        }

        AssertAtTheRightEdge(file.Model);
        Assert.All(Entries(file.Model), entry => Assert.Equal("right:9072/dot", TabText(entry)));
        Assert.Empty(editor.RefreshFields(host, RefreshScope.OrderBased).Content);
    }

    [Fact]
    public void AStyleStopRightBehindTheNumberServesAsItDoesOnThePage()
    {
        // The number ends a fraction of a twip in front of the stop: the page takes the stop, so no stop of the entry's own may be written, which would leave the style's to the page number.
        var (file, editor, host) = OverAStyleStop(5000);
        using var _ = file;
        Renumber(file.Model, "Chapter %1:", 1);
        editor.RefreshFields(host, RefreshScope.OrderBased);
        var layout = Finish(DocumentLayout.Create(file.Model, Fonts, new LayoutOptions()));
        var margin = layout.Caret(new TextPosition(StoryRef.Body, (Paragraph)file.Model.Body.Blocks[0], 0), 0)!.Value.X;
        var entry = Entries(file.Model)[0];
        var end = (At(layout, entry, "Chapter 1:".Length).X - margin) * 20f;
        var stop = (int)MathF.Floor(end) + 1;
        Assert.InRange(stop - end, 0.25f, 0.95f);
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        file.Model.Styles!.Find("TOC1")!.ParagraphProperties.Set(w + "tabs", new XElement(w + "tabs", new XElement(w + "tab", new XAttribute(w + "val", "left"), new XAttribute(w + "pos", stop))));
        foreach (var each in Entries(file.Model))
        {
            each.Properties.Set(w + "tabs", new XElement(w + "tabs", new XElement(w + "tab", new XAttribute(w + "val", "right"), new XAttribute(w + "leader", "dot"), new XAttribute(w + "pos", "9072"))));
        }

        file.Model.Edits.Raise();
        editor.RefreshFields(host, RefreshScope.OrderBased);

        AssertAtTheRightEdge(file.Model);
        Assert.Equal("right:9072/dot", TabText(Entries(file.Model)[0]));
    }

    [Theory]
    [InlineData(1200)]
    [InlineData(1320)]
    public void AShortEntryOverAHangingIndentKeepsNoStopOfItsOwnWhenItsLettersGrowAndShrink(int second)
    {
        // Word's own list over these styles (probe of 2026-11-28, Docs/write-word-check.md) writes the right stop only on the level-1 entry, whose number the hanging indent takes, and draws its page number at the right edge.
        var (file, editor) = Numbered([(null, "Contents."), ("Heading1", "A"), ("Heading2", "Beta")]);
        using var _ = file;
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        XElement Indent(int left, int hanging = 0) => new(w + "ind", new XAttribute(w + "left", left), hanging == 0 ? null : new XAttribute(w + "hanging", hanging));
        var first = file.Model.Styles!.Find("TOC1")!;
        first.ParagraphProperties.Set(w + "ind", Indent(400, 400));
        file.Model.Styles!.Find("TOC2")!.ParagraphProperties.Set(w + "ind", Indent(second));
        file.Model.Styles!.Find("TOC3")!.ParagraphProperties.Set(w + "ind", Indent(1800));
        file.Model.Edits.Raise();
        var host = new FakeFieldHost(file.Model);
        var at = (Paragraph)file.Model.Body.Blocks[0];
        Assert.NotNull(editor.InsertToc(new TextPosition(StoryRef.Body, at, "Contents.".Length), new TocOptions(1, 3, true, true, TocLeader.Dots, null), host));
        Assert.Equal("right:9072/dot", TabText(Entries(file.Model)[0]));
        AssertAtTheRightEdge(file.Model);

        first.RunProperties.Set(w + "sz", new XElement(w + "sz", new XAttribute(w + "val", "96")));
        file.Model.Edits.Raise();
        editor.RefreshFields(host, RefreshScope.OrderBased);
        // The large number passes the hanging indent: it needs a stop of the entry's own.
        Assert.Single(Tabs(Entries(file.Model)[0]), t => t.Kind == "left");

        first.RunProperties.Remove(w + "sz");
        file.Model.Edits.Raise();
        editor.RefreshFields(host, RefreshScope.OrderBased);

        AssertAtTheRightEdge(file.Model);
        Assert.Equal("right:9072/dot", TabText(Entries(file.Model)[0]));
        Assert.Empty(editor.RefreshFields(host, RefreshScope.OrderBased).Content);
    }

    [Theory]
    [InlineData(1200)]
    [InlineData(1320)]
    public void AShortEntryOverAStyleStopKeepsTheStopWordWritesThereWhenItsLettersGrowAndShrink(int second)
    {
        // Intended, because it is Word's look: over a TOC1 with a left stop at 600 and a TOC2 indented further, Word's own list (freshly made and after "Update table") writes a left stop at the TOC2 indent on the level-1 entry and draws
        // the page number of "1. A" at that stop, in the middle of the line (probe of 2026-11-28, Docs/write-word-check.md). The stop written for the large letters is that very stop, so a refresh keeps it. (Write itself writes none there in a
        // list it makes: the style's stop serves.)
        var (file, editor) = Numbered([(null, "Contents."), ("Heading1", "A"), ("Heading2", "Beta")]);
        using var _ = file;
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var first = file.Model.Styles!.Find("TOC1")!;
        first.ParagraphProperties.Set(w + "tabs", new XElement(w + "tabs", new XElement(w + "tab", new XAttribute(w + "val", "left"), new XAttribute(w + "pos", "600"))));
        file.Model.Styles!.Find("TOC2")!.ParagraphProperties.Set(w + "ind", new XElement(w + "ind", new XAttribute(w + "left", second)));
        file.Model.Styles!.Find("TOC3")!.ParagraphProperties.Set(w + "ind", new XElement(w + "ind", new XAttribute(w + "left", 1800)));
        file.Model.Edits.Raise();
        var host = new FakeFieldHost(file.Model);
        var at = (Paragraph)file.Model.Body.Blocks[0];
        Assert.NotNull(editor.InsertToc(new TextPosition(StoryRef.Body, at, "Contents.".Length), new TocOptions(1, 3, true, true, TocLeader.Dots, null), host));

        first.RunProperties.Set(w + "sz", new XElement(w + "sz", new XAttribute(w + "val", "96")));
        file.Model.Edits.Raise();
        editor.RefreshFields(host, RefreshScope.OrderBased);
        first.RunProperties.Remove(w + "sz");
        file.Model.Edits.Raise();
        editor.RefreshFields(host, RefreshScope.OrderBased);

        var entry = Entries(file.Model)[0];
        Assert.Equal($"left:{second} right:9072/dot", TabText(entry));
        var layout = Finish(DocumentLayout.Create(file.Model, Fonts, new LayoutOptions()));
        var margin = layout.Caret(new TextPosition(StoryRef.Body, at, 0), 0)!.Value.X;
        var text = ParagraphProjection.Build(entry, ProjectionHost.Plain).Text;
        var page = At(layout, entry, text.LastIndexOf('\t') + 1);
        Assert.Equal(At(layout, entry, 0).Top, page.Top);
        Assert.Equal(second / 20f, page.X - margin, 0.5f);
        Assert.Empty(editor.RefreshFields(host, RefreshScope.OrderBased).Content);
    }

    // Two numbered headings and their list over a TOC1 that has a left stop of its own, the entries as an insert over such a style leaves them.
    private static (DocxFile File, TextEditor Editor, FakeFieldHost Host) OverAStyleStop(int position)
    {
        var (file, editor) = Numbered([(null, "Contents."), ("Heading1", "A"), ("Heading1", "B")]);
        var host = new FakeFieldHost(file.Model);
        var at = (Paragraph)file.Model.Body.Blocks[0];
        Assert.NotNull(editor.InsertToc(new TextPosition(StoryRef.Body, at, "Contents.".Length), new TocOptions(1, 3, true, true, TocLeader.Dots, null), host));
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        file.Model.Styles!.Find("TOC1")!.ParagraphProperties.Set(w + "tabs", new XElement(w + "tabs", new XElement(w + "tab", new XAttribute(w + "val", "left"), new XAttribute(w + "pos", position))));
        foreach (var entry in Entries(file.Model))
        {
            entry.Properties.Set(w + "tabs", new XElement(w + "tabs", new XElement(w + "tab", new XAttribute(w + "val", "right"), new XAttribute(w + "leader", "dot"), new XAttribute(w + "pos", "9072"))));
        }

        file.Model.Edits.Raise();
        editor.RefreshFields(host, RefreshScope.OrderBased);
        return (file, editor, host);
    }

    private static void AssertAtTheRightEdge(WordDocument document)
    {
        var at = (Paragraph)document.Body.Blocks[0];
        var layout = Finish(DocumentLayout.Create(document, Fonts, new LayoutOptions()));
        var margin = layout.Caret(new TextPosition(StoryRef.Body, at, 0), 0)!.Value.X;
        var entries = Entries(document);
        Assert.Equal(2, entries.Count);
        foreach (var entry in entries)
        {
            var text = ParagraphProjection.Build(entry, ProjectionHost.Plain).Text;
            var end = At(layout, entry, text.Length);
            Assert.True(end.Top == At(layout, entry, 0).Top && Math.Abs(end.X - margin - TextWidth) < 1f, $"the page number of '{text}' does not end at the right edge: {end.X - margin} ({TabText(entry)})");
        }
    }

    // Every entry is one line: the title right after the number (the next TOC level indent, 11 points apart, or the number and its 12 points of room), the page number at the right edge.
    private static void AssertShortLines(WordDocument document, int count)
    {
        var at = (Paragraph)document.Body.Blocks[0];
        var layout = Finish(DocumentLayout.Create(document, Fonts, new LayoutOptions()));
        var entries = Entries(document);
        Assert.Equal(count, entries.Count);
        var margin = layout.Caret(new TextPosition(StoryRef.Body, at, 0), 0)!.Value.X;
        foreach (var entry in entries)
        {
            var text = ParagraphProjection.Build(entry, ProjectionHost.Plain).Text;
            var tab = text.IndexOf('\t', StringComparison.Ordinal);
            var number = At(layout, entry, 0);
            var labelEnd = At(layout, entry, tab);
            var title = At(layout, entry, tab + 1);
            var end = At(layout, entry, text.Length);
            Assert.True(number.Top == title.Top && number.Top == end.Top, $"the entry '{text}' is not one line: {number.Top}, {title.Top}, {end.Top}");
            Assert.True(title.X > labelEnd.X && title.X - labelEnd.X <= 24f, $"the title of '{text}' starts {title.X - labelEnd.X} points after its number");
            Assert.True(Math.Abs(end.X - margin - TextWidth) < 1f, $"the page number of '{text}' does not end at the right edge: {end.X - margin}");
        }
    }

    [Fact]
    public void AListThatIsNotRightAlignedHasThePageNumberRightAfterTheTitle()
    {
        var (file, editor) = Numbered([(null, "Contents."), ("Heading1", "Alpha")]);
        using var _ = file;
        var at = (Paragraph)file.Model.Body.Blocks[0];
        Assert.NotNull(editor.InsertToc(new TextPosition(StoryRef.Body, at, "Contents.".Length), new TocOptions(1, 3, true, false, TocLeader.Dots, null), new FakeFieldHost(file.Model)));

        var layout = Finish(DocumentLayout.Create(file.Model, Fonts, new LayoutOptions()));

        var entry = Assert.Single(Entries(file.Model));
        var margin = layout.Caret(new TextPosition(StoryRef.Body, at, 0), 0)!.Value.X;
        var start = At(layout, entry, 0);
        var end = At(layout, entry, ParagraphProjection.Build(entry, ProjectionHost.Plain).Text.Length);
        Assert.Equal(start.Top, end.Top);
        Assert.True(end.X - margin < 120f, $"the page number is {end.X - margin} points from the margin");
    }

    private static CaretBox At(DocumentLayout layout, Paragraph paragraph, int offset)
    {
        var box = layout.Caret(new TextPosition(StoryRef.Body, paragraph, offset), 0);
        Assert.NotNull(box);
        return box.Value;
    }
}
