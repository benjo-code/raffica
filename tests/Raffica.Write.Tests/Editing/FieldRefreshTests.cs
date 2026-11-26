using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>The order-based field refresh (milestone 4.5b): SEQ, STYLEREF, REF and NOTEREF results written into the model from the model alone, never an edit and never a change to a field the support table does not cover.</summary>
public sealed class FieldRefreshTests
{
    private const string Section = "<w:sectPr><w:pgSz w:w=\"12240\" w:h=\"15840\"/></w:sectPr>";

    private static string Run(string text) => $"<w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    private static string Field(string instruction, string result) =>
        "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r>"
        + $"<w:r><w:instrText xml:space=\"preserve\"> {instruction} </w:instrText></w:r>"
        + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>"
        + Run(result)
        + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";

    private static string P(string inner, string? style = null, bool numbered = false) =>
        "<w:p><w:pPr>"
        + (style is null ? string.Empty : $"<w:pStyle w:val=\"{style}\"/>")
        + (numbered ? "<w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr>" : string.Empty)
        + $"</w:pPr>{inner}</w:p>";

    private static string Heading(string text) => P(Run(text), "Heading1", numbered: true);

    private static string Seq(string instruction, string stale = "99") => P(Run("Figure ") + Field(instruction, stale), "Caption");

    private static WordDocument Doc(params string[] paragraphs)
    {
        var styles = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style><w:style w:type=\"paragraph\" w:styleId=\"Caption\"><w:name w:val=\"caption\"/></w:style>";
        for (var i = 1; i <= 9; i++)
        {
            styles += $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{i}\"><w:name w:val=\"heading {i}\"/><w:pPr><w:outlineLvl w:val=\"{i - 1}\"/></w:pPr></w:style>";
        }

        var numbering = "<w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/><w:lvlText w:val=\"%1.\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>";
        return LayoutModel.Document(string.Concat(paragraphs) + Section, LayoutModel.StylesPart(styles), LayoutModel.NumberingPart(numbering));
    }

    private static TextEditor Editor(WordDocument document) => new(document, (_, _) => ProjectionHost.Plain);

    private static string? Stored(WordDocument document, int block, int ordinal = 0) => ReferenceFields.Stored((Paragraph)document.Body.Blocks[block], ordinal);

    private static string Bytes(WordDocument document) => ModelSnapshot.Of(document).ToString(SaveOptions.DisableFormatting);

    private static FieldRefreshResult Refresh(WordDocument document, FakeFieldHost? host = null) =>
        Editor(document).RefreshFields(host ?? new FakeFieldHost(document), RefreshScope.OrderBased);

    [Fact]
    public void SeqCountsPerLabelInDocumentOrder()
    {
        var document = Doc(Seq("SEQ Figure"), Seq("SEQ Table"), Seq("SEQ Figure"), Seq("SEQ figure"));

        var result = Refresh(document);

        Assert.Equal(["1", "1", "2", "3"], new[] { Stored(document, 0), Stored(document, 1), Stored(document, 2), Stored(document, 3) });
        Assert.Equal(4, result.Written);
        Assert.Equal(0, result.NotUpdated);
        Assert.True(result.Settled);
        Assert.Null(result.Structural);
        Assert.Equal(4, result.Content.Count);
    }

    [Fact]
    public void SeqRestartsAfterAHeadingWithS()
    {
        var document = Doc(Heading("A"), Seq("SEQ Figure \\s 1"), Seq("SEQ Figure \\s 1"), Heading("B"), Seq("SEQ Figure \\s 1"), Seq("SEQ Figure"));

        Refresh(document);

        Assert.Equal(["1", "2", "1", "2"], new[] { Stored(document, 1), Stored(document, 2), Stored(document, 4), Stored(document, 5) });
    }

    [Fact]
    public void WithRSetsTheNumber()
    {
        var document = Doc(Seq("SEQ Figure"), Seq("SEQ Figure \\r 10"), Seq("SEQ Figure"));

        Refresh(document);

        Assert.Equal(["1", "10", "11"], new[] { Stored(document, 0), Stored(document, 1), Stored(document, 2) });
    }

    [Fact]
    public void SeqFormatsRomanAndLetters()
    {
        var document = Doc(
            Seq("SEQ A \\* roman"),
            Seq("SEQ A \\* roman"),
            Seq("SEQ A \\* roman"),
            Seq("SEQ A \\* roman"),
            Seq("SEQ B \\r 4 \\* ROMAN"),
            Seq("SEQ C \\r 27 \\* ALPHABETIC"),
            Seq("SEQ D \\r 3 \\* alphabetic"),
            Seq("SEQ E \\r 7 \\* ARABIC \\* MERGEFORMAT"));

        var result = Refresh(document);

        Assert.Equal(["i", "ii", "iii", "iv", "IV", "AA", "c", "7"], Enumerable.Range(0, 8).Select(i => Stored(document, i)));
        Assert.Equal(0, result.NotUpdated);
    }

