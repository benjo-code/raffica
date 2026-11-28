using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;
using static Raffica.Write.Tests.Support.TocDocuments;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// The shape of the entries of a table of contents as Word writes them (milestone 4.5b): the left tab stop after the number, the separator before the page number of a list that is not right-aligned (<c>\p</c>) and the headings
/// without text. The positions are the ones Word 16 wrote into its own lists of the same documents (<c>Docs/write-word-check.md</c>).
/// </summary>
public sealed class TocEntryShapeTests
{
    private const string Section = "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1417\" w:right=\"1417\" w:bottom=\"1417\" w:left=\"1417\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr>";

    private static readonly TocOptions s_default = new(1, 3, true, true, TocLeader.Dots, null);

    private static readonly (string? Style, string Text)[] s_three = [(null, "Contents."), ("Heading1", "Alpha"), ("Heading2", "Beta"), ("Heading3", "Gamma")];

    private static void Insert(TextEditor editor, WordDocument document, int block = 0, TocOptions? options = null, IFieldHost? host = null)
    {
        var paragraph = (Paragraph)document.Body.Blocks[block];
        var end = ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text.Length;
        Assert.NotNull(editor.InsertToc(new TextPosition(StoryRef.Body, paragraph, end), options ?? s_default, host ?? new FakeFieldHost(document)));
    }

    // The positions are exact where Word takes the indent of a TOC style, and within a few twips where it takes the width of the number (Word measures a little differently).
    private static void AssertStop(Paragraph entry, int expected, string what)
    {
        var tabs = Tabs(entry);
        Assert.True(tabs.Count == 2, $"{what}: the entry has the tab stops '{TabText(entry)}'");
        Assert.Equal("left", tabs[0].Kind);
        Assert.Null(tabs[0].Leader);
        Assert.True(expected % 220 == 0 ? tabs[0].Position == expected : Math.Abs(tabs[0].Position - expected) <= 15, $"{what}: Word writes the stop {expected}, this is {tabs[0].Position}");
        Assert.Equal(("right", 9072, "dot"), tabs[1]);
    }

    // ---- bug 1: the stop after the number ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("%1.", "%1.%2.", "%1.%2.%3.", null, 1, 440, 880, 1320)]
    [InlineData("%1.", "%1.%2.", "%1.%2.%3.", null, 10, 660, 1100, 1540)]
    [InlineData("%1.", "%1.%2.", "%1.%2.%3.", null, 100, 660, 1320, 1850)]
    [InlineData("Chapter %1:", "Chapter %1.%2:", "Chapter %1.%2.%3:", null, 1, 1320, 1760, 1948)]
    [InlineData("Appendix %1", "Appendix %1.%2", "Appendix %1.%2.%3", null, 1, 1320, 1760, 2020)]
    [InlineData("%1.", "%1.%2.", "%1.%2.%3.", "upperRoman", 18, 880, 1540, 2063)]
    [InlineData("MMMMMMMM%1", "MMMMMMMM%2", "MMMMMMMM%3", null, 1, 1856, 2076, 2296)]
    [InlineData("MMMMM%1", "MMMMM%2", "MMMMM%3", null, 1, 1320, 1540, 1760)]
    public void ANewEntryHasTheStopWordWritesAfterItsNumber(string first, string second, string third, string? format, int start, int one, int two, int three)
    {
        var (file, editor) = Numbered(s_three, [first, second, third], format, start);
        using var _ = file;

        Insert(editor, file.Model);

        var entries = Entries(file.Model);
        AssertStop(entries[0], one, "level 1");
        AssertStop(entries[1], two, "level 2");
        AssertStop(entries[2], three, "level 3");
    }

    [Theory]
    [InlineData("space")]
    [InlineData("nothing")]
    public void ANumberThatNoTabFollowsIsWrittenWithASpaceAndNoStop(string suffix)
    {
        var (file, editor) = Numbered(s_three, suffix: suffix);
        using var _ = file;
        var host = new FakeFieldHost(file.Model);

        Insert(editor, file.Model, host: host);

        var entries = Entries(file.Model);
        Assert.Equal("1. Alpha|?", Line(entries[0]));
        Assert.All(entries, e => Assert.Equal("right:9072/dot", TabText(e)));
        ReferenceToc.AssertEqualToExpected(file.Model, host, suffix);
    }

    [Fact]
    public void AnEntryWithoutANumberHasNoStopOfItsOwn()
    {
        var document = Doc(Heading(1, "Alpha") + Text(string.Empty));

        Insert(Editor(document), document, 1);

        Assert.Equal("right:9072/dot", TabText(Assert.Single(Entries(document))));
    }

    [Theory]
    // A left stop after the number and a right one in the style, as the corpus thesis has: Word adds nothing.
    [InlineData("<w:tabs><w:tab w:val=\"left\" w:pos=\"567\"/><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9072\"/></w:tabs>", "right:9072/dot")]
    // A left stop the number is longer than does not serve: the stop is added.
    [InlineData("<w:tabs><w:tab w:val=\"left\" w:pos=\"100\"/></w:tabs>", "left:440 right:9072/dot")]
    // A hanging indent that reaches behind the number is a stop of its own.
    [InlineData("<w:ind w:left=\"720\" w:hanging=\"720\"/>", "right:9072/dot")]
    // A hanging indent the number is longer than: the first TOC level indent behind the number is taken.
    [InlineData("<w:ind w:left=\"100\" w:hanging=\"100\"/>", "left:440 right:9072/dot")]
    public void TheStyleOfTheEntryDecidesWhetherAStopIsNeeded(string properties, string expected)
    {
        var toc1 = $"<w:style w:type=\"paragraph\" w:styleId=\"TOC1\"><w:name w:val=\"toc 1\"/><w:pPr>{properties}</w:pPr></w:style>";
        var document = Doc(Heading(1, "Alpha") + Text(string.Empty), numbered: true, extraStyles: toc1);

        Insert(Editor(document), document, 1);

        Assert.Equal(expected, TabText(Assert.Single(Entries(document))));
    }

