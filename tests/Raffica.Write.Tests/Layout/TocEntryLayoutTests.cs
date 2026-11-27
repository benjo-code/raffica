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

        var layout = Finish(DocumentLayout.Create(file.Model, Fonts, new LayoutOptions()));

        var entries = Entries(file.Model);
        Assert.Equal(4, entries.Count);
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
            // The next TOC level indent (11 points apart) or the number and its 12 points of room, never the right edge.
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
