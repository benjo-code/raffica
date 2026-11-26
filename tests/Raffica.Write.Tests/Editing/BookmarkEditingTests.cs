using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>Bookmarks (milestone 4.5b): Word's name rule, adding one, the delete rule (a whole range deleted takes the markers, half a range moves them), hidden names, paste, ids and the history.</summary>
public sealed class BookmarkEditingTests
{
    private static string P(string text) => $"<w:p><w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r></w:p>";

    private static WordDocument Document(params string[] paragraphs) =>
        LayoutModel.Document(string.Concat(paragraphs.Select(P)) + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>");

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain) { FragmentCodec = new DocxFragmentCodec() };

    private static TextPosition At(WordDocument document, int block, int offset) => new(StoryRef.Body, (Paragraph)document.Body.Blocks[block], offset);

    private static TextRange Range(WordDocument document, int block, int from, int to) => new(At(document, block, from), At(document, block, to));

    private static List<Inline> Markers(WordDocument document) =>
        [.. BookmarkOperations.ParagraphsOf(document.Body.Blocks).SelectMany(p => BookmarkOperations.InlinesOf(p.Inlines)).Where(i => i is BookmarkStart or BookmarkEnd)];

    private static string Body(WordDocument document) => ModelSnapshot.Of(document).ToString(System.Xml.Linq.SaveOptions.DisableFormatting);

    [Fact]
    public void NamesFollowWordsRule()
    {
        string[] none = [];
        Assert.Null(BookmarkNames.Problem("a", none));
        Assert.Null(BookmarkNames.Problem("Chapter_1", none));
        Assert.Null(BookmarkNames.Problem(new string('a', 40), none));
        Assert.Equal(BookmarkNameProblem.Empty, BookmarkNames.Problem(string.Empty, none));
        Assert.Equal(BookmarkNameProblem.TooLong, BookmarkNames.Problem(new string('a', 41), none));
        Assert.Equal(BookmarkNameProblem.BadStart, BookmarkNames.Problem("1a", none));
        Assert.Equal(BookmarkNameProblem.BadStart, BookmarkNames.Problem("_x", none));
        Assert.Equal(BookmarkNameProblem.BadCharacter, BookmarkNames.Problem("a b", none));
        Assert.Equal(BookmarkNameProblem.BadCharacter, BookmarkNames.Problem("a-b", none));
        Assert.Equal(BookmarkNameProblem.BadStart, BookmarkNames.Problem("Äb", none));
    }

    [Fact]
    public void ADuplicateIgnoringCaseIsRefused()
    {
        var document = Document("hello world");
        var editor = Editor(document);
        Assert.NotNull(editor.AddBookmark(Range(document, 0, 0, 5), "Intro"));
        var revision = document.Edits.Revision;

        Assert.Null(editor.AddBookmark(Range(document, 0, 6, 11), "intro"));

        Assert.Equal(revision, document.Edits.Revision);
        Assert.Equal(BookmarkNameProblem.Duplicate, BookmarkNames.Problem("INTRO", BookmarkOperations.AllNames(document)));
        Assert.Equal(2, Markers(document).Count);
    }

    [Fact]
    public void AddThenUndoLeavesNoMarker()
    {
        var document = Document("hello world", "second");
        var editor = Editor(document);
        var before = Body(document);

        var result = editor.AddBookmark(Range(document, 0, 6, 11), "Tail");

        Assert.NotNull(result);
        Assert.Equal(2, Markers(document).Count);
        var info = Assert.Single(BookmarkOperations.Visible(document));
        Assert.Equal("Tail", info.Name);
        Assert.Equal(6, info.Range.Start.Offset);
        Assert.Equal(11, info.Range.End.Offset);
        var after = Body(document);

        Assert.NotNull(editor.History.Undo());
        Assert.Empty(Markers(document));
        Assert.Equal(before, Body(document));

        Assert.NotNull(editor.History.Redo());
        Assert.Equal(after, Body(document));
    }

    [Fact]
    public void ACollapsedRangeMakesAPointWithTheStartFirst()
    {
        var document = Document("hello");
        var editor = Editor(document);

        Assert.NotNull(editor.AddBookmark(Range(document, 0, 5, 5), "Point"));
        Assert.NotNull(editor.AddBookmark(Range(document, 0, 0, 0), "Head"));
        Assert.NotNull(editor.AddBookmark(Range(document, 0, 2, 2), "Middle"));

        var markers = Markers(document);
        Assert.Equal(6, markers.Count);
        foreach (var bookmark in BookmarkOperations.Visible(document))
        {
            Assert.Equal(bookmark.Range.Start.Offset, bookmark.Range.End.Offset);
            var at = markers.FindIndex(m => m is BookmarkStart s && s.Id == bookmark.Id);
            Assert.True(markers[at + 1] is BookmarkEnd e && e.Id == bookmark.Id, bookmark.Name + ": the end follows its start");
        }

        Assert.Equal("hello", TestEdit.TextOf((Paragraph)document.Body.Blocks[0]));
    }

