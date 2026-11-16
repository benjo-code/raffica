using System.Text;
using System.Xml.Linq;
using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Docx.Xml;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Layout;
using Raffica.Write.Tests.Support;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// The acceptance criterion of 4.3.2: every override row of the inspector removes exactly that override and nothing else. The oracle is the
/// serialization of the whole body by the DOCX writer, compared as XML with the "before" serialization minus the element: it never looks at the
/// model objects the code under test produced.
/// </summary>
public sealed class OverrideRemovalTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly string[] s_stopNames = ["txbxContent", "drawing", "pict", "object", "AlternateContent", "del", "moveFrom"];

    private static string Root => Path.Combine(TestData.Root, "testdata", "write-corpus");

    // Many different overrides over a paragraph with text, an empty paragraph with a marked mark, and the structural elements that have no row.
    private const string Body =
        "<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/><w:keepNext/><w:jc w:val=\"center\"/><w:ind w:left=\"720\" w:hanging=\"360\"/><w:spacing w:before=\"120\" w:after=\"240\" w:line=\"360\" w:lineRule=\"auto\"/>"
        + "<w:tabs><w:tab w:val=\"left\" w:pos=\"1440\"/></w:tabs><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr><w:rPr><w:b/></w:rPr></w:pPr>"
        + "<w:r><w:rPr><w:rStyle w:val=\"Strong\"/><w:b/><w:i/><w:color w:val=\"FF0000\"/><w:sz w:val=\"28\"/><w:rFonts w:ascii=\"Arial\" w:hAnsi=\"Arial\"/></w:rPr><w:t>first </w:t></w:r>"
        + "<w:r><w:rPr><w:b/><w:u w:val=\"single\"/><w:vanish w:val=\"0\"/><w:lang w:val=\"de-DE\"/><w:highlight w:val=\"yellow\"/></w:rPr><w:t>second</w:t></w:r></w:p>"
        + "<w:p><w:pPr><w:jc w:val=\"right\"/><w:keepNext/><w:rPr><w:b/><w:color w:val=\"00FF00\"/><w:i/></w:rPr></w:pPr></w:p>"
        + "<w:p><w:pPr><w:jc w:val=\"left\"/></w:pPr><w:r><w:rPr><w:b/><w:color w:val=\"0000FF\"/></w:rPr><w:t>third</w:t></w:r></w:p>";

    private static (WordDocument Document, TextEditor Editor) Setup(string body)
    {
        var part = LayoutModel.StylesPart(
            "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>"
            + "<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"TableNormal\"><w:tblPr/></w:style>");
        var document = LayoutModel.Document(body, part);
        return (document, new TextEditor(document, (_, _) => ProjectionHost.Plain, format: new LayoutEffectiveFormat(document)));
    }

    private static Paragraph P(WordDocument document, int index) => (Paragraph)document.Body.Blocks[index];

    private static TextRange Whole(WordDocument document, int first, int last) =>
        new(new TextPosition(StoryRef.Body, P(document, first), 0), new TextPosition(StoryRef.Body, P(document, last), ParagraphProjection.Build(P(document, last), ProjectionHost.Plain).Text.Length));

    // The whole body as the DOCX writer serializes it.
    private static XDocument Serialize(WordDocument document)
    {
        using var stream = new MemoryStream();
        DocumentXml.Write(document.Body, stream);
        return XDocument.Parse(Encoding.UTF8.GetString(stream.ToArray()));
    }

    // The paragraphs a model walk reaches: not those inside text boxes, drawings and other content the model keeps as it is (a content control around a
    // table cell, directly in a row, is such content: the model keeps it unknown, and no formatting reaches the paragraphs in it).
    private static List<XElement> BodyParagraphs(XDocument xml) =>
        [.. xml.Descendants(W + "p").Where(p => !p.Ancestors().Any(a => s_stopNames.Contains(a.Name.LocalName) || (a.Name == W + "sdt" && a.Parent?.Name == W + "tr")))];

    // The runs of a paragraph itself (not those of a paragraph nested elsewhere, not deleted ones).
    private static List<XElement> OwnRuns(XElement paragraph) =>
        [.. paragraph.Descendants(W + "r").Where(r => ReferenceEquals(r.Ancestors(W + "p").First(), paragraph) && !r.Ancestors().Any(a => a.Name.LocalName is "del" or "moveFrom"))];

    // "Before minus the element": the container that loses its last child stays (the writer writes an emptied property element again).
    private static void RemoveChild(XElement container, XName name) => container.Elements(name).Remove();

    private static XDocument Minus(XDocument before, OverrideScope scope, XName element, Func<int, bool> paragraphTakesPart)
    {
        var copy = new XDocument(before);
        var paragraphs = BodyParagraphs(copy);
        for (var i = 0; i < paragraphs.Count; i++)
        {
            var paragraph = paragraphs[i];
            if (!paragraphTakesPart(i))
            {
                continue;
            }

            var properties = paragraph.Element(W + "pPr");
            switch (scope)
            {
                case OverrideScope.Paragraph when properties is not null:
                    RemoveChild(properties, element);
                    break;
                case OverrideScope.ParagraphMark when !HasText(paragraph) && properties?.Element(W + "rPr") is { } mark:
                    RemoveChild(mark, element);
                    break;
                case OverrideScope.Run:
                    foreach (var run in OwnRuns(paragraph))
                    {
                        if (run.Element(W + "rPr") is { } props)
                        {
                            RemoveChild(props, element);
                        }
                    }

                    break;
            }
        }

        return copy;
    }

    private static string Text(XDocument xml) => xml.ToString(SaveOptions.DisableFormatting);

    private static void AssertSame(XDocument expected, XDocument actual, string what)
    {
        if (XNode.DeepEquals(expected, actual))
        {
            return;
        }

        var e = Text(expected);
        var a = Text(actual);
        var at = 0;
        while (at < e.Length && at < a.Length && e[at] == a[at])
        {
            at++;
        }

        var from = Math.Max(0, at - 80);
        Assert.Fail($"{what}: the document differs at {at}.\nexpected: ...{e[from..Math.Min(e.Length, at + 160)]}\nactual:   ...{a[from..Math.Min(a.Length, at + 160)]}");
    }

    private static bool HasText(XElement paragraph) => OwnRuns(paragraph).Any(r => r.Elements(W + "t").Any(t => t.Value.Length > 0));

    private static string Describe(OverrideRow row) => $"{row.Scope} {row.Element.LocalName}";

    [Fact]
    public void RemovingARowRemovesExactlyThatElement()
    {
        var reference = Setup(Body);
        var rows = FormattingInspector.Read(reference.Document, Whole(reference.Document, 0, 2)).Rows;
        Assert.True(rows.Count >= 15, "the test document must carry many rows, it carries " + rows.Count);
        Assert.Equal(3, rows.Select(r => r.Scope).Distinct().Count());

        foreach (var row in rows)
        {
            var (document, editor) = Setup(Body);
            var before = Serialize(document);
            var expected = Minus(before, row.Scope, row.Element, _ => true);

            var result = editor.RemoveOverride(Whole(document, 0, 2), row.Scope, row.Element);

            Assert.Single(result.Changes);
            AssertSame(expected, Serialize(document), Describe(row));
            Assert.False(XNode.DeepEquals(before, Serialize(document)), Describe(row) + " changed nothing");
            Assert.Equal(1, editor.History.UndoCount);
            editor.History.Undo();
            AssertSame(before, Serialize(document), "undo of " + Describe(row));
        }
    }

    [Fact]
    public void AnOverrideThatIsOnlyPartlyThereIsRemovedFromEveryRunThatHasIt()
    {
        var body = "<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>a</w:t></w:r><w:r><w:t>b</w:t></w:r><w:r><w:rPr><w:b/><w:i/></w:rPr><w:t>c</w:t></w:r></w:p>"
            + "<w:p><w:r><w:rPr><w:i/></w:rPr><w:t>d</w:t></w:r></w:p>";
        var (document, editor) = Setup(body);
        var before = Serialize(document);
        Assert.Equal(OverridePresence.Partly, editor.Inspect(Whole(document, 0, 1)).Rows.Single(r => r.Element == W + "b").Presence);

        editor.RemoveOverride(Whole(document, 0, 1), OverrideScope.Run, W + "b");

        AssertSame(Minus(before, OverrideScope.Run, W + "b", _ => true), Serialize(document), "partly");
        Assert.DoesNotContain(editor.Inspect(Whole(document, 0, 1)).Rows, r => r.Element == W + "b");
        Assert.Contains(editor.Inspect(Whole(document, 0, 1)).Rows, r => r.Element == W + "i");
    }

    [Fact]
    public void RemovingNeverMergesRuns()
    {
        var body = "<w:p><w:r><w:rPr><w:b/><w:i/></w:rPr><w:t>aa</w:t></w:r><w:r><w:rPr><w:i/></w:rPr><w:t>bb</w:t></w:r></w:p>";
        var (document, editor) = Setup(body);
        var before = Serialize(document);

        editor.RemoveOverride(Whole(document, 0, 0), OverrideScope.Run, W + "b");

        // Both runs now have the same properties and still are two runs.
        Assert.Equal(2, P(document, 0).Inlines.OfType<Run>().Count());
        AssertSame(Minus(before, OverrideScope.Run, W + "b", _ => true), Serialize(document), "no merge");
    }

    [Fact]
    public void ARunPartlyCoveredIsSplitOnlyWhereItCarriesTheElementAndTheSplitStays()
    {
        var body = "<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>hello world</w:t></w:r><w:r><w:t xml:space=\"preserve\"> plain</w:t></w:r></w:p>";
        var (document, editor) = Setup(body);
        var paragraph = P(document, 0);

        editor.RemoveOverride(new TextRange(new TextPosition(StoryRef.Body, paragraph, 3), new TextPosition(StoryRef.Body, paragraph, 8)), OverrideScope.Run, W + "b");

        var runs = P(document, 0).Inlines.OfType<Run>().ToList();
        Assert.Equal(["hel", "lo wo", "rld", " plain"], runs.Select(r => ((TextContent)r.Content[0]).Text));
        Assert.Equal([true, false, true, false], runs.Select(r => r.Properties.GetFlag(W + "b")));

        // The run that does not carry the element is not split by a selection edge inside it.
        var (second, editor2) = Setup("<w:p><w:r><w:rPr><w:b/></w:rPr><w:t>bold</w:t></w:r><w:r><w:t>plain text</w:t></w:r></w:p>");
        var p2 = P(second, 0);
        editor2.RemoveOverride(new TextRange(new TextPosition(StoryRef.Body, p2, 2), new TextPosition(StoryRef.Body, p2, 8)), OverrideScope.Run, W + "b");
        Assert.Equal(["bo", "ld", "plain text"], P(second, 0).Inlines.OfType<Run>().Select(r => ((TextContent)r.Content[0]).Text));
    }

    [Fact]
    public void NothingElseChangesNotTheStyleNorTheOtherRows()
    {
        var reference = Setup(Body);
        var all = FormattingInspector.Read(reference.Document, Whole(reference.Document, 0, 2));

        foreach (var row in all.Rows)
        {
            var (document, editor) = Setup(Body);
            editor.RemoveOverride(Whole(document, 0, 2), row.Scope, row.Element);
            var after = editor.Inspect(Whole(document, 0, 2));

            Assert.Equal(all.ParagraphStyleId, after.ParagraphStyleId);
            Assert.Equal(all.CharacterStyleId, after.CharacterStyleId);
            Assert.Equal(all.ParagraphStyleMixed, after.ParagraphStyleMixed);
            Assert.Equal(all.CharacterStyleMixed, after.CharacterStyleMixed);
            Assert.Equal(all.Rows.Where(r => !(r.Scope == row.Scope && r.Element == row.Element)), after.Rows);
            Assert.Equal("Heading1", P(document, 0).Properties.StyleId);
            Assert.NotNull(P(document, 0).Properties.Get(W + "numPr"));
        }
    }

    [Fact]
    public void RemovingARowThatIsNotThereOrIsStructuralChangesNothing()
    {
        var (document, editor) = Setup(Body);
        var before = Serialize(document);
        var revision = document.Edits.Revision;
        var firstOnly = Whole(document, 0, 0);

        Assert.Empty(editor.RemoveOverride(Whole(document, 0, 2), OverrideScope.Run, W + "caps").Changes);
        Assert.Empty(editor.RemoveOverride(Whole(document, 0, 2), OverrideScope.Paragraph, W + "pStyle").Changes);
        Assert.Empty(editor.RemoveOverride(Whole(document, 0, 2), OverrideScope.Paragraph, W + "numPr").Changes);
        Assert.Empty(editor.RemoveOverride(Whole(document, 0, 2), OverrideScope.Run, W + "rStyle").Changes);

        // The mark of a paragraph with text has no row, so there is nothing to remove.
        Assert.Empty(editor.RemoveOverride(firstOnly, OverrideScope.ParagraphMark, W + "b").Changes);
        Assert.Empty(editor.RemoveOverride(firstOnly, OverrideScope.Paragraph, W + "rPr").Changes);

        Assert.Equal(revision, document.Edits.Revision);
        Assert.Equal(0, editor.History.UndoCount);
        AssertSame(before, Serialize(document), "refusals");
    }

    [Fact]
    public void EveryOverrideOfEveryParagraphAndRunInTheCorpusRemovesOnlyItself()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var removals = 0;
        var kinds = new HashSet<string>();
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            var path = CorpusCheck.Resolve(Root, entry.Path);
            Assert.NotNull(path);
            if (!path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var file = DocxFile.Open(path);
            var document = file.Model;
            var editor = new TextEditor(document, (_, _) => ProjectionHost.Plain, format: new LayoutEffectiveFormat(document));
            var before = Serialize(document);
            var xmlParagraphs = BodyParagraphs(before);
            var modelParagraphs = ModelWalk.Blocks(document.Body.Blocks).OfType<Paragraph>().ToList();
            Assert.True(xmlParagraphs.Count == modelParagraphs.Count, $"{entry.Id}: {xmlParagraphs.Count} paragraphs in the XML, {modelParagraphs.Count} in the model");
            var count = modelParagraphs.Count;

            // An undo replaces the paragraph objects, so the selection is made again from the model for every use.
            TextRange RangeOf(int position)
            {
                var paragraph = ModelWalk.Blocks(document.Body.Blocks).OfType<Paragraph>().ElementAt(position);
                var length = ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text.Length;
                return new TextRange(new TextPosition(StoryRef.Body, paragraph, 0), new TextPosition(StoryRef.Body, paragraph, length));
            }

            for (var index = 0; index < count; index++)
            {
                var rows = editor.Inspect(RangeOf(index)).Rows;
                foreach (var row in rows)
                {
                    kinds.Add(entry.Id + row.Scope + row.Element);
                    var result = editor.RemoveOverride(RangeOf(index), row.Scope, row.Element);
                    Assert.Single(result.Changes);
                    AssertRemovedOnlyFromThisParagraph(before, Serialize(document), row, index, entry.Id);
                    editor.History.Undo();
                    Assert.True(XNode.DeepEquals(before, Serialize(document)), $"{entry.Id}: undo of {Describe(row)} at paragraph {index}");
                    removals++;
                }
            }
        }

        Assert.True(removals > 200 && kinds.Count >= 20, $"the corpus run removed only {removals} overrides of {kinds.Count} kinds");
        Console.WriteLine($"corpus removals: {removals} in {watch.Elapsed.TotalSeconds:F1} s");
    }

    // The oracle for the corpus: after = before minus a set of elements of this paragraph only; every run that shows text must have lost it
    // (a run that shows nothing, such as a field instruction, may keep it) and nothing outside the paragraph differs.
    private static void AssertRemovedOnlyFromThisParagraph(XDocument before, XDocument after, OverrideRow row, int paragraphIndex, string id)
    {
        var allGone = Minus(before, row.Scope, row.Element, i => i == paragraphIndex);
        var beforeParagraphs = BodyParagraphs(before);
        var afterParagraphs = BodyParagraphs(after);
        var goneParagraphs = BodyParagraphs(allGone);
        Assert.Equal(beforeParagraphs.Count, afterParagraphs.Count);
        for (var i = 0; i < beforeParagraphs.Count; i++)
        {
            if (i != paragraphIndex)
            {
                Assert.True(XNode.DeepEquals(beforeParagraphs[i], afterParagraphs[i]), $"{id}: paragraph {i} changed by a removal in paragraph {paragraphIndex}");
            }
        }

        // Outside the paragraphs nothing changes either: compare the documents with this paragraph blanked.
        var b = new XDocument(before);
        var a = new XDocument(after);
        BodyParagraphs(b)[paragraphIndex].RemoveNodes();
        BodyParagraphs(a)[paragraphIndex].RemoveNodes();
        AssertSame(b, a, $"{id}: outside paragraph {paragraphIndex}");

        var target = beforeParagraphs[paragraphIndex];
        var actual = afterParagraphs[paragraphIndex];
        var gone = goneParagraphs[paragraphIndex];
        Assert.Equal(target.Attributes().Select(x => x.ToString()), actual.Attributes().Select(x => x.ToString()));
        if (row.Scope != OverrideScope.Run)
        {
            Assert.True(XNode.DeepEquals(gone, actual), $"{id}: {Describe(row)} at {paragraphIndex}\nexpected {gone}\nactual   {actual}");
            return;
        }

        // A run row: the paragraph properties are untouched and the runs match one by one.
        Assert.True(XNode.DeepEquals(target.Element(W + "pPr"), actual.Element(W + "pPr")), $"{id}: pPr changed by a run row at {paragraphIndex}");
        var runsBefore = OwnRuns(target);
        var runsAfter = OwnRuns(actual);
        var runsGone = OwnRuns(gone);
        Assert.Equal(runsBefore.Count, runsAfter.Count);
        for (var i = 0; i < runsBefore.Count; i++)
        {
            var shows = runsBefore[i].Elements(W + "t").Any(t => t.Value.Length > 0);
            var hadIt = runsBefore[i].Element(W + "rPr")?.Element(row.Element) is not null;
            var kept = XNode.DeepEquals(runsBefore[i], runsAfter[i]);
            var removed = XNode.DeepEquals(runsGone[i], runsAfter[i]);
            Assert.True(kept || removed, $"{id}: run {i} of paragraph {paragraphIndex} is neither unchanged nor minus {Describe(row)}\nbefore {runsBefore[i]}\nafter  {runsAfter[i]}");
            Assert.True(!shows || !hadIt || removed, $"{id}: a run with text kept {Describe(row)} in paragraph {paragraphIndex}");
        }
    }

    [Fact]
    public void ClearRemovesAllRowsKeepsStylesAndList()
    {
        var (document, editor) = Setup(Body);
        var before = Serialize(document);

        var result = editor.ClearDirectFormatting(Whole(document, 0, 2));

        Assert.Single(result.Changes);
        Assert.Empty(editor.Inspect(Whole(document, 0, 2)).Rows);
        Assert.Equal("Heading1", P(document, 0).Properties.StyleId);
        Assert.Equal("Strong", P(document, 0).Inlines.OfType<Run>().First().Properties.StyleId);
        Assert.NotNull(P(document, 0).Properties.Get(W + "numPr"));

        // Whole-document oracle: the paragraphs keep pStyle and numPr, the runs keep rStyle, a covered mark loses its rPr, everything else goes.
        var expected = new XDocument(before);
        foreach (var paragraph in BodyParagraphs(expected))
        {
            var properties = paragraph.Element(W + "pPr");
            properties?.Elements().Where(e => e.Name != W + "pStyle" && e.Name != W + "numPr" && e.Name != W + "rPr").Remove();
            properties?.Element(W + "rPr")?.Elements().Remove();

            foreach (var props in OwnRuns(paragraph).Select(r => r.Element(W + "rPr")).OfType<XElement>())
            {
                props.Elements().Where(e => e.Name != W + "rStyle").Remove();
            }
        }

        AssertSame(expected, Serialize(document), "clear");
        Assert.Equal(1, editor.History.UndoCount);
        editor.History.Undo();
        AssertSame(before, Serialize(document), "undo of clear");
    }

    [Fact]
    public void ClearKeepsTheMarkOfAParagraphWithText()
    {
        var body = "<w:p><w:pPr><w:jc w:val=\"center\"/><w:rPr><w:b/></w:rPr><w:pPrChange w:id=\"1\" w:author=\"a\"><w:pPr/></w:pPrChange></w:pPr>"
            + "<w:r><w:rPr><w:i/><w:rPrChange w:id=\"2\" w:author=\"a\"><w:rPr/></w:rPrChange></w:rPr><w:t>hello world</w:t></w:r></w:p>";
        var (document, editor) = Setup(body);
        var paragraph = P(document, 0);

        // Part of the text: the mark stays.
        editor.ClearDirectFormatting(new TextRange(new TextPosition(StoryRef.Body, paragraph, 0), new TextPosition(StoryRef.Body, paragraph, 5)));

        var props = P(document, 0).Properties;
        Assert.Null(props.Get(W + "jc"));
        Assert.NotNull(props.Get(W + "rPr")?.Element(W + "b"));
        Assert.NotNull(props.Get(W + "pPrChange"));
        var runs = P(document, 0).Inlines.OfType<Run>().ToList();
        Assert.Equal(["hello", " world"], runs.Select(r => ((TextContent)r.Content[0]).Text));
        Assert.Equal([[W + "rPrChange"], [W + "i", W + "rPrChange"]], runs.Select(r => r.Properties.Children.Select(c => c.Name).ToArray()));
    }

    [Fact]
    public void ClearTakesTheMarkWhenTheSelectionCoversTheTextOrTheParagraphIsEmpty()
    {
        var body = "<w:p><w:pPr><w:rPr><w:b/></w:rPr></w:pPr><w:r><w:t>text</w:t></w:r></w:p>"
            + "<w:p><w:pPr><w:rPr><w:b/><w:ins w:id=\"3\" w:author=\"a\"/></w:rPr></w:pPr></w:p>";
        var (document, editor) = Setup(body);

        editor.ClearDirectFormatting(Whole(document, 0, 1));

        Assert.Empty(P(document, 0).Properties.Get(W + "rPr")!.Elements());
        var mark = P(document, 1).Properties.Get(W + "rPr");
        Assert.NotNull(mark);
        Assert.Equal(["ins"], mark.Elements().Select(e => e.Name.LocalName));
    }

    [Fact]
    public void RemoveAndClearAreOneUndoStep()
    {
        var (document, editor) = Setup(Body);
        var before = Serialize(document);

        editor.RemoveOverride(Whole(document, 0, 2), OverrideScope.Run, W + "b");
        Assert.Equal(1, editor.History.UndoCount);
        editor.ClearDirectFormatting(Whole(document, 0, 2));
        Assert.Equal(2, editor.History.UndoCount);

        editor.History.Undo();
        editor.History.Undo();
        AssertSame(before, Serialize(document), "two undos");
        Assert.Equal(0, editor.History.UndoCount);

        editor.History.Redo();
        editor.History.Redo();
        Assert.Empty(editor.Inspect(Whole(document, 0, 2)).Rows);
    }

    [Fact]
    public void ClearOnAnAlreadyCleanSelectionChangesNothing()
    {
        var (document, editor) = Setup("<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr><w:r><w:rPr><w:rStyle w:val=\"Strong\"/></w:rPr><w:t>x</w:t></w:r></w:p>");
        var revision = document.Edits.Revision;

        Assert.Empty(editor.ClearDirectFormatting(Whole(document, 0, 0)).Changes);
        Assert.Equal(revision, document.Edits.Revision);
        Assert.Equal(0, editor.History.UndoCount);
    }

    [Fact]
    public void RowsInsideATableAndAContentControlAreReadAndRemoved()
    {
        var cell = "<w:p><w:pPr><w:jc w:val=\"right\"/></w:pPr><w:r><w:rPr><w:b/></w:rPr><w:t>cell</w:t></w:r></w:p>";
        var body = "<w:p><w:r><w:t>before</w:t></w:r></w:p>"
            + "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"3000\"/></w:tblGrid><w:tr><w:tc><w:tcPr/>" + cell + "</w:tc></w:tr></w:tbl>"
            + "<w:sdt><w:sdtPr/><w:sdtContent><w:p><w:r><w:rPr><w:i/></w:rPr><w:t>control</w:t></w:r></w:p></w:sdtContent></w:sdt>"
            + "<w:p><w:r><w:t>after</w:t></w:r></w:p>";
        var (document, editor) = Setup(body);
        var range = new TextRange(new TextPosition(StoryRef.Body, P(document, 0), 0), new TextPosition(StoryRef.Body, (Paragraph)document.Body.Blocks[3], 5));
        var before = Serialize(document);

        var rows = editor.Inspect(range).Rows;
        Assert.Contains(rows, r => r.Scope == OverrideScope.Paragraph && r.Element == W + "jc" && r.Presence == OverridePresence.Partly);
        Assert.Contains(rows, r => r.Scope == OverrideScope.Run && r.Element == W + "b");
        Assert.Contains(rows, r => r.Scope == OverrideScope.Run && r.Element == W + "i");

        foreach (var row in rows)
        {
            editor.RemoveOverride(range, row.Scope, row.Element);
            AssertSame(Minus(before, row.Scope, row.Element, _ => true), Serialize(document), Describe(row));
            editor.History.Undo();

            // The undo replaced the paragraph objects: the same selection is made again.
            range = new TextRange(new TextPosition(StoryRef.Body, P(document, 0), 0), new TextPosition(StoryRef.Body, (Paragraph)document.Body.Blocks[3], 5));
        }
    }
}
