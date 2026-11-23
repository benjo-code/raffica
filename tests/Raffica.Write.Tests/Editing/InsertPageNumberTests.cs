using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;
using static Raffica.Write.Tests.Layout.ChromeTestKit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Editing;

/// <summary>Insert page number: what the header or footer story holds afterwards, what a laid-out copy draws, the layout against a full layout, the sharing and the history (one step per command).</summary>
public sealed class InsertPageNumberTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly PageNumberWords English = new("Page", "of");

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain);

    private static string Pg(string text) => P(text, "<w:pageBreakBefore/>");

    private static Dictionary<string, string> Parts(params (string Id, string Xml)[] parts) => parts.ToDictionary(p => p.Id, p => p.Xml, StringComparer.Ordinal);

    private static TextPosition At(WordDocument document, int block, int offset = 0) => new(StoryRef.Body, (Paragraph)document.Body.Blocks[block], offset);

    // Three pages and no footer at all.
    private static WordDocument Three() => Document(P("p1") + Pg("p2") + Pg("p3") + Sect());

    // Two sections of two pages: the first has the default footer "fA" ("A"), the second sets nothing and inherits it.
    private static WordDocument Two() =>
        Document(
            P("p1") + PEnd("p2", Sect(Footer("default", "fA")), "<w:pageBreakBefore/>") + P("p3") + Pg("p4") + Sect(),
            Parts(("fA", FooterPart(Para(Text("A"))))));

    private static DocumentLayout Lay(WordDocument document) => Finish(DocumentLayout.Create(document, Fonts, new LayoutOptions()));

    private static string[] Feet(WordDocument document) => [.. Pages(Lay(document)).Select(page => Lines(page)[^1])];

    private static List<string> Snapshot(WordDocument document) =>
        [.. SectionList.Of(document).Select(s => string.Join("|", s.Properties.Children.Select(c => c.ToString(SaveOptions.DisableFormatting)).Concat(s.Properties.HeaderFooterReferences.Select(r => $"{r.Kind}{r.Type}{r.RelationshipId}"))))
            .Concat(document.HeadersFooters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + string.Concat(p.Value.Content.Blocks.Select(b => b.ToString()))))];

    private static string Raw(Paragraph paragraph) =>
        string.Concat(paragraph.Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<TextContent>().Select(t => t.Text));

    private static int Fields(Paragraph paragraph, string instruction) =>
        paragraph.Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<InstructionText>().Count(i => i.Text.Trim().Equals(instruction, StringComparison.Ordinal));

    private static Paragraph FirstOf(WordDocument document, string id) => (Paragraph)document.HeadersFooters[id].Content.Blocks[0];

    // The incremental layout after the command against a full one: the restart the session does for a section change, the chrome path for a story edit.
    private static void LayoutAgrees(DocumentLayout before, WordDocument document, EditResult result, string label)
    {
        if (result.Sections is not null)
        {
            before.Resection(0);
        }
        else
        {
            before.ApplyEdit(result.Changes[0], 1);
        }

        LayoutOracle.AssertEqualToFull(before, document, label: label);
    }

    [Fact]
    public void AFooterOfABlankDocumentGetsACentredPageNumber()
    {
        var document = Three();
        var layout = Lay(document);
        var editor = Editor(document);

        var result = editor.InsertPageNumber(At(document, 0), PageNumberLook.Plain, HeaderFooterKind.Footer, PageNumberAlign.Center, English, "1", "3");

        Assert.NotNull(result);
        Assert.NotNull(result.Sections);
        Assert.True(result.KeepSelection);
        var reference = SectionList.Of(document)[0].Properties.HeaderFooterReferences.Single(r => r.Kind == HeaderFooterKind.Footer && r.Type == HeaderFooterType.Default);
        var footer = FirstOf(document, reference.RelationshipId);
        Assert.Equal("center", (string?)footer.Properties.Get(W + "jc")!.Attribute(W + "val"));
        Assert.Single(document.HeadersFooters[reference.RelationshipId].Content.Blocks);
        Assert.Equal(["1", "2", "3"], Feet(document));
        LayoutAgrees(layout, document, result, "centred number");
        var drawn = DrawnPageNumbers.Read(Lay(document), document);
        Assert.Equal(ReferencePageNumbers.Numbers(document, [.. drawn.Select(d => d.Section)]), drawn.Select(d => d.Number));
    }

    [Fact]
    public void AnExistingNumberParagraphIsReplacedNotDuplicated()
    {
        var document = Document(
            P("p1") + Pg("p2") + Pg("p3") + Sect(Footer("default", "fN")),
            Parts(("fN", FooterPart(Para(Text("old ") + Field("PAGE", "1")) + Para(Text("keep"))))));
        var layout = Lay(document);
        var editor = Editor(document);

        var result = editor.InsertPageNumber(At(document, 0), PageNumberLook.Page, HeaderFooterKind.Footer, PageNumberAlign.Right, English, "1", "3");

        Assert.NotNull(result);
        Assert.Null(result.Sections);
        var blocks = document.HeadersFooters["fN"].Content.Blocks;
        Assert.Equal(2, blocks.Count);
        Assert.Equal(1, blocks.OfType<Paragraph>().Sum(p => Fields(p, "PAGE")));
        Assert.DoesNotContain("old", Raw((Paragraph)blocks[0]), StringComparison.Ordinal);
        Assert.Equal("keep", Raw((Paragraph)blocks[1]));
        Assert.Equal(["keep", "keep", "keep"], Feet(document));
        LayoutAgrees(layout, document, result, "replaced");
        Assert.Contains(Pages(Lay(document)).Select(page => string.Join("|", Lines(page))), text => text.Contains("Page 2", StringComparison.Ordinal));
    }

    [Fact]
    public void ASharedFooterEditsEverySectionThatSharesIt()
    {
        var document = Two();
        var editor = Editor(document);
        var at = At(document, 2);
        var references = Snapshot(document).Take(2).ToList();

        var result = editor.InsertPageNumber(at, PageNumberLook.Plain, HeaderFooterKind.Footer, PageNumberAlign.Center, English, "1", "4");

        Assert.NotNull(result);
        Assert.Null(result.Sections);
        var sharing = editor.PageNumberSharing(at, HeaderFooterKind.Footer);
        Assert.Equal(0, sharing.OwnerSection);
        Assert.Equal([0, 1], sharing.Sections);
        Assert.Single(document.HeadersFooters);
        Assert.Equal(references, Snapshot(document).Take(2).ToList());
        Assert.Equal(["1", "2", "3", "4"], Feet(document));
        Assert.Equal(result.Changes[0].Story, new StoryRef(StoryKind.HeaderFooter, "fA"));
    }

    [Fact]
    public void TheCaretInAFooterInsertsAtTheCaret()
    {
        var document = Document(P("p1") + Pg("p2") + Sect(Footer("default", "fA")), Parts(("fA", FooterPart(Para(Text("AB"))))));
        var editor = Editor(document);
        var paragraph = FirstOf(document, "fA");
        var at = new TextPosition(new StoryRef(StoryKind.HeaderFooter, "fA"), paragraph, 1);

        var result = editor.InsertPageNumber(at, PageNumberLook.Plain, HeaderFooterKind.Header, PageNumberAlign.Right, English, "7", "9");

        Assert.NotNull(result);
        Assert.Null(result.Sections);
        Assert.False(result.KeepSelection);
        Assert.Same(paragraph, result.Caret.Paragraph);
        Assert.Equal(new TextPosition(at.Story, paragraph, 2), result.Caret);
        Assert.Null(paragraph.Properties.Get(W + "jc"));
        Assert.Equal("A7B", Raw(paragraph));
        Assert.Equal(1, Fields(paragraph, "PAGE"));
        Assert.Equal(["A1B", "A2B"], Feet(document));
        Assert.Equal([0], editor.PageNumberSharing(at, HeaderFooterKind.Footer).Sections);
    }

    [Fact]
    public void PageOfTotalHoldsTwoFields()
    {
        var document = Three();
        var editor = Editor(document);

        var result = editor.InsertPageNumber(At(document, 0), PageNumberLook.PageOfTotal, HeaderFooterKind.Footer, PageNumberAlign.Left, English, "1", "3");

        Assert.NotNull(result);
        var id = SectionList.Of(document)[0].Properties.HeaderFooterReferences.Single(r => r.Kind == HeaderFooterKind.Footer).RelationshipId;
        var footer = FirstOf(document, id);
        Assert.Equal(1, Fields(footer, "PAGE"));
        Assert.Equal(1, Fields(footer, "NUMPAGES"));
        Assert.Equal(["Page 1 of 3", "Page 2 of 3", "Page 3 of 3"], Feet(document));
        Assert.True(PageNumberField.HoldsPage(footer));
    }

    [Fact]
    public void TheFieldIsOneUnitForTheCaret()
    {
        var document = Document(P("p1") + Sect(Footer("default", "fA")), Parts(("fA", FooterPart(Para(Text("ab"))))));
        var editor = Editor(document);
        var paragraph = FirstOf(document, "fA");
        var story = new StoryRef(StoryKind.HeaderFooter, "fA");
        var result = editor.InsertPageNumber(new TextPosition(story, paragraph, 2), PageNumberLook.Plain, HeaderFooterKind.Footer, PageNumberAlign.Left, English, "123", "1")!;
        Assert.Equal(5, result.Caret.Offset);

        var first = editor.Backspace(result.Caret, DeleteUnit.Cluster);

        Assert.NotNull(first);
        Assert.Empty(first.Changes);
        Assert.Equal(new TextRange(new TextPosition(story, paragraph, 2), new TextPosition(story, paragraph, 5)), first.Selected);
        Assert.Equal(1, Fields(paragraph, "PAGE"));
        editor.Delete(first.Selected!.Value);
        Assert.Equal(0, Fields(paragraph, "PAGE"));
        Assert.Equal("ab", Raw(paragraph));
    }

    [Fact]
    public void TheCachedResultIsTheCaretsPage()
    {
        var document = Three();
        var editor = Editor(document);

        editor.InsertPageNumber(At(document, 2), PageNumberLook.PageOfTotal, HeaderFooterKind.Footer, PageNumberAlign.Center, English, "3", "3");

        var id = SectionList.Of(document)[0].Properties.HeaderFooterReferences.Single(r => r.Kind == HeaderFooterKind.Footer).RelationshipId;
        Assert.Equal("Page 3 of 3", Raw(FirstOf(document, id)));
    }

    [Fact]
    public void OneUndoStep()
    {
        var document = Three();
        var editor = Editor(document);
        var start = Snapshot(document);
        var revision = document.Edits.Revision;

        Assert.NotNull(editor.InsertPageNumber(At(document, 0), PageNumberLook.Slash, HeaderFooterKind.Footer, PageNumberAlign.Center, English, "1", "3"));
        var after = Snapshot(document);
        Assert.Equal(1, editor.History.UndoCount);
        var steps = editor.History.UndoCount;

        Assert.NotNull(editor.History.Undo());
        Assert.False(editor.History.CanUndo);
        Assert.Equal(revision, document.Edits.Revision);
        Assert.Empty(SectionList.Of(document)[0].Properties.HeaderFooterReferences);
        Assert.Equal(["p1", "p2", "p3"], Feet(document));
        Assert.NotNull(editor.History.Redo());
        Assert.Equal(after, Snapshot(document));
        Assert.Equal(steps, editor.History.UndoCount);
        Assert.NotNull(editor.History.Undo());
        Assert.NotEqual(after, Snapshot(document));
        Assert.Equal(start.Take(1), Snapshot(document).Take(1));

        // A replace of a story paragraph is one step too.
        var twoSections = Two();
        var second = Editor(twoSections);
        var before = Snapshot(twoSections);
        Assert.NotNull(second.InsertPageNumber(At(twoSections, 2), PageNumberLook.Page, HeaderFooterKind.Footer, PageNumberAlign.Center, English, "3", "4"));
        var changed = Snapshot(twoSections);
        Assert.NotNull(second.History.Undo());
        Assert.Equal(before, Snapshot(twoSections));
        Assert.False(second.History.CanUndo);
        Assert.NotNull(second.History.Redo());
        Assert.Equal(changed, Snapshot(twoSections));
    }

    [Fact]
    public void NoNumberInAPrefixDocument()
    {
        var document = Three();
        var editor = Editor(document);

        // The editor cannot see that the file was opened as a prefix: the caller asks the facts and does not call the command (as for the section commands).
        Assert.Equal(SectionReason.PrefixDocument, editor.ReadSectionFacts(At(document, 0), prefixDocument: true).Reason);
        Assert.Equal(0, editor.History.UndoCount);
        Assert.Empty(document.HeadersFooters);
    }

    [Fact]
    public void ANoteAnEmptyPositionAndAStalePositionChangeNothing()
    {
        var document = Three();
        var editor = Editor(document);
        var before = Snapshot(document);
        var note = new TextPosition(new StoryRef(StoryKind.Note, "3"), (Paragraph)document.Body.Blocks[0], 0);

        Assert.Null(editor.InsertPageNumber(note, PageNumberLook.Plain, HeaderFooterKind.Footer, PageNumberAlign.Center, English, "1", "3"));
        Assert.Null(editor.InsertPageNumber(default, PageNumberLook.Plain, HeaderFooterKind.Footer, PageNumberAlign.Center, English, "1", "3"));
        var stale = new TextPosition(StoryRef.Body, new Paragraph(), 0);
        Assert.Throws<StalePositionException>(() => editor.InsertPageNumber(stale, PageNumberLook.Plain, HeaderFooterKind.Footer, PageNumberAlign.Center, English, "1", "3"));
        Assert.Equal(before, Snapshot(document));
        Assert.Equal(0, editor.History.UndoCount);
    }

    [Fact]
    public void AHeaderGetsItsNumberToo()
    {
        var document = Three();
        var editor = Editor(document);

        Assert.NotNull(editor.InsertPageNumber(At(document, 0), PageNumberLook.Plain, HeaderFooterKind.Header, PageNumberAlign.Right, English, "1", "3"));

        Assert.Equal(["1", "2", "3"], Pages(Lay(document)).Select(page => Lines(page)[0]));
        Assert.Contains(SectionList.Of(document)[0].Properties.HeaderFooterReferences, r => r.Kind == HeaderFooterKind.Header);
    }
}