    [Fact]
    public void ARangeAcrossParagraphsIsFoundAndNextIdIsOneMore()
    {
        var document = Document("one", "two", "three");
        var editor = Editor(document);

        Assert.NotNull(editor.AddBookmark(new TextRange(At(document, 0, 1), At(document, 2, 2)), "Span"));
        Assert.NotNull(editor.AddBookmark(Range(document, 1, 0, 3), "Second"));

        var span = editor.FindBookmark("span")!.Value;
        Assert.Equal(1, span.Start.Offset);
        Assert.Same(document.Body.Blocks[0], span.Start.Paragraph);
        Assert.Same(document.Body.Blocks[2], span.End.Paragraph);
        Assert.Equal(2, span.End.Offset);
        Assert.Equal([0, 1], BookmarkOperations.Visible(document).Select(b => b.Id).Order().ToArray());
        Assert.Null(editor.FindBookmark("missing"));
    }

    [Fact]
    public void DeletingTheWholeRangeRemovesBothMarkersAndUndoRestores()
    {
        var document = Document("hello world");
        var editor = Editor(document);
        Assert.NotNull(editor.AddBookmark(Range(document, 0, 0, 5), "Greeting"));
        var before = Body(document);

        var result = editor.Delete(Range(document, 0, 0, 5));

        Assert.NotNull(result);
        Assert.Empty(Markers(document));
        Assert.Equal(" world", TestEdit.TextOf((Paragraph)document.Body.Blocks[0]));
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(before, Body(document));
        Assert.Equal(2, Markers(document).Count);
        Assert.NotNull(editor.History.Redo());
        Assert.Empty(Markers(document));
    }

    [Fact]
    public void DeletingAWholeRangeAcrossParagraphsRemovesTheMarkersAndKeepsOthers()
    {
        var document = Document("one", "two", "three");
        var editor = Editor(document);
        Assert.NotNull(editor.AddBookmark(new TextRange(At(document, 0, 0), At(document, 1, 3)), "Inner"));
        Assert.NotNull(editor.AddBookmark(Range(document, 2, 0, 5), "Outer"));
        var before = Body(document);

        Assert.NotNull(editor.Delete(new TextRange(At(document, 0, 0), At(document, 1, 3))));

        Assert.Equal(["Outer"], BookmarkOperations.Visible(document).Select(b => b.Name).ToArray());
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(before, Body(document));
    }

    [Fact]
    public void DeletingHalfTheRangeMovesTheAnchor()
    {
        var document = Document("hello world");
        var editor = Editor(document);
        Assert.NotNull(editor.AddBookmark(Range(document, 0, 0, 5), "Greeting"));

        Assert.NotNull(editor.Delete(Range(document, 0, 3, 8)));

        Assert.Equal(2, Markers(document).Count);
        var info = Assert.Single(BookmarkOperations.Visible(document));
        Assert.Equal("Greeting", info.Name);
        Assert.Equal(0, info.Range.Start.Offset);
        Assert.Equal(3, info.Range.End.Offset);
    }

    [Fact]
    public void ADeleteThatLeavesBookmarkedTextKeepsTheBookmark()
    {
        var document = Document("hello world");
        var editor = Editor(document);
        Assert.NotNull(editor.AddBookmark(Range(document, 0, 0, 11), "All"));

        Assert.NotNull(editor.Delete(Range(document, 0, 2, 4)));

        var info = Assert.Single(BookmarkOperations.Visible(document));
        Assert.Equal(0, info.Range.Start.Offset);
        Assert.Equal(9, info.Range.End.Offset);
    }

    [Fact]
    public void ADeletedPointStaysBecauseItHoldsNoText()
    {
        var document = Document("hello world");
        var editor = Editor(document);
        Assert.NotNull(editor.AddBookmark(Range(document, 0, 3, 3), "Point"));

        Assert.NotNull(editor.Delete(Range(document, 0, 0, 5)));

        Assert.Equal(2, Markers(document).Count);
    }

