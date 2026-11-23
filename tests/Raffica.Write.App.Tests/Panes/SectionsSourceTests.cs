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
    public void NothingIsReadBeforeTheFirstAsk()
    {
        var source = new SectionsSource();
        Assert.Equal(0, source.Reads);
    }
}
