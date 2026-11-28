using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// A new bookmark never takes an id or a name the document already holds (milestone 4.5b): also the ones kept as content the model does not read (between blocks, in a table row, inside a wrapper or a text box) and those of the
/// headers, footers, notes and comments; and a name Write generates is never one a field or a link still points at, so an old reference cannot come to mean something else.
/// </summary>
public sealed class BookmarkReuseTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private const string Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private static readonly string[] s_foreign =
    [
        "_Toc200000001", "BetweenRows", "InRow", "InCell", "InWrapper", "InTextBox", "InHeader", "InFooter", "_Ref200000005", "InEndnote", "InComment",
    ];

    private static string Run(string text) => $"<w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    private static string P(string inner, string? style = null) => $"<w:p>{(style is null ? string.Empty : $"<w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr>")}{inner}</w:p>";

    private static string Start(int id, string name) => $"<w:bookmarkStart w:id=\"{id}\" w:name=\"{name}\"/>";

    private static string End(int id) => $"<w:bookmarkEnd w:id=\"{id}\"/>";

    private static string Field(string instruction) =>
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
        + $"<w:r><w:instrText xml:space=\"preserve\"> {instruction} </w:instrText></w:r>"
        + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>"
        + Run("old")
        + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";

    private static WordDocument Document(string body)
    {
        var styles = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>"
            + "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/><w:pPr><w:outlineLvl w:val=\"0\"/></w:pPr></w:style>";
        return LayoutModel.Document(body + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>", LayoutModel.StylesPart(styles));
    }

    private static Story StoryOf(string root, int id, string name)
    {
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new BookmarkStart(id, name));
        paragraph.Inlines.Add(new BookmarkEnd(id));
        var story = new Story(new OoxmlRoot(W + root, []));
        story.Blocks.Add(paragraph);
        return story;
    }

    // One bookmark in every place Write holds one besides the paragraphs of the body.
    private static WordDocument WithForeignMarkers()
    {
        var table = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"4000\"/></w:tblGrid>" + Start(30, "BetweenRows")
            + "<w:tr>" + Start(31, "InRow") + "<w:tc><w:tcPr/>" + Start(32, "InCell") + End(32) + P(Run("cell")) + "</w:tc>" + End(31) + "</w:tr>" + End(30) + "</w:tbl>";
        var body = P(Run("Chapter"), "Heading1")
            + Start(7, "_Toc200000001") + P(Run("body level")) + End(7)
            + table
            + P($"<w:customXml w:element=\"x\">{Start(40, "InWrapper")}{Run("wrapped")}{End(40)}</w:customXml>")
            + P($"<w:r><mc:AlternateContent xmlns:mc=\"{Mc}\"><mc:Fallback><w:pict><w:txbxContent><w:p>{Start(41, "InTextBox")}{End(41)}</w:p></w:txbxContent></w:pict></mc:Fallback></mc:AlternateContent></w:r>")
            + P(Run("the end"));
        var document = Document(body);
        document.HeadersFooters["rId9"] = new HeaderFooterPart(StoryOf("hdr", 50, "InHeader"));
        document.HeadersFooters["rId10"] = new HeaderFooterPart(StoryOf("ftr", 51, "InFooter"));
        var footnotes = new NoteCollection(new OoxmlRoot(W + "footnotes", []));
        footnotes.Notes.Add(new Note(1, NoteKind.Normal, StoryOf("footnote", 60, "_Ref200000005")));
        document.Footnotes = footnotes;
        var endnotes = new NoteCollection(new OoxmlRoot(W + "endnotes", []));
        endnotes.Notes.Add(new Note(1, NoteKind.Normal, StoryOf("endnote", 61, "InEndnote")));
        document.Endnotes = endnotes;
        var comments = new CommentCollection(new OoxmlRoot(W + "comments", []));
        comments.Comments.Add(new Comment(0, StoryOf("comment", 62, "InComment")));
        document.Comments = comments;
        return document;
    }

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain);

    private static Paragraph Para(WordDocument document, string text) =>
        ModelWalk.Blocks(document.Body.Blocks).OfType<Paragraph>().First(p => ParagraphProjection.Build(p, ProjectionHost.Plain).Text == text);

    private static BookmarkStart Marker(WordDocument document, string name) =>
        ModelWalk.Blocks(document.Body.Blocks).OfType<Paragraph>().SelectMany(p => ModelWalk.Inlines(p.Inlines)).OfType<BookmarkStart>().Single(s => s.Name == name);

    [Fact]
    public void EveryBookmarkTheDocumentHoldsIsKnownByNameAndId()
    {
        var document = WithForeignMarkers();

        Assert.Equal(s_foreign.Order(StringComparer.Ordinal), BookmarkOperations.AllNames(document).Order(StringComparer.Ordinal));
        Assert.Equal(63, BookmarkOperations.NextId(document));
    }

    [Theory]
    [InlineData("betweenrows")]
    [InlineData("InRow")]
    [InlineData("InCell")]
    [InlineData("InWrapper")]
    [InlineData("InTextBox")]
    [InlineData("InHeader")]
    [InlineData("InFooter")]
    [InlineData("InEndnote")]
    [InlineData("InComment")]
    public void ANameHeldAnywhereIsTaken(string name)
    {
        var document = WithForeignMarkers();
        var editor = Editor(document);
        var end = Para(document, "the end");
        var at = new TextPosition(StoryRef.Body, end, 0);

        Assert.Null(editor.AddBookmark(new TextRange(at, at), name));

        Assert.Equal(BookmarkNameProblem.Duplicate, BookmarkNames.Problem(name, BookmarkOperations.AllNames(document)));
        Assert.False(editor.History.CanUndo);
    }

    [Fact]
    public void TheBookmarksWriteMakesTakeIdsAndNamesNothingElseHas()
    {
        var document = WithForeignMarkers();
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        var end = Para(document, "the end");
        var at = new TextPosition(StoryRef.Body, end, 0);

        Assert.NotNull(editor.AddBookmark(new TextRange(at, at), "Mine"));
        Assert.Equal(63, Marker(document, "Mine").Id);

        var heading = CrossReferences.Targets(document, host, CrossRefTargetKind.Heading)[0];
        Assert.NotNull(editor.InsertCrossReference(new TextPosition(StoryRef.Body, end, 7), heading, CrossRefKind.Text, host));
        Assert.Equal(64, Marker(document, "_Ref200000006").Id);

        Assert.NotNull(editor.InsertToc(new TextPosition(StoryRef.Body, Para(document, "body level"), 0), new TocOptions(1, 3, true, true, TocLeader.Dots, null), host));
        Assert.Equal(65, Marker(document, "_Toc200000002").Id);
    }

    // The bookmarks these fields and links point at are gone (their text was deleted): a new cross-reference bookmark under such a name would make the old reference mean the new target.
    [Fact]
    public void ACrossReferenceNameAFieldOrALinkStillPointsAtIsNotUsedAgain()
    {
        var body = P(Run("Chapter"), "Heading1")
            + P(Run("see ") + Field("REF _Ref100000007 \\h"))
            + P(Field("PAGEREF _Ref100000004 \\h"))
            + P("<w:hyperlink w:anchor=\"_Ref100000009\">" + Run("a link") + "</w:hyperlink>")
            + P("<w:fldSimple w:instr=\" NOTEREF _Ref100000011 \"><w:r><w:t>1</w:t></w:r></w:fldSimple>")
            + P($"<w:customXml w:element=\"x\">{Field("REF _Ref100000013 \\h")}</w:customXml>")
            + P(Run("the end"));
        var document = Document(body);
        var footer = new Story(new OoxmlRoot(W + "ftr", []));
        var pointing = new Paragraph();
        pointing.Inlines.Add(new SimpleField(" PAGEREF _Ref100000015 \\h "));
        footer.Blocks.Add(pointing);
        document.HeadersFooters["rId10"] = new HeaderFooterPart(footer);
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        var end = Para(document, "the end");

        Assert.Empty(BookmarkOperations.AllNames(document));
        var heading = CrossReferences.Targets(document, host, CrossRefTargetKind.Heading)[0];
        Assert.NotNull(editor.InsertCrossReference(new TextPosition(StoryRef.Body, end, 0), heading, CrossRefKind.Text, host));
        Marker(document, "_Ref100000016");
    }
}