    [Fact]
    public void CChangesNothing()
    {
        var document = Doc(Seq("SEQ Figure"), Seq("SEQ Figure \\c"), Seq("SEQ Figure"));

        Refresh(document);

        Assert.Equal(["1", "1", "2"], new[] { Stored(document, 0), Stored(document, 1), Stored(document, 2) });
    }

    [Fact]
    public void StyleRefGivesTheChapterLabel()
    {
        var document = Doc(
            P(Run("before ") + Field("STYLEREF 1 \\s", "x")),
            Heading("A"),
            P(Run("one ") + Field("STYLEREF 1 \\s", "x")),
            Heading("B"),
            P(Run("two ") + Field("STYLEREF 1 \\s", "x")));

        var result = Refresh(document);

        // Nothing before the first heading: the stored result stays and the field is counted.
        Assert.Equal(["x", "1", "2"], new[] { Stored(document, 0), Stored(document, 2), Stored(document, 4) });
        Assert.Equal(1, result.NotUpdated);
    }

    [Fact]
    public void RefTextAndRefNumberOfAHeading()
    {
        var document = Doc(
            P("<w:bookmarkStart w:id=\"7\" w:name=\"Intro\"/>" + Run("Introduction") + "<w:bookmarkEnd w:id=\"7\"/>", "Heading1", numbered: true),
            P(Run("see ") + Field("REF Intro \\h", "old") + Run(" and ") + Field("REF intro \\n", "old") + Run(" and ") + Field("REF Intro \\r \\h", "old")));

        var result = Refresh(document);

        Assert.Equal(["Introduction", "1", "1"], new[] { Stored(document, 1, 0), Stored(document, 1, 1), Stored(document, 1, 2) });
        Assert.Equal(3, result.Written);
    }

    [Fact]
    public void ARefToACaptionTakesTheNewSequenceNumber()
    {
        // The reference comes before the caption: it still shows the number the caption gets in the same refresh.
        var document = Doc(
            P(Run("see ") + Field("REF _Ref1 \\h", "Figure 9")),
            P("<w:bookmarkStart w:id=\"1\" w:name=\"_Ref1\"/>" + Run("Figure ") + Field("SEQ Figure", "9") + "<w:bookmarkEnd w:id=\"1\"/>", "Caption"));

        Refresh(document);

        Assert.Equal("1", Stored(document, 1));
        Assert.Equal("Figure 1", Stored(document, 0));
    }

    [Fact]
    public void NoterefGivesTheMark()
    {
        var document = Doc(
            P(Run("a") + "<w:r><w:footnoteReference w:id=\"4\"/></w:r>"),
            P(Run("b") + "<w:bookmarkStart w:id=\"2\" w:name=\"Note\"/><w:r><w:footnoteReference w:id=\"5\"/></w:r><w:bookmarkEnd w:id=\"2\"/>"),
            P(Run("see ") + Field("NOTEREF Note \\h", "old")));

        var result = Refresh(document);

        Assert.Equal("2", Stored(document, 2));
        Assert.Equal(0, result.NotUpdated);
    }

    [Fact]
    public void AMissingBookmarkIsNotUpdatedAndKeepsItsBytes()
    {
        var document = Doc(P(Run("see ") + Field("REF Nowhere \\h", "kept")), P(Run("note ") + Field("NOTEREF Nowhere", "kept")));
        var before = Bytes(document);
        var revision = document.Edits.Revision;
        var version = document.Edits.DerivedVersion;

        var result = Refresh(document);

        Assert.Equal(2, result.NotUpdated);
        Assert.Equal(0, result.Written);
        Assert.Empty(result.Content);
        Assert.Equal(before, Bytes(document));
        Assert.Equal(revision, document.Edits.Revision);
        Assert.Equal(version, document.Edits.DerivedVersion);
    }

    [Fact]
    public void UnsupportedFieldsKeepTheirBytes()
    {
        var document = Doc(
            P(Run("a ") + Field("DATE \\@ \"d\"", "today")),
            P(Run("b ") + Field("SEQ Figure \\h", "5")),
            P(Run("c ") + Field("STYLEREF 2 \\s", "6")),
            P(Run("d ") + Field("REF Mark \\w", "7")));
        var before = Bytes(document);

        var result = Refresh(document);

        Assert.Equal(4, result.NotUpdated);
        Assert.Equal(before, Bytes(document));
    }