    // ---- bug 1: a refresh ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void AListSavedWithoutTheStopIsRepairedByTheNextRefresh()
    {
        const string right = "<w:tabs><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9072\"/></w:tabs>";
        var document = Doc(
            Marked("Alpha", 1) + Marked("Beta", 2, "Heading2")
            + Entry("TOC1", right, "TOC \\o \"1-3\" \\h \\z \\u", "1.", "Alpha", 1)
            + Entry("TOC2", right, null, "1.1.", "Beta", 2) + End(),
            numbered: true);
        var editor = Editor(document);
        var host = new FakeFieldHost(document);

        var result = editor.RefreshFields(host, RefreshScope.OrderBased);

        Assert.NotEmpty(result.Content);
        Assert.Null(result.Structural);
        Assert.Equal(["left:440 right:9072/dot", "left:880 right:9072/dot"], Entries(document).Select(TabText));
        Assert.Equal(["1.|Alpha|1", "1.1.|Beta|2"], Entries(document).Select(Line));
        Assert.Empty(editor.RefreshFields(host, RefreshScope.OrderBased).Content);
        ReferenceToc.AssertEqualToExpected(document, host, "repaired");
    }

    [Fact]
    public void AListAsWordWritesItIsLeftAlone()
    {
        // Word's own markup: a stop per level, one of them where Write would not put it, and a stop of the user's behind it.
        const string right = "<w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9062\"/></w:tabs>";
        var document = Doc(
            Marked("Alpha", 1) + Marked("Beta", 2, "Heading2")
            + Entry("TOC1", "<w:tabs><w:tab w:val=\"left\" w:pos=\"600\"/>" + right, "TOC \\o \"1-3\" \\h \\z \\u", "1.", "Alpha", 1)
            + Entry("TOC2", "<w:tabs><w:tab w:val=\"left\" w:pos=\"880\"/><w:tab w:val=\"left\" w:pos=\"3000\"/>" + right, null, "1.1.", "Beta", 2) + End(),
            numbered: true);
        var before = Bytes(document);

        var result = Editor(document).RefreshFields(new FakeFieldHost(document), RefreshScope.OrderBased);

        Assert.Empty(result.Content);
        Assert.Equal(before, Bytes(document));
    }

    [Theory]
    // What Word 16 wrote itself over TOC styles with hanging indents (left indents of TOC1 to TOC3 and the hanging indent in twips; the level texts "%1." or "Chapter %1:"): also where the hanging stop takes the number it writes a stop behind it,
    // or one at it. 0 is no stop. A refresh with nothing changed leaves each such list as it is.
    [InlineData(720, 1080, 1440, 720, "", 0, 0, 1540)]
    [InlineData(360, 580, 800, 360, "", 0, 800, 1320)]
    [InlineData(440, 880, 1320, 440, "", 0, 1320, 1760)]
    [InlineData(280, 560, 840, 280, "", 0, 880, 1320)]
    [InlineData(2000, 4000, 6000, 2000, "", 0, 4000, 6000)]
    [InlineData(720, 1080, 1440, 720, "Chapter ", 1440, 1760, 2228)]
    [InlineData(2000, 4000, 6000, 2000, "Chapter ", 0, 4000, 6000)]
    public void AListAsWordWritesItOverHangingIndentsIsLeftAlone(int first, int second, int third, int hanging, string word, int one, int two, int three)
    {
        var tocStyles = string.Concat(new[] { first, second, third }.Select((left, i) =>
            $"<w:style w:type=\"paragraph\" w:styleId=\"TOC{i + 1}\"><w:name w:val=\"toc {i + 1}\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:ind w:left=\"{left}\" w:hanging=\"{hanging}\"/></w:pPr></w:style>"));
        string Stops(int left) => "<w:tabs>" + (left == 0 ? string.Empty : $"<w:tab w:val=\"left\" w:pos=\"{left}\"/>") + "<w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9062\"/></w:tabs>";
        string Said(string number) => word.Length == 0 ? number + "." : word + number + ":";
        var document = Doc(
            Marked("Alpha", 1) + Marked("Beta", 2, "Heading2") + Marked("Gamma", 3, "Heading3")
            + Entry("TOC1", Stops(one), "TOC \\o \"1-3\" \\h \\z \\u", Said("1"), "Alpha", 1)
            + Entry("TOC2", Stops(two), null, Said("1.1"), "Beta", 2)
            + Entry("TOC3", Stops(three), null, Said("1.1.1"), "Gamma", 3) + End(),
            numbered: true,
            tocStyles: tocStyles);
        if (word.Length > 0)
        {
            foreach (var level in document.Numbering!.Abstracts[0].Levels)
            {
                level.Text = word + level.Text![..^1] + ":";
            }

            document.Edits.Raise();
        }

        var before = Bytes(document);
        var stops = Entries(document).Select(TabText).ToList();

        var result = Editor(document).RefreshFields(new FakeFieldHost(document), RefreshScope.OrderBased);

        Assert.Equal(stops, Entries(document).Select(TabText));
        Assert.Empty(result.Content);
        Assert.Equal(before, Bytes(document));
    }

