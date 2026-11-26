using System.Globalization;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// The page-based field results of milestone 4.5b (task 11): <c>PAGEREF</c> fields and the page numbers of a table of contents, from a real layout through <see cref="FieldPages"/>, in the section's number format and start; and when they are
/// given (a complete layout and the scope that asks for them), what is rewritten and what a refresh that finds nothing does.
/// </summary>
public sealed class PageRefreshTests
{
    private const string Plain = "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgMar w:top=\"1440\" w:right=\"1440\" w:bottom=\"1440\" w:left=\"1440\" w:header=\"720\" w:footer=\"720\" w:gutter=\"0\"/></w:sectPr>";

    private static string Run(string text) => $"<w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    private static string P(string inner, string? style = null, string extra = "") =>
        $"<w:p><w:pPr>{(style is null ? string.Empty : $"<w:pStyle w:val=\"{style}\"/>")}{extra}</w:pPr>{inner}</w:p>";

    private static string Field(string instruction, string result) =>
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
        + $"<w:r><w:instrText xml:space=\"preserve\"> {instruction} </w:instrText></w:r>"
        + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>"
        + Run(result)
        + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";

    private static string Mark(int id, string name, string text) => $"<w:bookmarkStart w:id=\"{id}\" w:name=\"{name}\"/>{Run(text)}<w:bookmarkEnd w:id=\"{id}\"/>";

    private static string PageBreak => "<w:r><w:br w:type=\"page\"/></w:r>";

    private static string Styles()
    {
        var styles = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>";
        for (var i = 1; i <= 9; i++)
        {
            styles += $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{i}\"><w:name w:val=\"heading {i}\"/><w:pPr><w:outlineLvl w:val=\"{i - 1}\"/></w:pPr></w:style>";
        }

        return LayoutModel.StylesPart(styles);
    }

    private static string Section(string numbering) => $"<w:p><w:pPr><w:sectPr><w:type w:val=\"nextPage\"/><w:pgSz w:w=\"12240\" w:h=\"15840\"/>{numbering}</w:sectPr></w:pPr></w:p>";

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain);

    private static async Task<LayoutFieldHost> HostAsync(WordDocument document) => new(document, await SectionOracleTests.LaidAsync(document));

    private static Paragraph Para(WordDocument document, int block) => (Paragraph)document.Body.Blocks[block];

    private static string TextOf(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;

    // The stored result of the nth field of the paragraph.
    private static string? Stored(WordDocument document, int block, int ordinal = 0) => ReferenceFields.Stored(Para(document, block), ordinal);

    // Roman front matter, a restart at 1 for the main matter, a letter format for the appendix: PAGEREF gives the number the footer's PAGE field prints on that page.
    [Fact]
    public async Task PagerefGivesThePrintedNumberInTheSectionFormat()
    {
        var body = P(Mark(1, "Front1", "front one"))
            + P(Mark(2, "Front2", "front two"), null, "<w:pageBreakBefore/>")
            + Section("<w:pgNumType w:fmt=\"lowerRoman\"/>")
            + P(Mark(3, "Main1", "main one"))
            + P(Mark(4, "Main2", "main two"), null, "<w:pageBreakBefore/>")
            + Section("<w:pgNumType w:start=\"1\"/>")
            + P(Mark(5, "App1", "appendix"))
            + P(Run("see ") + Field("PAGEREF Front1 \\h", "?") + Run(" ") + Field("PAGEREF Front2 \\h", "?") + Run(" ") + Field("PAGEREF Main1 \\h", "?") + Run(" ") + Field("PAGEREF Main2 \\h", "?") + Run(" ") + Field("PAGEREF App1 \\h", "?") + Run(" ") + Field("PAGEREF Missing \\h", "kept"))
            + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgNumType w:fmt=\"upperLetter\" w:start=\"1\"/></w:sectPr>";
        var document = LayoutModel.Document(body, Styles());
        var layout = await SectionOracleTests.LaidAsync(document);
        var host = new LayoutFieldHost(document, layout);
        var last = document.Body.Blocks.Count - 1;

        var result = Editor(document).RefreshFields(host, RefreshScope.WithPages);

        Assert.Equal(["i", "ii", "1", "2", "A"], Enumerable.Range(0, 5).Select(i => Stored(document, last, i)));
        Assert.Equal("kept", Stored(document, last, 5));
        Assert.Equal(5, result.Written);
        Assert.Equal(1, result.NotUpdated);

        // The same text as the page shows in its footer (the layout's own number), page by page.
        Assert.Equal(["i", "ii", "1", "2", "A"], Enumerable.Range(0, layout.LaidOutPages).Select(p => layout.Page(p).Number));
    }

    // A footer that prints "A-" in front of the number: the printed page number of the field is the number (the prefix is the footer's text, not the page number).
    [Fact]
    public async Task AnAppendixFooterPrefixIsNotPartOfThePrintedNumber()
    {
        var body = P(Run("one")) + Section(string.Empty)
            + P(Mark(1, "App1", "appendix")) + P(Run("see ") + Field("PAGEREF App1 \\h", "?"))
            + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/><w:pgNumType w:start=\"1\"/></w:sectPr>";
        var document = LayoutModel.Document(body, Styles());
        var host = await HostAsync(document);

        Editor(document).RefreshFields(host, RefreshScope.WithPages);

        Assert.Equal("1", Stored(document, document.Body.Blocks.Count - 1));
    }

    private static string TocDocumentBody()
    {
        var body = P(string.Empty);
        for (var i = 0; i < 3; i++)
        {
            body += P(Run("Chapter " + "ABC"[i]), "Heading1", i == 0 ? string.Empty : "<w:pageBreakBefore/>");
        }

        return body + Plain;
    }

    private static List<string> Lines(WordDocument document)
    {
        var site = TocRefresh.Find(document.Body.Blocks)[0];
        var last = TocRefresh.Analyze(site)?.TailParagraph == true ? site.Last - 1 : site.Last;
        return [.. Enumerable.Range(site.First, last - site.First + 1).Select(i => string.Concat(ModelWalk.Inlines(((Paragraph)site.Container[i]).Inlines).OfType<Run>().SelectMany(r => r.Content).Select(c => c switch { TextContent t => t.Text, TabContent => "|", _ => string.Empty })).TrimStart())];
    }

    private static TocOptions Options => new(1, 3, true, true, TocLeader.Dots, null);

    private static Paragraph Break()
    {
        var paragraph = new Paragraph();
        var run = new Run();
        run.Content.Add(new BreakContent(Raffica.Write.Model.BreakKind.Page));
        paragraph.Inlines.Add(run);
        return paragraph;
    }

    [Fact]
    public async Task ATocPageNumberFollowsAnInsertedPageBreak()
    {
        var document = LayoutModel.Document(TocDocumentBody(), Styles());
        var editor = Editor(document);
        Assert.NotNull(editor.InsertToc(new TextPosition(StoryRef.Body, Para(document, 0), 0), Options, new FakeFieldHost(document)));
        editor.RefreshFields(await HostAsync(document), RefreshScope.WithPages);
        Assert.Equal(["Chapter A|1", "Chapter B|2", "Chapter C|3"], Lines(document));

        // A page break before chapter B: B and C move one page down, A stays.
        var index = document.Body.Blocks.ToList().FindIndex(b => b is Paragraph p && TextOf(p) == "Chapter B");
        document.Body.Blocks.Insert(index, Break());
        var again = editor.RefreshFields(await HostAsync(document), RefreshScope.WithPages);

        Assert.Equal(["Chapter A|1", "Chapter B|3", "Chapter C|4"], Lines(document));
        Assert.Equal(1, again.Written);
        Assert.False(again.Settled, "something was written, so the numbers are read once more");
        var third = editor.RefreshFields(await HostAsync(document), RefreshScope.WithPages);
        Assert.Equal(0, third.Written);
        Assert.True(third.Settled);
    }

    [Fact]
    public async Task AStableDocumentWritesNothingAndIsSettledInOnePass()
    {
        var document = LayoutModel.Document(TocDocumentBody(), Styles());
        var editor = Editor(document);
        editor.InsertToc(new TextPosition(StoryRef.Body, Para(document, 0), 0), Options, new FakeFieldHost(document));
        editor.RefreshFields(await HostAsync(document), RefreshScope.WithPages);
        var before = ModelSnapshot.Of(document).ToString();
        var derived = document.Edits.DerivedVersion;

        var result = editor.RefreshFields(await HostAsync(document), RefreshScope.WithPages);

        Assert.Equal(0, result.Written);
        Assert.Empty(result.Content);
        Assert.True(result.Settled);
        Assert.Equal(derived, document.Edits.DerivedVersion);
        Assert.Equal(before, ModelSnapshot.Of(document).ToString());
    }

    [Fact]
    public async Task NothingIsWrittenBeforeTheLayoutIsComplete()
    {
        var body = P(Mark(1, "Bk", "target")) + P(PageBreak + Run("two") + Field("PAGEREF Bk \\h", "9")) + Plain;
        var document = LayoutModel.Document(body, Styles());
        var layout = DocumentLayout.Create(document, PageTestKit.Fonts, new LayoutOptions());
        layout.LayOutFirstPage();
        var host = new LayoutFieldHost(document, layout);
        Assert.False(host.IsComplete);
        var before = ModelSnapshot.Of(document).ToString();

        var early = Editor(document).RefreshFields(host, RefreshScope.WithPages);

        Assert.Equal(0, early.Written);
        Assert.Equal(0, host.PageQuestions);
        Assert.False(early.Settled, "a page result may still change once the layout is complete");
        Assert.Equal(1, early.PageFields);
        Assert.Equal(before, ModelSnapshot.Of(document).ToString());
        Assert.Equal("9", Stored(document, 1));

        // The order-based scope never gives a page, even from a complete layout.
        await layout.LayOutRestAsync(null, CancellationToken.None);
        var orderOnly = Editor(document).RefreshFields(host, RefreshScope.OrderBased);
        Assert.Equal(0, orderOnly.Written);
        Assert.Equal("9", Stored(document, 1));

        var late = Editor(document).RefreshFields(host, RefreshScope.WithPages);
        Assert.Equal(1, late.Written);
        Assert.Equal("1", Stored(document, 1));
    }

    [Fact]
    public void OnlyEntriesOfChangedPagesAreRewritten()
    {
        var body = P(string.Empty);
        for (var i = 0; i < 4; i++)
        {
            body += P(Run("Chapter " + i), "Heading1");
        }

        var document = LayoutModel.Document(body + Plain, Styles());
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        for (var i = 0; i < 4; i++)
        {
            host.Pages[Para(document, 1 + i)] = (i, (i + 1).ToString(CultureInfo.InvariantCulture));
        }

        editor.InsertToc(new TextPosition(StoryRef.Body, Para(document, 0), 0), Options, host);
        editor.RefreshFields(host, RefreshScope.WithPages);
        Assert.Equal(["Chapter 0|1", "Chapter 1|2", "Chapter 2|3", "Chapter 3|4"], Lines(document));
        var site = TocRefresh.Find(document.Body.Blocks)[0];
        var objects = Enumerable.Range(site.First, 4).Select(i => document.Body.Blocks[i]).ToList();
        var bytes = objects.Select(o => TocDocument.Dump(TocDocument.Document(o.Clone()))).ToList();

        var heading = document.Body.Blocks.OfType<Paragraph>().First(p => TextOf(p) == "Chapter 2");
        host.Pages[heading] = (7, "12");
        var result = editor.RefreshFields(host, RefreshScope.WithPages);

        Assert.Equal(1, result.Written);
        Assert.Single(result.Content);
        Assert.Equal(site.First + 2, result.Content[0].First);
        Assert.Equal(["Chapter 0|1", "Chapter 1|2", "Chapter 2|12", "Chapter 3|4"], Lines(document));
        for (var i = 0; i < 4; i++)
        {
            Assert.Same(objects[i], document.Body.Blocks[site.First + i]);
            if (i != 2)
            {
                Assert.Equal(bytes[i], TocDocument.Dump(TocDocument.Document(document.Body.Blocks[site.First + i].Clone())));
            }
        }
    }

    // What the insert stores for a page reference is what a refresh gives: the same host answers both.
    [Fact]
    public async Task AnInsertedPageCrossReferenceEqualsWhatARefreshGives()
    {
        var body = P(Run("one")) + P(PageBreak + Run("two")) + P(Run("Chapter"), "Heading1", "<w:pageBreakBefore/>") + P(Run("tail")) + Plain;
        var document = LayoutModel.Document(body, Styles());
        var host = await HostAsync(document);
        var editor = Editor(document);
        var target = CrossReferences.Targets(document, host, CrossRefTargetKind.Heading)[0];

        var result = editor.InsertCrossReference(new TextPosition(StoryRef.Body, Para(document, 1), 0), target, CrossRefKind.Page, host);

        Assert.NotNull(result);
        var made = result!.Caret.Paragraph;
        var inserted = ReferenceFields.Stored(made, 0);
        Assert.NotEqual("?", inserted);

        // A refresh over a layout of the changed document finds nothing to change: the stored page is the one it gives.
        var refresh = editor.RefreshFields(await HostAsync(document), RefreshScope.WithPages);
        Assert.Equal(inserted, ReferenceFields.Stored(made, 0));
        Assert.Equal(0, refresh.Written);
    }

    [Fact]
    public async Task APagerefToATableCellAndAContentControlAnswersWithTheEnclosingBlocksPage()
    {
        var cell = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"4000\"/></w:tblGrid><w:tr><w:tc><w:tcPr/>" + P(Mark(1, "InCell", "cell text")) + "</w:tc></w:tr></w:tbl>";
        var control = "<w:sdt><w:sdtPr/><w:sdtContent>" + P(Mark(2, "InControl", "control text")) + "</w:sdtContent></w:sdt>";
        var body = P(Run("one")) + P(PageBreak + Run("two")) + cell + control + P(Run("see ") + Field("PAGEREF InCell \\h", "?") + Run(" ") + Field("PAGEREF InControl \\h", "?")) + Plain;
        var document = LayoutModel.Document(body, Styles());
        var host = await HostAsync(document);

        Editor(document).RefreshFields(host, RefreshScope.WithPages);

        var last = document.Body.Blocks.Count - 1;
        Assert.Equal("2", Stored(document, last, 0));
        Assert.Equal("2", Stored(document, last, 1));
    }
}