    [Fact]
    public void ARefreshChangesNoRevisionAndNoDirtyFlag()
    {
        var document = Doc(Seq("SEQ Figure"), Seq("SEQ Figure"));
        var revision = document.Edits.Revision;
        var version = document.Edits.DerivedVersion;

        var result = Refresh(document);

        Assert.True(result.Written > 0);
        Assert.Equal(revision, document.Edits.Revision);
        Assert.False(document.Edits.IsDirty);
        Assert.Equal(version + 1, document.Edits.DerivedVersion);
    }

    [Fact]
    public void ARefreshThatChangesNothingRaisesNothing()
    {
        var document = Doc(Seq("SEQ Figure"), Seq("SEQ Figure"));
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        editor.RefreshFields(host, RefreshScope.OrderBased);
        var version = document.Edits.DerivedVersion;
        var before = Bytes(document);

        var again = editor.RefreshFields(host, RefreshScope.OrderBased);

        Assert.Empty(again.Content);
        Assert.Equal(0, again.Written);
        Assert.Equal(version, document.Edits.DerivedVersion);
        Assert.Equal(before, Bytes(document));
    }

    [Fact]
    public void FieldsInNotesHeadersAndTrackedChangesAreSkipped()
    {
        var tracked = P("<w:ins w:id=\"1\" w:author=\"a\" w:date=\"2026-01-01T00:00:00Z\">" + Run("Figure ") + Field("SEQ Figure", "55") + "</w:ins>");
        var document = Doc(Seq("SEQ Figure"), tracked, Seq("SEQ Figure"));

        var notes = NotesParts.Ensure(document, NoteType.Footnote);
        var inNote = (Paragraph)LayoutModel.Document(P(Run("n ") + Field("SEQ Figure", "66"))).Body.Blocks[0];
        notes.Notes[0].Content.Blocks.Add(inNote);
        var header = new Story(new OoxmlRoot(XName.Get("hdr", LayoutModel.W), []));
        var inHeader = (Paragraph)LayoutModel.Document(P(Run("h ") + Field("SEQ Figure", "77"))).Body.Blocks[0];
        header.Blocks.Add(inHeader);
        document.HeadersFooters["h1"] = new HeaderFooterPart(header);

        var result = Refresh(document);

        // The field in the tracked insertion does not count in the sequence: the two captions are 1 and 2.
        Assert.Equal(["1", "2"], new[] { Stored(document, 0), Stored(document, 2) });
        Assert.Equal("55", Stored(document, 1));
        Assert.Equal("66", ReferenceFields.Stored(inNote, 0));
        Assert.Equal("77", ReferenceFields.Stored(inHeader, 0));
        Assert.Equal(3, result.NotUpdated);
    }

    [Fact]
    public void AFieldInAnInlineContentControlIsRefreshed()
    {
        var document = Doc(P("<w:sdt><w:sdtPr/><w:sdtContent>" + Run("Figure ") + Field("SEQ Figure", "9") + "</w:sdtContent></w:sdt>"));

        var result = Refresh(document);

        Assert.Equal(1, result.Written);
        Assert.Equal("1", Stored(document, 0));
    }

    [Fact]
    public void ATouchedRefreshWritesOnlyWhereItWasAskedUnlessABookmarkChanged()
    {
        var document = Doc(P(Run("x ") + Field("REF Mark", "old")), P(Run("y ") + Field("REF Mark", "old")), P("<w:bookmarkStart w:id=\"1\" w:name=\"Mark\"/>" + Run("text") + "<w:bookmarkEnd w:id=\"1\"/>"));
        var editor = Editor(document);
        var host = new FakeFieldHost(document);
        var touched = new[] { new BlockChange(StoryRef.Body, document.Body.Blocks, 1, 1, 1, [document.Body.Blocks[1]]) };

        var result = editor.RefreshFields(host, RefreshScope.OrderBased, touched);

        Assert.Equal("old", Stored(document, 0));
        Assert.Equal("text", Stored(document, 1));
        Assert.Equal(1, result.Written);

        // A touched block that holds a bookmark makes every reference of the story a target.
        var affecting = new[] { new BlockChange(StoryRef.Body, document.Body.Blocks, 2, 1, 1, [document.Body.Blocks[2]]) };
        editor.RefreshFields(host, RefreshScope.OrderBased, affecting);
        Assert.Equal("text", Stored(document, 0));
    }

    [Fact]
    public void NoMessageHoldsDocumentText()
    {
        var document = Doc(P(Run("see ") + Field("REF SecretName \\h", "SecretStale")));
        var editor = Editor(document);

        var result = editor.RefreshFields(new FakeFieldHost(document), RefreshScope.OrderBased);
        var text = result.ToString();
        var error = Assert.Throws<ArgumentNullException>(() => editor.RefreshFields(null!, RefreshScope.OrderBased));

        Assert.DoesNotContain("SecretName", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretStale", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretName", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretStale", error.Message, StringComparison.Ordinal);
    }
}