    [Fact]
    public void HiddenNamesAreNeverListed()
    {
        var document = LayoutModel.Document(
            "<w:p><w:bookmarkStart w:id=\"7\" w:name=\"_Ref123\"/><w:r><w:t>text</w:t></w:r><w:bookmarkEnd w:id=\"7\"/></w:p>"
            + "<w:p><w:bookmarkStart w:id=\"8\" w:name=\"Shown\"/><w:r><w:t>more</w:t></w:r><w:bookmarkEnd w:id=\"8\"/></w:p>"
            + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>");
        var editor = Editor(document);

        Assert.Equal(["Shown"], BookmarkOperations.Visible(document).Select(b => b.Name).ToArray());
        Assert.Equal(2, BookmarkOperations.AllNames(document).Count);
        Assert.NotNull(editor.FindBookmark("_Ref123"));
        Assert.Null(editor.AddBookmark(Range(document, 0, 0, 2), "_mine"));

        var added = editor.AddBookmark(Range(document, 0, 0, 2), "Next");
        Assert.NotNull(added);
        Assert.Contains(BookmarkOperations.Visible(document), b => b.Name == "Next" && b.Id == 9);
    }

    [Fact]
    public void AFragmentPasteCarriesNoBookmarks()
    {
        var document = Document("hello world", "target");
        var editor = Editor(document);
        Assert.NotNull(editor.AddBookmark(Range(document, 0, 0, 5), "Greeting"));
        var copied = editor.Copy(Range(document, 0, 0, 11));
        Assert.NotNull(copied.Fragment);

        Assert.NotNull(editor.Paste(Range(document, 1, 6, 6), copied, _ => true));

        Assert.Equal(2, Markers(document).Count);
        Assert.Equal(["Greeting"], BookmarkOperations.Visible(document).Select(b => b.Name).ToArray());
    }

    [Fact]
    public void IdsStayUniqueAfterSplitAndPaste()
    {
        var document = Document("first paragraph here", "second");
        var editor = Editor(document);
        Assert.NotNull(editor.AddBookmark(Range(document, 0, 0, 20), "Whole"));
        Assert.NotNull(editor.AddBookmark(Range(document, 1, 0, 3), "Part"));

        // A split inside the bookmark's range, a paste of text that held the bookmark, then one more bookmark.
        var split = editor.InsertText(Range(document, 0, 5, 5), "\n");
        Assert.NotNull(split);
        var copied = editor.Copy(new TextRange(At(document, 0, 0), At(document, 1, 3)));
        Assert.NotNull(editor.Paste(new TextRange(At(document, 2, 1), At(document, 2, 1)), copied, _ => true));
        Assert.NotNull(editor.AddBookmark(Range(document, 2, 0, 2), "Later"));

        var markers = Markers(document);
        var starts = markers.OfType<BookmarkStart>().ToList();
        var ends = markers.OfType<BookmarkEnd>().ToList();
        Assert.Equal(starts.Count, starts.Select(s => s.Id).Distinct().Count());
        Assert.Equal(starts.Count, ends.Count);
        foreach (var start in starts)
        {
            Assert.Single(ends, e => e.Id == start.Id);
        }

        Assert.Equal(starts.Count, starts.Select(s => s.Name.ToUpperInvariant()).Distinct().Count());
    }

    [Fact]
    public void AnIdTakenInAHeaderIsNeverReused()
    {
        var header = $"<w:hdr xmlns:w=\"{LayoutModel.W}\"><w:p><w:bookmarkStart w:id=\"41\" w:name=\"InHeader\"/><w:r><w:t>h</w:t></w:r><w:bookmarkEnd w:id=\"41\"/></w:p></w:hdr>";
        var document = ChromeTestKit.Document(P("hello") + "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>", new Dictionary<string, string> { ["rId9"] = header });
        var editor = Editor(document);

        Assert.NotNull(editor.AddBookmark(Range(document, 0, 0, 5), "InBody"));

        Assert.Equal(42, Assert.Single(BookmarkOperations.Visible(document)).Id);
    }

    [Fact]
    public void PrefixDocumentRefuses()
    {
        var document = Document("hello world");
        var editor = Editor(document);
        Assert.NotNull(editor.AddBookmark(Range(document, 0, 0, 5), "Keep"));
        var revision = document.Edits.Revision;

        Assert.Null(editor.AddBookmark(Range(document, 0, 6, 11), "Other", prefixDocument: true));
        Assert.Null(editor.DeleteBookmark("Keep", prefixDocument: true));

        Assert.Equal(revision, document.Edits.Revision);
        Assert.Equal(2, Markers(document).Count);
    }

    [Fact]
    public void DeleteBookmarkRemovesBothMarkersKeepsTheTextAndUndoRestores()
    {
        var document = Document("one", "two");
        var editor = Editor(document);
        Assert.NotNull(editor.AddBookmark(new TextRange(At(document, 0, 1), At(document, 1, 2)), "Span"));
        var before = Body(document);

        var result = editor.DeleteBookmark("SPAN");

        Assert.NotNull(result);
        Assert.Empty(Markers(document));
        Assert.Equal("one", TestEdit.TextOf((Paragraph)document.Body.Blocks[0]));
        Assert.Null(editor.DeleteBookmark("Span"));
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(before, Body(document));
        Assert.NotNull(editor.History.Redo());
        Assert.Empty(Markers(document));
    }
}