    [Theory]
    // What Word 16 wrote itself for "1. A" and "1.1. Beta" (probes of 2026-11-28; the left indent of TOC3 is 1800). Over a left stop of TOC1 at 600 it writes a stop at the TOC2 indent on the level-1 entry, and no right stop (the style has a
    // tab stop): the page number is drawn at that stop, in the middle of the line, and that is intended. Over a hanging indent of TOC1 that takes the number it writes no stop when the first line starts at the margin, and one when the first
    // line starts 240 twips behind it. A refresh with nothing changed leaves each of the four as it is.
    [InlineData("<w:tabs><w:tab w:val=\"left\" w:pos=\"600\"/></w:tabs>", 1200, 1200, false, 1800)]
    [InlineData("<w:tabs><w:tab w:val=\"left\" w:pos=\"600\"/></w:tabs>", 1320, 1320, false, 1894)]
    [InlineData("<w:ind w:left=\"400\" w:hanging=\"400\"/>", 1200, 0, true, 1800)]
    [InlineData("<w:ind w:left=\"600\" w:hanging=\"360\"/>", 1200, 1200, true, 1800)]
    public void AListAsWordWritesItWithAStopAtTheNextLevelsIndentIsLeftAlone(string toc1, int indent, int one, bool rightOnOne, int two)
    {
        var tocStyles = $"<w:style w:type=\"paragraph\" w:styleId=\"TOC1\"><w:name w:val=\"toc 1\"/><w:basedOn w:val=\"Normal\"/><w:pPr>{toc1}</w:pPr></w:style>"
            + $"<w:style w:type=\"paragraph\" w:styleId=\"TOC2\"><w:name w:val=\"toc 2\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:ind w:left=\"{indent}\"/></w:pPr></w:style>"
            + "<w:style w:type=\"paragraph\" w:styleId=\"TOC3\"><w:name w:val=\"toc 3\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:ind w:left=\"1800\"/></w:pPr></w:style>";
        const string right = "<w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9062\"/>";
        string Stops(int left, bool withRight) => "<w:tabs>" + (left == 0 ? string.Empty : $"<w:tab w:val=\"left\" w:pos=\"{left}\"/>") + (withRight ? right : string.Empty) + "</w:tabs>";
        var document = Doc(
            Marked("A", 1) + Marked("Beta", 2, "Heading2")
            + Entry("TOC1", Stops(one, rightOnOne), "TOC \\o \"1-3\" \\h \\z \\u", "1.", "A", 1)
            + Entry("TOC2", Stops(two, true), null, "1.1.", "Beta", 2) + End(),
            numbered: true,
            tocStyles: tocStyles);
        var before = Bytes(document);
        var stops = Entries(document).Select(TabText).ToList();

        var result = Editor(document).RefreshFields(new FakeFieldHost(document), RefreshScope.OrderBased);

        Assert.Equal(stops, Entries(document).Select(TabText));
        Assert.Empty(result.Content);
        Assert.Equal(before, Bytes(document));
    }

    [Theory]
    // Word's stop for the old number is also its stop for the new one: over a style stop behind it, and at the hanging indent. It stays, as Word writes it again.
    [InlineData("<w:tabs><w:tab w:val=\"left\" w:pos=\"567\"/></w:tabs>", 440)]
    [InlineData("<w:ind w:left=\"4000\" w:hanging=\"2000\"/>", 4000)]
    public void AStopThatIsWordsPlaceForTheNewNumberTooStays(string properties, int stop)
    {
        var toc1 = $"<w:style w:type=\"paragraph\" w:styleId=\"TOC1\"><w:name w:val=\"toc 1\"/><w:basedOn w:val=\"Normal\"/><w:pPr>{properties}</w:pPr></w:style>";
        var tabs = $"<w:tabs><w:tab w:val=\"left\" w:pos=\"{stop}\"/><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9062\"/></w:tabs>";
        // The list says "3." and the heading is "1." now.
        var document = Doc(Marked("Alpha", 1) + Entry("TOC1", tabs, "TOC \\o \"1-3\" \\h \\z \\u", "3.", "Alpha", 1) + End(), numbered: true, extraStyles: toc1);

        Editor(document).RefreshFields(new FakeFieldHost(document), RefreshScope.OrderBased);

        var entry = Assert.Single(Entries(document));
        Assert.Equal("1.|Alpha|1", Line(entry));
        Assert.Equal($"left:{stop} right:9062/dot", TabText(entry));
    }

    [Fact]
    public void AListWithoutLinksAsWordWritesItIsLeftAlone()
    {
        // TOC \o "1-3" without \h: Word writes no link around an entry, and still \h into its page reference.
        const string tabs = "<w:tabs><w:tab w:val=\"left\" w:pos=\"440\"/><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9062\"/></w:tabs>";
        var entry = $"<w:p><w:pPr><w:pStyle w:val=\"TOC1\"/>{tabs}</w:pPr>" + Start("TOC \\o \"1-3\"") + Run("1.") + "<w:r><w:tab/></w:r>" + Run("Alpha") + "<w:r><w:tab/></w:r>" + Start("PAGEREF _Toc100000001 \\h") + Run("1") + Mark("end") + "</w:p>";
        var document = Doc(Marked("Alpha", 1) + entry + End(), numbered: true);
        var before = Bytes(document);

        var result = Editor(document).RefreshFields(new FakeFieldHost(document), RefreshScope.OrderBased);

        Assert.Empty(result.Content);
        Assert.Equal(before, Bytes(document));
    }

    [Fact]
    public void AListThatNeverHadTabStopsKeepsItsLines()
    {
        var document = Doc(Marked("Alpha", 1) + Entry("TOC1", string.Empty, "TOC \\o \"1-3\" \\h \\z \\u", "1.", "Alpha", 1) + End(), numbered: true);
        var before = Bytes(document);

        var result = Editor(document).RefreshFields(new FakeFieldHost(document), RefreshScope.OrderBased);

        Assert.Empty(result.Content);
        Assert.Equal(before, Bytes(document));
    }

    [Fact]
    public void ARefreshKeepsTheStopsOfEveryEntryAndGivesANewOneItsOwn()
    {
        const string right = "<w:tab w:val=\"right\" w:leader=\"hyphen\" w:pos=\"9062\"/></w:tabs>";
        var document = Doc(
            Marked("Alpha", 1) + Marked("Beta", 2, "Heading2") + Text("Gamma")
            + Entry("TOC1", "<w:tabs><w:tab w:val=\"left\" w:pos=\"600\"/>" + right, "TOC \\o \"1-3\" \\h \\z \\u", "1.", "Alpha", 1)
            + Entry("TOC2", "<w:tabs><w:tab w:val=\"left\" w:pos=\"1500\"/>" + right, null, "1.1.", "Beta", 2) + End(),
            numbered: true);
        var editor = Editor(document);
        var host = new FakeFieldHost(document);

        // The first heading gets another title (its entry is written again) and a third heading appears.
        var alpha = (Paragraph)document.Body.Blocks[0];
        Assert.NotNull(editor.InsertText(Caret(new TextPosition(StoryRef.Body, alpha, 5)), " renamed"));
        var gamma = (Paragraph)document.Body.Blocks[2];
        Assert.NotNull(editor.ApplyParagraphStyle(Caret(new TextPosition(StoryRef.Body, gamma, 0)), "Heading1"));
        var result = editor.RefreshFields(host, RefreshScope.OrderBased);

        Assert.NotNull(result.Structural);
        Assert.Equal(["1.|Alpha renamed|1", "1.1.|Beta|2", "2.|Gamma|?"], Entries(document).Select(Line));
        Assert.Equal(["left:600 right:9062/hyphen", "left:1500 right:9062/hyphen", "left:440 right:9062/hyphen"], Entries(document).Select(TabText));
        ReferenceToc.AssertEqualToExpected(document, host, "kept");
    }

