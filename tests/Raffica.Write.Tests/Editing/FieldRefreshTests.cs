using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
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

    // Typing right after a field must not put the text into the run of the field's end mark: a run that holds the end mark and other content cannot take a new result (FieldWriter), so the field would never be updated again.
    [Theory]
    [InlineData("x")]
    [InlineData("x\nrest")]
    [InlineData("\nrest")]
    public void TextTypedRightAfterAFieldLeavesTheFieldUpdatable(string typed)
    {
        var bold = "<w:r><w:rPr><w:b/></w:rPr><w:fldChar w:fldCharType=\"end\"/></w:r>";
        var field = Field("SEQ Figure", "7").Replace("<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>", bold, StringComparison.Ordinal);
        var document = Doc(P(Run("Figure ") + field, "Caption"));
        var editor = Editor(document);
        var caption = (Paragraph)document.Body.Blocks[0];
        var at = new TextPosition(StoryRef.Body, caption, "Figure 7".Length);

        var result = editor.InsertText(new TextRange(at, at), typed);

        Assert.True(FieldWriter.CanWrite(FieldView.InParagraph(caption)[0]));
        if (typed[0] == 'x')
        {
            // The typed text takes the formatting of the run it follows.
            var inserted = caption.Inlines.OfType<Run>().Single(r => r.Content.OfType<TextContent>().Any(t => t.Text == "x"));
            Assert.DoesNotContain(inserted.Content, c => c is FieldChar);
            Assert.Contains(inserted.Properties.Children, c => c.Name.LocalName == "b");
        }

        // The second keystroke goes on in the same run.
        var after = editor.InsertText(new TextRange(result.Caret, result.Caret), "y");
        Assert.True(FieldWriter.CanWrite(FieldView.InParagraph(caption)[0]));
        Assert.NotNull(after);

        var refreshed = editor.RefreshFields(new FakeFieldHost(document), RefreshScope.OrderBased);
        Assert.Equal("1", Stored(document, 0));
        Assert.Equal(0, refreshed.NotUpdated);
    }

    // Word calls a reference to a bookmark that holds the reference itself invalid ("Error! Not a valid bookmark self-reference.", checked with Word 16.0 on 2026-11-27); Write never shows an error text, so the field keeps its stored result and is counted. Without this the result grows with every refresh and never settles.
    [Theory]
    [InlineData("REF Self \\h")]
    [InlineData("REF Self")]
    public void AReferenceToABookmarkThatHoldsItselfKeepsItsResult(string instruction)
    {
        var document = Doc(P("<w:bookmarkStart w:id=\"1\" w:name=\"Self\"/>" + Run("Chapter ") + Field(instruction, "old") + Run(" end") + "<w:bookmarkEnd w:id=\"1\"/>"));
        var editor = Editor(document);

        var first = editor.RefreshFields(new FakeFieldHost(document), RefreshScope.OrderBased);

        Assert.Equal("old", Stored(document, 0));
        Assert.Equal(0, first.Written);
        Assert.Equal(1, first.NotUpdated);
        Assert.Empty(editor.RefreshFields(new FakeFieldHost(document), RefreshScope.OrderBased).Content);
    }

    // A reference inside a bookmark that does not reach back to itself (the number of the paragraph, or a bookmark elsewhere) is still written.
    [Fact]
    public void AReferenceThatOnlyStandsInsideAnotherBookmarkIsStillWritten()
    {
        var document = Doc(
            P("<w:bookmarkStart w:id=\"1\" w:name=\"Other\"/>" + Run("text") + "<w:bookmarkEnd w:id=\"1\"/>"),
            P("<w:bookmarkStart w:id=\"2\" w:name=\"Around\"/>" + Field("REF Other \\h", "old") + "<w:bookmarkEnd w:id=\"2\"/>"));

        Assert.Equal(1, Refresh(document).Written);
        Assert.Equal("text", Stored(document, 1));
    }

    // A field with an empty result has no text a delete could take: one that lies between the deleted characters goes with them (as in Word), so a bookmark whose whole range is deleted goes too.
    [Fact]
    public void DeletingTheTextAroundAFieldWithAnEmptyResultTakesTheFieldAndTheBookmark()
    {
        var empty = "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> REF Missing </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";
        var document = Doc(
            P("<w:bookmarkStart w:id=\"1\" w:name=\"Mark\"/>" + Run("ab") + empty + Run("cd") + "<w:bookmarkEnd w:id=\"1\"/>"),
            P("<w:bookmarkStart w:id=\"2\" w:name=\"Kept\"/>" + Run("ab") + empty + Run("cd") + "<w:bookmarkEnd w:id=\"2\"/>"));
        var editor = Editor(document);
        var first = (Paragraph)document.Body.Blocks[0];
        var second = (Paragraph)document.Body.Blocks[1];

        editor.Delete(new TextRange(new TextPosition(StoryRef.Body, first, 0), new TextPosition(StoryRef.Body, first, 4)));
        editor.Delete(new TextRange(new TextPosition(StoryRef.Body, second, 0), new TextPosition(StoryRef.Body, second, 2)));

        Assert.Empty(FieldView.InParagraph(first));
        Assert.DoesNotContain("Mark", BookmarkOperations.AllNames(document));
        Assert.Single(FieldView.InParagraph(second));
        Assert.Contains("Kept", BookmarkOperations.AllNames(document));
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

    // The rules below are the ones Word 16.0 follows (checked with tools/word-field-results.ps1 on 2026-11-27, Docs/write-word-check.md, "Fields, captions and tables of contents (milestone 4.5b)").
    [Fact]
    public void ARestartAppliesOnlyWhenAHeadingStandsBetweenTheFieldAndTheLastOneOfItsLabel()
    {
        var document = Doc(Seq("SEQ E"), Heading("A"), Seq("SEQ E"), Seq("SEQ E \\s 1"), Seq("SEQ F"), Heading("B"), Seq("SEQ F \\r 3"), Seq("SEQ F \\s 1"));

        Refresh(document);

        Assert.Equal(["1", "2", "3", "1", "3", "4"], new[] { Stored(document, 0), Stored(document, 2), Stored(document, 3), Stored(document, 4), Stored(document, 6), Stored(document, 7) });
    }

    [Fact]
    public void AHeadingOfAHigherLevelRestartsAndALowerOneDoesNot()
    {
        var document = Doc(
            Seq("SEQ B \\s 2"), Heading("A"), Seq("SEQ B \\s 2"),
            P(Run("sub"), "Heading3"), Seq("SEQ B \\s 2"),
            P(Run("sub"), "Heading2"), Seq("SEQ B \\s 2"));

        Refresh(document);

        Assert.Equal(["1", "1", "2", "1"], new[] { Stored(document, 0), Stored(document, 2), Stored(document, 4), Stored(document, 6) });
    }

    [Fact]
    public void CAtTheStartOfAChapterShowsZeroAndDoesNotCountAndNWinsOverC()
    {
        var document = Doc(Heading("A"), Seq("SEQ I \\c"), Seq("SEQ I"), Seq("SEQ I \\n \\c"), Heading("B"), Seq("SEQ I \\c \\s 1"), Seq("SEQ I \\s 1"));

        Refresh(document);

        Assert.Equal(["0", "1", "2", "0", "1"], new[] { Stored(document, 1), Stored(document, 2), Stored(document, 3), Stored(document, 5), Stored(document, 6) });
    }

    [Fact]
    public void SequenceFieldsWriteDoesNotUpdateStillCount()
    {
        var document = Doc(
            Seq("SEQ N"), Seq("SEQ N \\h", "kept"), Seq("SEQ N"), Seq("SEQ N \\* CardText", "kept"), Seq("SEQ N \\# \"00\"", "kept"), Seq("SEQ N"),
            Seq("SEQ N \\r 7 \\* Ordinal", "kept"), Seq("SEQ N"));

        var result = Refresh(document);

        Assert.Equal(["1", "kept", "3", "kept", "kept", "6", "kept", "8"], Enumerable.Range(0, 8).Select(i => Stored(document, i)));
        Assert.Equal(4, result.NotUpdated);
    }

    [Fact]
    public void ASequenceFieldThatNamesABookmarkOrHasAnUnknownSwitchDoesNotCount()
    {
        var document = Doc(Seq("SEQ T"), Seq("SEQ T Elsewhere", "kept"), Seq("SEQ T \\q", "kept"), Seq("SEQ T"));

        Refresh(document);

        Assert.Equal(["1", "kept", "kept", "2"], Enumerable.Range(0, 4).Select(i => Stored(document, i)));
    }

    [Fact]
    public void ARestartValueThatIsNoNumberSetsZeroAndAnUnreadableLevelRestartsNothing()
    {
        var document = Doc(Seq("SEQ X"), Seq("SEQ X \\r abc"), Seq("SEQ X"), Heading("A"), Seq("SEQ X \\s 0"), Seq("SEQ X \\s abc"), Seq("SEQ X"));

        Refresh(document);

        Assert.Equal(["1", "0", "1", "2", "3", "4"], new[] { Stored(document, 0), Stored(document, 1), Stored(document, 2), Stored(document, 4), Stored(document, 5), Stored(document, 6) });
    }

    [Fact]
    public void InsertedAndMovedInSequenceFieldsCountAndDeletedOnesDoNot()
    {
        const string Change = " w:author=\"a\" w:date=\"2026-01-01T00:00:00Z\"";
        string Tracked(string kind, int id, string instruction) => P($"<w:{kind} w:id=\"{id}\"{Change}>{Field(instruction, "old")}</w:{kind}>");
        var deleted = Field("SEQ P", "old").Replace("instrText", "delInstrText", StringComparison.Ordinal).Replace("<w:t ", "<w:delText ", StringComparison.Ordinal).Replace("</w:t>", "</w:delText>", StringComparison.Ordinal);
        var document = Doc(
            Seq("SEQ P"),
            Tracked("ins", 1, "SEQ P"),
            Tracked("moveTo", 2, "SEQ P"),
            P($"<w:del w:id=\"3\"{Change}>{deleted}</w:del>"),
            Tracked("moveFrom", 4, "SEQ P"),
            Seq("SEQ P"));

        Refresh(document);

        // The fields inside the tracked changes keep their stored results; the ones after them are counted with the inserted and the moved-in field.
        Assert.Equal(["1", "old", "old", "4"], new[] { Stored(document, 0), Stored(document, 1), Stored(document, 2), Stored(document, 5) });
    }

    [Fact]
    public void ASequenceFieldNestedInAnotherFieldCounts()
    {
        var link = "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> HYPERLINK \\l \"x\" </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" + Field("SEQ Y", "old") + "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";
        var document = Doc(Seq("SEQ Y"), P(link), Seq("SEQ Y"));

        Refresh(document);

        Assert.Equal(["1", "old", "3"], new[] { Stored(document, 0), Stored(document, 1, 1), Stored(document, 2) });
    }

    // STYLEREF 1 \s is the number of the chapter alone: the text of the list level before its first and after its last number is left out, the text between two numbers stays.
    [Theory]
    [InlineData("Chapter %1:", "upperRoman", 0, "I")]
    [InlineData("(%1)", "decimal", 0, "1")]
    [InlineData("Part 2.%1.", "lowerLetter", 0, "a")]
    [InlineData("%1.0", "decimalZero", 0, "01")]
    [InlineData("Sec %1 - %2)", "decimal", 1, "1 - 1")]
    [InlineData("%1 part %2 of it", "decimal", 1, "1 part 1")]
    public void StyleRefGivesTheChapterNumberWithoutTheTextOfItsLevel(string text, string format, int level, string expected)
    {
        var styles = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style><w:style w:type=\"paragraph\" w:styleId=\"Heading1\"><w:name w:val=\"heading 1\"/></w:style>";
        var first = level == 0 ? text : "%1";
        var numbering = $"<w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"{(level == 0 ? format : "decimal")}\"/><w:lvlText w:val=\"{first}\"/></w:lvl>"
            + $"<w:lvl w:ilvl=\"1\"><w:start w:val=\"1\"/><w:numFmt w:val=\"{format}\"/><w:lvlText w:val=\"{(level == 1 ? text : "%1.%2")}\"/></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num>";
        var heading = $"<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/><w:numPr><w:ilvl w:val=\"{level}\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr>{Run("A")}</w:p>";
        var opener = level == 0 ? string.Empty : $"<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr></w:pPr>{Run("first")}</w:p>";
        var document = LayoutModel.Document(opener + heading + P(Field("STYLEREF 1 \\s", "x")) + Section, LayoutModel.StylesPart(styles), LayoutModel.NumberingPart(numbering));
        var last = document.Body.Blocks.Count - 1;

        Refresh(document);

        Assert.Equal(expected, Stored(document, last));
        Assert.Equal(expected, new FieldLabels(document).ChapterNumber((Paragraph)document.Body.Blocks[last - 1]));
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

        // The field in the tracked insertion keeps its result and counts in the sequence, as in Word: the two captions are 1 and 3.
        Assert.Equal(["1", "3"], new[] { Stored(document, 0), Stored(document, 2) });
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
    public void ANestedContainerChangeMakesEveryFieldATarget()
    {
        // The edit was in a table cell or a content control: its change names that nested list, not the body, so the whole body is refreshed.
        var document = Doc(Seq("SEQ Figure"), Seq("SEQ Figure"), Seq("SEQ Figure"));
        var nested = new BlockChange(StoryRef.Body, new List<Block>(), 0, 1, 1, [document.Body.Blocks[1]]);

        var result = Editor(document).RefreshFields(new FakeFieldHost(document), RefreshScope.OrderBased, [nested]);

        Assert.Equal(["1", "2", "3"], new[] { Stored(document, 0), Stored(document, 1), Stored(document, 2) });
        Assert.Equal(3, result.Written);
    }

    [Fact]
    public void AChangeOfAnotherStoryTouchesNothingInTheBody()
    {
        var document = Doc(Seq("SEQ Figure"));
        var other = new BlockChange(new StoryRef(StoryKind.HeaderFooter, "h1"), new List<Block>(), 0, 1, 1, [document.Body.Blocks[0]]);

        var result = Editor(document).RefreshFields(new FakeFieldHost(document), RefreshScope.OrderBased, [other]);

        Assert.Equal(0, result.Written);
        Assert.Equal("99", Stored(document, 0));
    }

    [Fact]
    public void ANumberedParagraphInATouchedBlockMakesEveryReferenceATarget()
    {
        var item = P(Run("a"), numbered: true);
        var document = Doc(
            item,
            P("<w:bookmarkStart w:id=\"1\" w:name=\"Mark\"/>" + Run("b") + "<w:bookmarkEnd w:id=\"1\"/>", numbered: true),
            P(Run("see ") + Field("REF Mark \\n", "old")));
        // The change names the first list item only (a bookmark and a heading are not in it), yet the label of the second item depends on it.
        var touched = new[] { new BlockChange(StoryRef.Body, document.Body.Blocks, 0, 1, 1, [document.Body.Blocks[0]]) };

        var result = Editor(document).RefreshFields(new FakeFieldHost(document), RefreshScope.OrderBased, touched);

        Assert.Equal("2", Stored(document, 2));
        Assert.Equal(1, result.Written);
    }

    [Fact]
    public void ARemovedNumberedParagraphIsFoundInTheOldBlocks()
    {
        var document = Doc(
            P("<w:bookmarkStart w:id=\"1\" w:name=\"Mark\"/>" + Run("b") + "<w:bookmarkEnd w:id=\"1\"/>", numbered: true),
            P(Run("see ") + Field("REF Mark \\n", "old")),
            P(Run("tail")));
        var removed = (Paragraph)LayoutModel.Document(P(Run("gone"), numbered: true)).Body.Blocks[0];
        var touched = new[] { new BlockChange(StoryRef.Body, document.Body.Blocks, 2, 0, 0, [removed]) };

        Editor(document).RefreshFields(new FakeFieldHost(document), RefreshScope.OrderBased, touched);

        Assert.Equal("1", Stored(document, 1));
    }

    [Fact]
    public void AResultThatCannotBeWrittenIsCountedAsNotUpdated()
    {
        // A complex field with no separate character has no result position: Write cannot put a result there.
        var noResult = "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> SEQ Figure </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";
        var document = Doc(P(Run("Figure ") + noResult));
        var before = Bytes(document);

        var result = Refresh(document);

        Assert.Equal(1, result.NotUpdated);
        Assert.Equal(0, result.Written);
        Assert.Equal(before, Bytes(document));
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
