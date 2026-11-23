using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Panes;
using Raffica.WriteApp.Tests.Commands;
using Xunit;

namespace Raffica.WriteApp.Tests.Panes;

/// <summary>What the Sections pane reads (milestone 4.4b, Task 13): one row per section, own or same as previous, the bound of 200 sections and the memo by model and revision.</summary>
public sealed class SectionsSourceTests
{
    private static string Text(string text) => $"<w:r><w:t>{text}</w:t></w:r>";

    private static string EndsSection(string text, string properties = "") => $"<w:pPr><w:sectPr><w:type w:val=\"nextPage\"/>{properties}</w:sectPr></w:pPr>{Text(text)}";

    private static WordDocument Model(params string[] paragraphs) => DocxFile.Open(StyledDocx.Bytes(null, paragraphs)).Model;

    private static IReadOnlyList<int> Pages(int count) => [.. Enumerable.Range(0, count).Select(i => i * 2)];

    [Fact]
    public void OneRowPerSectionWithItsStartPage()
    {
        var model = Model(EndsSection("a"), EndsSection("b"), Text("c"));
        var view = new SectionsSource().Read(model, [0, 2, -1]);

        Assert.False(view.TooMany);
        Assert.Equal(3, view.SectionCount);
        Assert.Equal([0, 1, 2], view.Rows.Select(r => r.Index));
        Assert.Equal([0, 2, -1], view.Rows.Select(r => r.FirstPage));
        Assert.All(view.Rows, r => Assert.False(r.Setup.Landscape));
        Assert.Equal(11906, view.Rows[0].Setup.WidthTwips);
        Assert.False(view.LeftRight);
        Assert.All(view.Rows, r => Assert.Same(SectionList.Of(model)[r.Index].Properties, r.Identity));
    }

    [Fact]
    public void AMissingPageListReadsAsNotLaidOut()
    {
        var view = new SectionsSource().Read(Model(EndsSection("a"), Text("b")), []);
        Assert.Equal([-1, -1], view.Rows.Select(r => r.FirstPage));
    }