    [Fact]
    public void TheStopsOfAListWhoseEntryStylesHaveAnotherLanguageSurviveARefresh()
    {
        // A German Word calls the style "toc 1" Verzeichnis1. The refresh finds the style by its name, so the entry keeps it, and the stops of the entry stay with it.
        const string german = "<w:style w:type=\"paragraph\" w:styleId=\"Verzeichnis1\"><w:name w:val=\"toc 1\"/></w:style>";
        var tabs = "<w:tabs><w:tab w:val=\"left\" w:pos=\"1500\"/><w:tab w:val=\"right\" w:leader=\"hyphen\" w:pos=\"9062\"/></w:tabs>";
        var document = Doc(Marked("Alpha", 1) + Entry("Verzeichnis1", tabs, "TOC \\o \"1-3\" \\h \\z \\u", "1.", "Alpha", 1) + End(), numbered: true, extraStyles: german);

        Editor(document).RefreshFields(new FakeFieldHost(document), RefreshScope.OrderBased);

        Assert.Equal("left:1500 right:9062/hyphen", TabText(Assert.Single(Entries(document))));
        Assert.Equal("Verzeichnis1", Assert.Single(Entries(document)).Properties.StyleId);
    }

    [Theory]
    // The stop the number outgrew moves: left behind, it would take the tab before the page number when the number is short again.
    [InlineData("<w:tab w:val=\"left\" w:pos=\"220\"/>", "1.", "Chapter %1:", "left:1320 right:9062/dot")]
    // A stop of the user's that still takes the title behind the new number stays where it is.
    [InlineData("<w:tab w:val=\"left\" w:pos=\"600\"/>", "1.", "%1)", "left:600 right:9062/dot")]
    // Word's own stop for the old number moves to Word's stop for the new one, also when the old one would still serve.
    [InlineData("<w:tab w:val=\"left\" w:pos=\"1320\"/>", "Chapter 1:", "%1.", "left:440 right:9062/dot")]
    // A stop left before the old number (Word's own update leaves such) takes the shorter number; the stop of the old one goes.
    [InlineData("<w:tab w:val=\"left\" w:pos=\"440\"/><w:tab w:val=\"left\" w:pos=\"1500\"/>", "Chapter 1:", "%1.", "left:440 right:9062/dot")]
    // A stop of the user's behind the number stop stays.
    [InlineData("<w:tab w:val=\"left\" w:pos=\"440\"/><w:tab w:val=\"left\" w:pos=\"3000\"/>", "1.", "Chapter %1:", "left:1320 left:3000 right:9062/dot")]
    // The numbering goes: its stop goes with it, the user's behind it stays.
    [InlineData("<w:tab w:val=\"left\" w:pos=\"440\"/><w:tab w:val=\"left\" w:pos=\"3000\"/>", "1.", "", "left:3000 right:9062/dot")]
    [InlineData("<w:tab w:val=\"left\" w:pos=\"440\"/><w:tab w:val=\"left\" w:pos=\"880\"/>", "Chapter 1:", "", "right:9062/dot")]
    // A stop in front of the one the old number took takes the new number: no stop is added in front of it, which would leave it to the page number.
    [InlineData("<w:tab w:val=\"left\" w:pos=\"880\"/><w:tab w:val=\"left\" w:pos=\"3000\"/>", "Chapter 1:", "%1.", "left:880 right:9062/dot")]
    // The number stays, but its stop lies so far to the right (written for much larger letters) that the title and the page number do not fit behind it.
    [InlineData("<w:tab w:val=\"left\" w:pos=\"8600\"/>", "1.", "%1.", "left:440 right:9062/dot")]
    // A stop far to the right that leaves them room stays.
    [InlineData("<w:tab w:val=\"left\" w:pos=\"5011\"/>", "1.", "%1.", "left:5011 right:9062/dot")]
    public void ANumberThatChangesTakesItsStopWithIt(string stops, string number, string level, string expected)
    {
        var tabs = "<w:tabs>" + stops + "<w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9062\"/></w:tabs>";
        var document = Doc(Marked("Alpha", 1) + Entry("TOC1", tabs, "TOC \\o \"1-3\" \\h \\z \\u", number, "Alpha", 1) + End(), numbered: true);
        document.Numbering!.Abstracts[0].Levels[0].Text = level;
        document.Edits.Raise();
        var editor = Editor(document);
        var host = new FakeFieldHost(document);

        editor.RefreshFields(host, RefreshScope.OrderBased);

        Assert.Equal(expected, TabText(Assert.Single(Entries(document))));
        Assert.Empty(editor.RefreshFields(host, RefreshScope.OrderBased).Content);
        ReferenceToc.AssertEqualToExpected(document, host, "changed number");
    }

    [Fact]
    public void AStopLeftBeforeANumberThatGrowsDoesNotStayBesideTheNewOne()
    {
        const string tabs = "<w:tabs><w:tab w:val=\"left\" w:pos=\"440\"/><w:tab w:val=\"left\" w:pos=\"1320\"/><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9062\"/></w:tabs>";
        var document = Doc(Marked("Alpha", 1) + Entry("TOC1", tabs, "TOC \\o \"1-3\" \\h \\z \\u", "Chapter 1:", "Alpha", 1) + End(), numbered: true);
        document.Numbering!.Abstracts[0].Levels[0].Text = "Chapter %1: Chapter %1:";
        document.Edits.Raise();

        Editor(document).RefreshFields(new FakeFieldHost(document), RefreshScope.OrderBased);

        // Kept, the stop at 440 would take the tab of a short number again and leave the new stop to the tab before the page number.
        var left = Assert.Single(Tabs(Assert.Single(Entries(document))), t => t.Kind == "left");
        Assert.True(left.Position > 1760, $"the stop is at {left.Position}");
    }

    [Theory]
    // What Word 16 wrote into its own list of the same numbers after "Update table": one stop per entry, also on the way back.
    [InlineData("%1.", "upperRoman", 2, "%1.", 18, 440, 660, 880, 660)]
    [InlineData("%1.", "upperRoman", 2, "Chapter %1:", 2, 440, 660, 1320, 1320)]
    public void ANumberThatGrowsAndShrinksAgainHasTheStopsWordWrites(string first, string format, int start, string changed, int changedStart, int one, int two, int changedOne, int changedTwo)
    {
        var (file, editor) = Numbered([(null, "Contents."), ("Heading1", "FAQ"), ("Heading1", "Notes")], [first], format, start);
        using var _ = file;
        var host = new FakeFieldHost(file.Model);
        Insert(editor, file.Model, host: host);
        string[] before = [$"left:{one} right:9072/dot", $"left:{two} right:9072/dot"];
        Assert.Equal(before, Entries(file.Model).Select(TabText));

        Renumber(file.Model, changed, changedStart);
        editor.RefreshFields(host, RefreshScope.OrderBased);
        Assert.Equal([$"left:{changedOne} right:9072/dot", $"left:{changedTwo} right:9072/dot"], Entries(file.Model).Select(TabText));

        Renumber(file.Model, first, start);
        editor.RefreshFields(host, RefreshScope.OrderBased);

        Assert.Equal(before, Entries(file.Model).Select(TabText));
        Assert.Empty(editor.RefreshFields(host, RefreshScope.OrderBased).Content);
    }

    [Fact]
    public void AStopThatGoesWhereTheStyleServesLeavesNoEmptyTabStops()
    {
        const string toc1 = "<w:style w:type=\"paragraph\" w:styleId=\"TOC1\"><w:name w:val=\"toc 1\"/><w:pPr><w:tabs><w:tab w:val=\"left\" w:pos=\"300\"/></w:tabs></w:pPr></w:style>";
        const string tabs = "<w:tabs><w:tab w:val=\"left\" w:pos=\"1320\"/></w:tabs>";
        var document = Doc(Marked("Alpha", 1) + Entry("TOC1", tabs, "TOC \\o \"1-3\" \\h \\z \\u", "Chapter 1:", "Alpha", 1) + End(), numbered: true, extraStyles: toc1);

        Editor(document).RefreshFields(new FakeFieldHost(document), RefreshScope.OrderBased);

        Assert.Null(Assert.Single(Entries(document)).Properties.Get(XName.Get("tabs", "http://schemas.openxmlformats.org/wordprocessingml/2006/main")));
    }

    [Fact]
    public void AUsersStopOfAnEntryWithoutANumberSurvivesAChangedTitle()
    {
        const string tabs = "<w:tabs><w:tab w:val=\"left\" w:pos=\"2000\"/><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9062\"/></w:tabs>";
        var document = Doc(Marked("Introduction renamed", 1) + Entry("TOC1", tabs, "TOC \\o \"1-3\" \\h \\z \\u", null, "Introduction", 1) + End());

        Editor(document).RefreshFields(new FakeFieldHost(document), RefreshScope.OrderBased);

        var entry = Assert.Single(Entries(document));
        Assert.Equal("Introduction renamed|1", Line(entry));
        Assert.Equal("left:2000 right:9062/dot", TabText(entry));
    }

    [Fact]
    public void AUsersStopOfAListOfFiguresSurvivesAChangedCaption()
    {
        const string tabs = "<w:tabs><w:tab w:val=\"left\" w:pos=\"2000\"/><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9062\"/></w:tabs>";
        var caption = "<w:p><w:bookmarkStart w:id=\"1\" w:name=\"_Toc100000001\"/>" + Run("Figure ") + Start("SEQ Figure") + Run("1") + Mark("end") + Run(": New words") + "<w:bookmarkEnd w:id=\"1\"/></w:p>";
        var document = Doc(caption + Entry("TableofFigures", tabs, "TOC \\h \\z \\c \"Figure\"", null, "Figure 1: Old words", 1) + End());

        Editor(document).RefreshFields(new FakeFieldHost(document), RefreshScope.OrderBased);

        var entry = Assert.Single(Entries(document));
        Assert.Equal("Figure 1: New words|1", Line(entry));
        Assert.Equal("left:2000 right:9062/dot", TabText(entry));
    }

    [Theory]
    // What Word 16 lists for these captions: the first tab stays, with a stop behind the text before it; a later one is a space; a tab at the start or the end is trimmed away.
    [InlineData(": |Words", "Figure 1: |Words|?", true)]
    [InlineData(": A|B|C", "Figure 1: A|B C|?", true)]
    [InlineData(":||Double", "Figure 1:| Double|?", true)]
    [InlineData(": Trailing|", "Figure 1: Trailing|?", false)]
    [InlineData(": |", "Figure 1:|?", false)]
    public void AListOfFiguresKeepsTheFirstTabOfACaption(string after, string line, bool stop)
    {
        var rest = string.Concat(after.Split('|').Select((part, i) => (i == 0 ? string.Empty : "<w:r><w:tab/></w:r>") + (part.Length == 0 ? string.Empty : Run(part))));
        var caption = "<w:p>" + Run("Figure ") + Start("SEQ Figure") + Run("1") + Mark("end") + rest + "</w:p>";
        var document = Doc(caption + Text(string.Empty));
        var host = new FakeFieldHost(document);

        Insert(Editor(document), document, 1, new TocOptions(1, 1, true, true, TocLeader.Dots, "Figure"), host);

        var entry = Assert.Single(Entries(document));
        Assert.Equal(line, Line(entry));
        Assert.Equal(stop ? 1 : 0, Tabs(entry).Count(t => t.Kind == "left"));
        Assert.Empty(Editor(document).RefreshFields(host, RefreshScope.OrderBased).Content);
        ReferenceToc.AssertEqualToExpected(document, host, line);
    }

    [Fact]
    public void AListOfFiguresWithATabInItsCaptionsAsWordWritesItIsLeftAlone()
    {
        const string tabs = "<w:tabs><w:tab w:val=\"left\" w:pos=\"1100\"/><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9062\"/></w:tabs>";
        var caption = "<w:p><w:bookmarkStart w:id=\"1\" w:name=\"_Toc100000001\"/>" + Run("Figure ") + Start("SEQ Figure") + Run("1") + Mark("end") + Run(":") + "<w:r><w:tab/></w:r>" + Run("Words") + "<w:bookmarkEnd w:id=\"1\"/></w:p>";
        var document = Doc(caption + Entry("TableofFigures", tabs, "TOC \\h \\z \\c \"Figure\"", "Figure 1:", "Words", 1) + End());
        var before = Bytes(document);

        var result = Editor(document).RefreshFields(new FakeFieldHost(document), RefreshScope.OrderBased);

        Assert.Empty(result.Content);
        Assert.Equal(before, Bytes(document));
    }