    [Fact]
    public void TheRowsSayOwnOrSameAsPrevious()
    {
        var model = Model(
            EndsSection("a"),
            EndsSection("b", "<w:pgNumType w:start=\"5\" w:fmt=\"lowerRoman\"/><w:titlePg/>"),
            EndsSection("c", "<w:pgSz w:w=\"16838\" w:h=\"11906\" w:orient=\"landscape\"/><w:cols w:num=\"2\" w:space=\"720\"/>"),
            Text("d"));
        var sections = SectionList.Of(model);
        sections[0].Properties.HeaderFooterReferences.Add(new HeaderFooterReference(HeaderFooterKind.Header, HeaderFooterType.Default, "rId90"));
        sections[1].Properties.HeaderFooterReferences.Add(new HeaderFooterReference(HeaderFooterKind.Footer, HeaderFooterType.Default, "rId91"));
        sections[2].Properties.HeaderFooterReferences.Add(new HeaderFooterReference(HeaderFooterKind.Header, HeaderFooterType.First, "rId92"));
        model.Settings = new DocumentSettings(new OoxmlRoot(System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main") + "settings", []));
        model.Settings.EvenAndOddHeaders = true;

        var view = new SectionsSource().Read(model, Pages(4));

        // The first section is always its own; a section that sets no default part of a kind takes the one before (a first-page part alone does not make the default own).
        Assert.Equal([true, false, false, false], view.Rows.Select(r => r.HeaderOwn));
        Assert.Equal([true, true, false, false], view.Rows.Select(r => r.FooterOwn));
        Assert.Equal(5, view.Rows[1].NumberStart);
        Assert.Equal(PageNumberFormat.LowerRoman, view.Rows[1].NumberFormat);
        Assert.True(view.Rows[1].FirstPageDifferent);
        Assert.Null(view.Rows[0].NumberStart);
        Assert.True(view.Rows[2].Setup.Landscape);
        Assert.Equal(2, view.Rows[2].Setup.Columns);
        Assert.True(view.LeftRight);
    }

    [Fact]
    public void ARowKnowsWhetherItsSetupDiffersFromTheSectionBefore()
    {
        var model = Model(EndsSection("a"), EndsSection("b"), EndsSection("c", "<w:cols w:num=\"2\" w:space=\"720\"/>"), Text("d"));
        var rows = new SectionsSource().Read(model, Pages(4)).Rows;
        Assert.False(rows[1].DiffersFrom(rows[0]));
        Assert.True(rows[2].DiffersFrom(rows[1]));
    }

    [Fact]
    public void MoreThan200SectionsShowTheTooManyRow()
    {
        var paragraphs = new List<string>();
        for (var i = 0; i < SectionsSource.MaximumSections + 1; i++)
        {
            paragraphs.Add(EndsSection("s" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        paragraphs.Add(Text("end"));
        var view = new SectionsSource().Read(Model([.. paragraphs]), []);
        Assert.True(view.TooMany);
        Assert.Empty(view.Rows);
        Assert.Equal(SectionsSource.MaximumSections + 2, view.SectionCount);

        // Exactly the bound lists every row.
        var fewer = paragraphs.Take(SectionsSource.MaximumSections - 1).Append(Text("end")).ToArray();
        var edge = new SectionsSource().Read(Model(fewer), []);
        Assert.False(edge.TooMany);
        Assert.Equal(SectionsSource.MaximumSections, edge.Rows.Count);
    }

    [Fact]
    public void TheAnswerIsMemoizedByRevision()
    {
        var model = Model(EndsSection("a"), Text("b"));
        var source = new SectionsSource();
        var first = source.Read(model, [0, 1]);
        Assert.Equal(1, source.Reads);
        Assert.Same(first, source.Read(model, [0, 1]));
        Assert.Equal(1, source.Reads);

        // Another page list (the layout went on) or a new revision reads again, and so does another model.
        var moved = source.Read(model, [0, 2]);
        Assert.NotSame(first, moved);
        Assert.Equal(2, source.Reads);
        model.Edits.Raise();
        _ = source.Read(model, [0, 2]);
        Assert.Equal(3, source.Reads);
        _ = source.Read(Model(Text("x")), [0]);
        Assert.Equal(4, source.Reads);
    }

    [Fact]
    public void AFreshSourceHasReadNothingAndTheFirstAskReadsOnce()
    {
        // The window makes no source before the pane is first opened (SectionsPaneTests.NothingOfThePaneExistsBeforeItIsOpened); the source itself reads only when asked.
        var source = new SectionsSource();
        Assert.Equal(0, source.Reads);
        _ = source.Read(Model(Text("a")), [0]);
        Assert.Equal(1, source.Reads);
    }

    private static IReadOnlyList<SectionRow> RowsWith(Action<IReadOnlyList<SectionInfo>> setUp, bool leftRight = false, params string[] paragraphs)
    {
        var model = Model(paragraphs);
        if (leftRight)
        {
            model.Settings = new DocumentSettings(new OoxmlRoot(System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main") + "settings", []));
            model.Settings.EvenAndOddHeaders = true;
        }

        setUp(SectionList.Of(model));
        return new SectionsSource().Read(model, []).Rows;
    }

    private static void Header(SectionInfo section, string id, HeaderFooterType type = HeaderFooterType.Default) =>
        section.Properties.HeaderFooterReferences.Add(new HeaderFooterReference(HeaderFooterKind.Header, type, id));

    [Fact]
    public void ASectionThatInheritsTheHeaderOfTheSectionBeforeChangesNothingWhenItsMarkerGoes()
    {
        // Section 1 has a header and section 2 sets none, so it shows section 1's: the merge changes no header (the raw references differ, what the pages show does not).
        var rows = RowsWith(s => Header(s[0], "rId90"), false, EndsSection("a"), EndsSection("b"), EndsSection("c"), Text("d"));
        Assert.Equal(MergeChanges.None, rows[1].ChangesWhenMerged(rows[0]));
        Assert.False(rows[1].DiffersFrom(rows[0]));
        Assert.Equal(MergeChanges.None, rows[2].ChangesWhenMerged(rows[1]));
    }

    [Fact]
    public void ADifferentHeaderIsNamedAndNothingElse()
    {
        var rows = RowsWith(s => { Header(s[0], "rId90"); Header(s[1], "rId91"); }, false, EndsSection("a"), EndsSection("b"), EndsSection("c"), Text("d"));
        Assert.Equal(MergeChanges.Headers, rows[1].ChangesWhenMerged(rows[0]));

        // Section 3 inherits section 2's own header: no change against section 2.
        Assert.Equal(MergeChanges.None, rows[2].ChangesWhenMerged(rows[1]));
    }

    [Fact]
    public void ADifferenceOfTheFirstPageSwitchAloneIsNamedAsTheSwitch()
    {
        var rows = RowsWith(s => Header(s[0], "rId90"), false, EndsSection("a"), EndsSection("b", "<w:titlePg/>"), Text("c"));
        Assert.Equal(MergeChanges.FirstPage, rows[1].ChangesWhenMerged(rows[0]));
    }

    [Fact]
    public void IdenticalSetupsChangeNothingAndEachGroupIsNamedOnItsOwn()
    {
        var plain = RowsWith(_ => { }, false, EndsSection("a"), EndsSection("b"), Text("c"));
        Assert.Equal(MergeChanges.None, plain[1].ChangesWhenMerged(plain[0]));

        var rows = RowsWith(
            s => s[1].Properties.HeaderFooterReferences.Add(new HeaderFooterReference(HeaderFooterKind.Footer, HeaderFooterType.Default, "rId95")),
            false,
            EndsSection("a"),
            EndsSection("b", "<w:pgNumType w:start=\"3\"/><w:cols w:num=\"2\" w:space=\"720\"/>"),
            Text("c"));
        Assert.Equal(MergeChanges.PageSetup | MergeChanges.Numbering | MergeChanges.Footers, rows[1].ChangesWhenMerged(rows[0]));
    }

    [Fact]
    public void TheEvenPartsCountOnlyWithLeftAndRightPagesAndTheFirstPageOnesOnlyWhenBothUseThem()
    {
        var withoutLeftRight = RowsWith(s => Header(s[1], "rId91", HeaderFooterType.Even), false, EndsSection("a"), EndsSection("b"), Text("c"));
        Assert.Equal(MergeChanges.None, withoutLeftRight[1].ChangesWhenMerged(withoutLeftRight[0]));
        var withLeftRight = RowsWith(s => Header(s[1], "rId91", HeaderFooterType.Even), true, EndsSection("a"), EndsSection("b"), Text("c"));
        Assert.Equal(MergeChanges.Headers, withLeftRight[1].ChangesWhenMerged(withLeftRight[0]));

        var firstOnly = RowsWith(s => Header(s[1], "rId92", HeaderFooterType.First), false, EndsSection("a"), EndsSection("b"), Text("c"));
        Assert.Equal(MergeChanges.None, firstOnly[1].ChangesWhenMerged(firstOnly[0]));
        var bothFirst = RowsWith(s => Header(s[1], "rId92", HeaderFooterType.First), false, EndsSection("a", "<w:titlePg/>"), EndsSection("b", "<w:titlePg/>"), Text("c"));
        Assert.Equal(MergeChanges.Headers, bothFirst[1].ChangesWhenMerged(bothFirst[0]));
    }

    [Fact]
    public void TheSentenceNamesEveryGroupThatChangesInBothLanguages()
    {
        const string Cols = "<w:cols w:num=\"2\" w:space=\"720\"/>";
        var three = RowsWith(s => Header(s[1], "rId91"), false, EndsSection("a"), EndsSection("b", Cols + "<w:titlePg/>"), Text("c"));
        var two = RowsWith(s => Header(s[1], "rId91"), false, EndsSection("a"), EndsSection("b", Cols), Text("c"));
        var one = RowsWith(s => Header(s[1], "rId91"), false, EndsSection("a"), EndsSection("b"), Text("c"));
        var none = RowsWith(_ => { }, false, EndsSection("a"), EndsSection("b"), Text("c"));
        var previous = WriteStrings.Culture;
        try
        {
            WriteStrings.Culture = System.Globalization.CultureInfo.GetCultureInfo("en");
            Assert.Equal("Section 2 takes over the page setup, the different first page setting and the header of section 1.", SectionsRowWording.RemoveTip(three[1], three[0]));
            Assert.Equal("Section 2 takes over the page setup and the header of section 1.", SectionsRowWording.RemoveTip(two[1], two[0]));
            Assert.Equal("Section 2 takes over the header of section 1.", SectionsRowWording.RemoveTip(one[1], one[0]));
            Assert.Equal("Merges section 2 into section 1.", SectionsRowWording.RemoveTip(none[1], none[0]));
            WriteStrings.Culture = System.Globalization.CultureInfo.GetCultureInfo("de");
            Assert.Equal("Abschnitt 2 übernimmt den Seitenaufbau, die Einstellung für die erste Seite und die Kopfzeile von Abschnitt 1.", SectionsRowWording.RemoveTip(three[1], three[0]));
            Assert.Equal("Abschnitt 2 übernimmt den Seitenaufbau und die Kopfzeile von Abschnitt 1.", SectionsRowWording.RemoveTip(two[1], two[0]));
            Assert.Equal("Abschnitt 2 übernimmt die Kopfzeile von Abschnitt 1.", SectionsRowWording.RemoveTip(one[1], one[0]));
            Assert.Equal("Fügt Abschnitt 2 mit Abschnitt 1 zusammen.", SectionsRowWording.RemoveTip(none[1], none[0]));
        }
        finally
        {
            WriteStrings.Culture = previous;
        }
    }

    [Fact]
    public void AnEmptyFinalSectionIsMarkedAndHasNothingToNumberFrom()
    {
        var rows = RowsWith(_ => { }, false, EndsSection("a"));
        Assert.Equal(2, rows.Count);
        Assert.False(rows[0].IsEmpty);
        Assert.True(rows[0].CanNumber);
        Assert.True(rows[1].IsEmpty);
        Assert.False(rows[1].CanNumber);
        Assert.Equal(WriteStrings.SectionEmptyFinal, SectionsRowWording.Start(rows[1]));
    }

    [Fact]
    public void ASectionThatStartsWithAnEmptyTableCannotBeNumbered()
    {
        var model = Model(EndsSection("a"), Text("b"));
        var sections = SectionList.Of(model);
        model.Body.Blocks.Insert(sections[1].FirstBlock, new Table());
        var rows = new SectionsSource().Read(model, []).Rows;
        Assert.False(rows[1].IsEmpty);
        Assert.False(rows[1].CanNumber);
        Assert.True(rows[0].CanNumber);
    }
}