    [Fact]
    public void AnEntryWhoseHeadingLosesItsTextLosesTheStopThatWouldCatchThePageNumber()
    {
        var (file, editor) = Numbered([(null, "Contents."), ("Heading1", "A"), ("Heading1", "Beta")]);
        using var _ = file;
        var host = new FakeFieldHost(file.Model);
        Insert(editor, file.Model, host: host);
        var heading = file.Model.Body.Blocks.OfType<Paragraph>().Single(p => p.Properties.StyleId == "Heading1" && TextOf(p) == "A");
        Assert.NotNull(editor.Delete(new TextRange(new TextPosition(StoryRef.Body, heading, 0), new TextPosition(StoryRef.Body, heading, 1))));

        editor.RefreshFields(host, RefreshScope.OrderBased);

        var entries = Entries(file.Model);
        Assert.Equal(["1.|?", "2.|Beta|?"], entries.Select(Line));
        Assert.Equal(["right:9072/dot", "left:440 right:9072/dot"], entries.Select(TabText));
    }

    [Fact]
    public async Task TheStopsSurviveUndoRedoARefreshASaveAndAReopen()
    {
        var (file, editor) = Numbered([(null, "Contents."), ("Heading1", "Alpha"), ("Heading2", "Beta"), (null, "Gamma")]);
        using var _ = file;
        var host = new FakeFieldHost(file.Model);
        Insert(editor, file.Model, host: host);
        string[] two = ["left:440 right:9072/dot", "left:880 right:9072/dot"];
        Assert.Equal(two, Entries(file.Model).Select(TabText));

        Assert.NotNull(editor.History.Undo());
        Assert.Empty(TocRefresh.Find(file.Model.Body.Blocks));
        Assert.NotNull(editor.History.Redo());
        Assert.Equal(two, Entries(file.Model).Select(TabText));

        var gamma = file.Model.Body.Blocks.OfType<Paragraph>().Single(p => p.Properties.StyleId is null && TextOf(p) == "Gamma");
        Assert.NotNull(editor.ApplyParagraphStyle(Caret(new TextPosition(StoryRef.Body, gamma, 0)), "Heading3"));
        var refreshed = editor.RefreshFields(host, RefreshScope.OrderBased);
        editor.History.AttachDerived(refreshed.Structural!);
        string[] three = [.. two, "left:1320 right:9072/dot"];
        Assert.Equal(three, Entries(file.Model).Select(TabText));
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(two, Entries(file.Model).Select(TabText));
        Assert.NotNull(editor.History.Redo());
        Assert.Equal(three, Entries(file.Model).Select(TabText));

        // The refresh before a save (a redo leaves the new heading without its bookmark until then).
        Assert.Null(editor.RefreshFields(host, RefreshScope.OrderBased).Structural);
        Assert.Equal(three, Entries(file.Model).Select(TabText));

        var path = Path.Combine(Directory.CreateTempSubdirectory("raffica-toc").FullName, "contents.docx");
        await file.SaveAsync(path, TestContext.Current.CancellationToken);
        using var reopened = DocxFile.Open(path);
        Assert.Equal(three, Entries(reopened.Model).Select(TabText));
        var again = new TextEditor(reopened.Model, (_, _) => ProjectionHost.Plain).RefreshFields(new FakeFieldHost(reopened.Model), RefreshScope.OrderBased);
        Assert.Empty(again.Content);
        Directory.Delete(Path.GetDirectoryName(path)!, true);
    }

    // ---- bug 2: page numbers that are not right-aligned -----------------------------------------------------------------------------

    [Theory]
    [InlineData(true, false, null, "TOC \\o \"1-3\" \\p \" \" \\h \\z \\u")]
    [InlineData(false, false, null, "TOC \\o \"1-3\" \\n \\p \" \" \\h \\z \\u")]
    [InlineData(true, false, "Figure", "TOC \\p \" \" \\h \\z \\c \"Figure\"")]
    [InlineData(true, true, null, "TOC \\o \"1-3\" \\h \\z \\u")]
    public void AListThatIsNotRightAlignedHasTheSwitchWordWrites(bool pages, bool right, string? label, string instruction)
    {
        var document = Doc(Heading(1, "Alpha") + Text(string.Empty));

        Insert(Editor(document), document, 1, new TocOptions(1, 3, pages, right, TocLeader.Hyphens, label));

        Assert.Equal(instruction, Instruction(document));
    }

    [Fact]
    public void ThePageNumberOfAListThatIsNotRightAlignedFollowsASpaceThroughEveryRefresh()
    {
        var (file, editor) = Numbered([(null, "Contents."), ("Heading1", "Alpha"), (null, "Beta")]);
        using var _ = file;
        var host = new FakeFieldHost(file.Model);
        Insert(editor, file.Model, options: new TocOptions(1, 3, true, false, TocLeader.None, null), host: host);

        var entry = Assert.Single(Entries(file.Model));
        Assert.Equal("1.|Alpha ?", Line(entry));
        Assert.Equal("left:440 right:9072/dot", TabText(entry));

        var beta = file.Model.Body.Blocks.OfType<Paragraph>().Single(p => p.Properties.StyleId is null && TextOf(p) == "Beta");
        Assert.NotNull(editor.ApplyParagraphStyle(Caret(new TextPosition(StoryRef.Body, beta, 0)), "Heading1"));
        host.Pages[beta] = (0, "7");
        var result = editor.RefreshFields(host, RefreshScope.WithPages);

        Assert.Equal(0, result.NotUpdated);
        Assert.NotNull(result.Structural);
        Assert.Equal(["1.|Alpha ?", "2.|Beta 7"], Entries(file.Model).Select(Line));
        Assert.Empty(editor.RefreshFields(host, RefreshScope.OrderBased).Content);
        ReferenceToc.AssertEqualToExpected(file.Model, host, "not right-aligned");
    }

    [Theory]
    [InlineData("\\p \" \"", " ")]
    [InlineData("\\p \"-\"", "-")]
    [InlineData("\\p \", \"", ",")]
    [InlineData("\\p \"...\"", ".")]
    [InlineData("\\p \"\"", " ")]
    [InlineData("\\p x", "x")]
    [InlineData("\\p \" - \"", " ")]
    [InlineData("\\P \"abc\"", "a")]
    [InlineData("\\p", "|")]
    [InlineData("\\p \"\t\"", "|")]
    public void TheSeparatorOfAListIsTheOneWordTakes(string separator, string shown)
    {
        var document = Doc(Marked("Alpha", 1) + StaleToc("TOC \\o \"1-3\" " + separator + " \\h \\z \\u", ("_Toc100000001", "Old", 1)));
        var host = new FakeFieldHost(document);
        host.Pages[(Paragraph)document.Body.Blocks[0]] = (0, "3");

        var result = Editor(document).RefreshFields(host, RefreshScope.WithPages);

        Assert.Equal(0, result.NotUpdated);
        Assert.Equal("Alpha" + shown + "3", Line(Assert.Single(Entries(document))));
        ReferenceToc.AssertEqualToExpected(document, host, "separator");
    }

    [Fact]
    public void ASeparatorThatIsAControlCharacterLeavesTheListAsItIs()
    {
        var document = Doc(Marked("Alpha", 1) + StaleToc("TOC \\o \"1-3\" \\p \"&#10;\" \\h \\z \\u", ("_Toc100000001", "Old", 1)));
        var before = Bytes(document);

        var result = Editor(document).RefreshFields(new FakeFieldHost(document), RefreshScope.OrderBased);

        Assert.Equal(1, result.NotUpdated);
        Assert.Equal(before, Bytes(document));
    }

    // ---- bug 3: headings without text ----------------------------------------------------------------------------------------------

    private const string Hidden = "<w:r><w:rPr><w:vanish/></w:rPr><w:t>secret</w:t></w:r>";

    [Theory]
    [InlineData("")]
    [InlineData("<w:r><w:t xml:space=\"preserve\">    </w:t></w:r>")]
    [InlineData("<w:r><w:t xml:space=\"preserve\"> </w:t></w:r>")]
    [InlineData("<w:r><w:tab/></w:r>")]
    [InlineData("<w:r><w:br/></w:r>")]
    [InlineData("<w:r><w:br w:type=\"column\"/></w:r>")]
    [InlineData("<w:r><w:footnoteReference w:id=\"1\"/></w:r>")]
    [InlineData("<w:r><w:endnoteReference w:id=\"1\"/></w:r>")]
    [InlineData("<w:bookmarkStart w:id=\"7\" w:name=\"Here\"/><w:bookmarkEnd w:id=\"7\"/>")]
    [InlineData("<w:del w:id=\"8\" w:author=\"a\"><w:r><w:delText>gone</w:delText></w:r></w:del>")]
    [InlineData("<w:proofErr w:type=\"spellStart\"/>")]
    [InlineData(Hidden + "<w:r><w:t xml:space=\"preserve\">  </w:t></w:r>")]
    [InlineData(Hidden)]
    public void ANumberedHeadingWithoutTextIsListedAsItsNumber(string content)
    {
        var document = Doc("<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr>" + content + "</w:p>" + Heading(1, "Omega") + Text(string.Empty), numbered: true);
        var host = new FakeFieldHost(document);

        Insert(Editor(document), document, 2, host: host);

        var entries = Entries(document);
        Assert.Equal(["1.|?", "2.|Omega|?"], entries.Select(Line));
        Assert.Equal("right:9072/dot", TabText(entries[0]));
        var heading = document.Body.Blocks.OfType<Paragraph>().First(p => p.Properties.StyleId == "Heading1");
        Assert.Contains(heading.Inlines.OfType<BookmarkStart>(), b => b.Name.StartsWith("_Toc", StringComparison.Ordinal));
        ReferenceToc.AssertEqualToExpected(document, host, "textless");
    }

    [Theory]
    [InlineData(true, false, "1.|?")]
    [InlineData(true, true, "1.|?")]
    [InlineData(false, true, "1.")]
    [InlineData(false, false, "1.")]
    public void TheEntryOfAHeadingWithoutTextHasNoSeparatorAfterItsNumber(bool pages, bool right, string line)
    {
        var document = Doc(Heading(1, string.Empty) + Text(string.Empty), numbered: true);

        Insert(Editor(document), document, 1, new TocOptions(1, 3, pages, right, TocLeader.Dots, null));

        var expected = pages && !right ? "1. ?" : line;
        Assert.Equal(expected, Line(Assert.Single(Entries(document))));
    }

    [Theory]
    [InlineData("<w:r><w:br w:type=\"page\"/></w:r>")]
    [InlineData("<w:r><w:br w:type=\"page\"/><w:t xml:space=\"preserve\"> </w:t></w:r>")]
    [InlineData("<w:r><w:sym w:font=\"Wingdings\" w:char=\"F0E0\"/></w:r>")]
    [InlineData("<w:r><w:noBreakHyphen/></w:r>")]
    [InlineData("<w:r><w:pict/></w:r>")]
    public void ANumberedHeadingThatHoldsSomethingElseThanTextIsStillLeftOut(string content)
    {
        // Word leaves out a heading that is only a page break; it lists a picture, a symbol or a hyphen with a copy, which Write does not make (a recorded difference).
        var document = Doc("<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr>" + content + "</w:p>" + Heading(1, "Omega") + Text(string.Empty), numbered: true);
        var host = new FakeFieldHost(document);

        Insert(Editor(document), document, 2, host: host);

        Assert.Equal(["2.|Omega|?"], Entries(document).Select(Line));
        ReferenceToc.AssertEqualToExpected(document, host, "left out");
    }

    [Theory]
    [InlineData("")]
    [InlineData("<w:r><w:t xml:space=\"preserve\">  </w:t></w:r>")]
    [InlineData(Hidden)]
    public void ANumberedHeadingWithoutTextWhoseParagraphMarkIsHiddenIsLeftOut(string content)
    {
        var document = Doc("<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/><w:rPr><w:vanish/></w:rPr></w:pPr>" + content + "</w:p>" + Heading(1, "Omega") + Text(string.Empty), numbered: true);
        var host = new FakeFieldHost(document);

        Insert(Editor(document), document, 2, host: host);

        Assert.Equal(["2.|Omega|?"], Entries(document).Select(Line));
        ReferenceToc.AssertEqualToExpected(document, host, "hidden mark");
    }

    [Fact]
    public void AHeadingWithoutNumberAndTextIsStillLeftOut()
    {
        var document = Doc(Heading(1, string.Empty) + Heading(1, "   ") + Heading(1, "Omega") + Text(string.Empty));

        Insert(Editor(document), document, 3);

        Assert.Equal(["Omega|?"], Entries(document).Select(Line));
    }

    [Fact]
    public void ARefreshAddsTheEntryOfAHeadingWhoseTextIsGone()
    {
        var (file, editor) = Numbered([(null, "Contents."), ("Heading1", "Alpha"), ("Heading1", "B")]);
        using var _ = file;
        var host = new FakeFieldHost(file.Model);
        Insert(editor, file.Model, host: host);
        var heading = file.Model.Body.Blocks.OfType<Paragraph>().Single(p => p.Properties.StyleId == "Heading1" && TextOf(p) == "B");
        Assert.NotNull(editor.Delete(new TextRange(new TextPosition(StoryRef.Body, heading, 0), new TextPosition(StoryRef.Body, heading, 1))));

        editor.RefreshFields(host, RefreshScope.OrderBased);

        Assert.Equal(["1.|Alpha|?", "2.|?"], Entries(file.Model).Select(Line));
    }

    // ---- documents ----------------------------------------------------------------------------------------------------------------------

    private static string Run(string text) => $"<w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    private static string Text(string text, string? style = null) =>
        $"<w:p><w:pPr>{(style is null ? string.Empty : $"<w:pStyle w:val=\"{style}\"/>")}</w:pPr>{Run(text)}</w:p>";

    private static string Heading(int level, string text) => Text(text, "Heading" + level.ToString(CultureInfo.InvariantCulture));

    private static string Marked(string text, int id, string style = "Heading1") =>
        $"<w:p><w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr><w:bookmarkStart w:id=\"{id}\" w:name=\"_Toc10000000{id}\"/>{Run(text)}<w:bookmarkEnd w:id=\"{id}\"/></w:p>";

    private static string Mark(string kind) => $"<w:r><w:fldChar w:fldCharType=\"{kind}\"/></w:r>";

    private static string Start(string instruction) => Mark("begin") + $"<w:r><w:instrText xml:space=\"preserve\"> {instruction} </w:instrText></w:r>" + Mark("separate");

    // An entry as Word and Write write it: a link with the number, a tab, the title, a tab and the page reference. The first one begins the field.
    private static string Entry(string style, string pPr, string? begins, string? number, string title, int id) =>
        $"<w:p><w:pPr><w:pStyle w:val=\"{style}\"/>{pPr}</w:pPr>{(begins is null ? string.Empty : Start(begins))}<w:hyperlink w:anchor=\"_Toc10000000{id}\" w:history=\"1\">{(number is null ? string.Empty : Run(number) + "<w:r><w:tab/></w:r>")}{Run(title)}<w:r><w:tab/></w:r>"
        + Start($"PAGEREF _Toc10000000{id} \\h") + Run(id.ToString(CultureInfo.InvariantCulture)) + Mark("end") + "</w:hyperlink></w:p>";

    private static string End() => "<w:p>" + Mark("end") + "</w:p>";

    private static string StaleToc(string instruction, (string Anchor, string Text, int Level) entry) =>
        $"<w:p><w:pPr><w:pStyle w:val=\"TOC{entry.Level}\"/></w:pPr>{Start(instruction.Replace("\"&#10;\"", "\"&#10;\"", StringComparison.Ordinal))}<w:hyperlink w:anchor=\"{entry.Anchor}\">{Run(entry.Text)}</w:hyperlink></w:p>" + End();

    // Heading1 to Heading9, Normal (11 points, as in a document of Write) and Word's TOC2 and TOC3 indents; numbered documents give Heading1 to Heading3 the list "1.", "1.1." and "1.1.1.". <paramref name="tocStyles"/> replaces the indented TOC2 and TOC3.
    // A TOC1 without properties of its own is added unless the styles given define that id or a style named "toc 1" (found by name, so a second one would take its place): the hand-written lists name it, as every list of Word names a style the file has.
    private static WordDocument Doc(string body, bool numbered = false, string extraStyles = "", string? tocStyles = null)
    {
        var styles = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\"/><w:sz w:val=\"22\"/></w:rPr></w:style>"
            + (tocStyles
                ?? "<w:style w:type=\"paragraph\" w:styleId=\"TOC2\"><w:name w:val=\"toc 2\"/><w:pPr><w:ind w:left=\"220\"/></w:pPr></w:style>"
                    + "<w:style w:type=\"paragraph\" w:styleId=\"TOC3\"><w:name w:val=\"toc 3\"/><w:pPr><w:ind w:left=\"440\"/></w:pPr></w:style>");
        for (var i = 1; i <= 9; i++)
        {
            var number = numbered && i <= 3 ? $"<w:numPr><w:ilvl w:val=\"{i - 1}\"/><w:numId w:val=\"1\"/></w:numPr>" : string.Empty;
            styles += $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{i}\"><w:name w:val=\"heading {i}\"/><w:pPr>{number}<w:outlineLvl w:val=\"{i - 1}\"/></w:pPr></w:style>";
        }

        var given = styles + extraStyles;
        if (!given.Contains("w:styleId=\"TOC1\"", StringComparison.Ordinal) && !given.Contains("<w:name w:val=\"toc 1\"/>", StringComparison.Ordinal))
        {
            styles += "<w:style w:type=\"paragraph\" w:styleId=\"TOC1\"><w:name w:val=\"toc 1\"/><w:basedOn w:val=\"Normal\"/></w:style>";
        }

        var numbering = "<w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/></w:lvl>"
            + "<w:lvl w:ilvl=\"1\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.%2.\"/></w:lvl><w:lvl w:ilvl=\"2\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.%2.%3.\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>";
        return LayoutModel.Document(body + Section, LayoutModel.StylesPart(styles + extraStyles), LayoutModel.NumberingPart(numbering));
    }

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain);

    private static string TextOf(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;

    private static string Bytes(WordDocument document) => ModelSnapshot.Of(document).ToString(SaveOptions.DisableFormatting);
}
