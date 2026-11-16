using System.Globalization;
using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using WriteCorpus;
using Xunit;
using static Raffica.Write.Tests.Layout.ChromeTestKit;

namespace Raffica.Write.Tests.Layout;

/// <summary>
/// The gate of the layout half of milestone 4.3a: over the whole compatibility corpus, in both views, a layout that was brought up to date with
/// <see cref="DocumentLayout.ApplyEdit"/> equals a layout made from scratch after every one of a series of seeded edits; plus the rules of the
/// fallbacks (what restarts, what must not).
/// </summary>
public sealed class CorpusIncrementalTests
{
    private const int EditsPerFile = 25;

    private static string Root => Path.Combine(TestData.Root, "testdata", "write-corpus");

    private static string Corpus(string id) => Path.Combine(TestData.Root, "testdata", "write-corpus", id, id + ".docx");

    public static TheoryData<string, LayoutMode> Cases()
    {
        var data = new TheoryData<string, LayoutMode>();
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            data.Add(entry.Id, LayoutMode.Pages);
            data.Add(entry.Id, LayoutMode.Flow);
        }

        return data;
    }

    private static readonly string s_long = string.Concat(Enumerable.Repeat("a longer insert that wraps ", 14));

    private static async Task<DocumentLayout> LaidAsync(WordDocument model, LayoutMode mode)
    {
        var layout = DocumentLayout.Create(model, PageTestKit.Fonts, new LayoutOptions(mode));
        await layout.LayOutRestAsync(null, CancellationToken.None);
        Assert.True(layout.IsComplete);
        return layout;
    }

    private static string FirstText(Paragraph paragraph) => TestEdit.TextOf(paragraph);

    /// <summary>Appends text to the paragraph of a story that is not the body (a header, a footer, a note), in place.</summary>
    private static BlockChange EditInStory(StoryRef story, IList<Block> container, Paragraph paragraph, string text)
    {
        var before = paragraph.Clone();
        var content = paragraph.Inlines.OfType<Run>().SelectMany(r => r.Content.OfType<TextContent>()).FirstOrDefault();
        if (content is not null)
        {
            content.Text += text;
        }
        else
        {
            var run = new Run();
            run.Content.Add(new TextContent(text));
            paragraph.Inlines.Add(run);
        }

        return new BlockChange(story, container, container.IndexOf(paragraph), 1, 1, [before]);
    }

    private static List<(StoryRef Story, IList<Block> Container, Paragraph Paragraph)> OtherStories(WordDocument model)
    {
        var found = new List<(StoryRef, IList<Block>, Paragraph)>();
        foreach (var (id, part) in model.HeadersFooters)
        {
            foreach (var paragraph in part.Content.Blocks.OfType<Paragraph>())
            {
                found.Add((new StoryRef(StoryKind.HeaderFooter, id), part.Content.Blocks, paragraph));
            }
        }

        foreach (var (collection, prefix) in new[] { (model.Footnotes, string.Empty), (model.Endnotes, "endnote:") })
        {
            foreach (var note in collection?.Notes ?? [])
            {
                if (note.Kind != NoteKind.Normal)
                {
                    continue;
                }

                foreach (var paragraph in note.Content.Blocks.OfType<Paragraph>())
                {
                    found.Add((new StoryRef(StoryKind.Note, prefix + note.Id.ToString(CultureInfo.InvariantCulture)), note.Content.Blocks, paragraph));
                }
            }
        }

        return found;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EveryCorpusFileStaysEqualToFullLayout(string id, LayoutMode mode)
    {
        using var file = DocxFile.Open(Corpus(id));
        var model = file.Model;
        var layout = await LaidAsync(model, mode);
        var seed = unchecked((int)(Fnv1a(id) * 7919u) + (int)mode);
        var random = new Random(seed);
        var others = OtherStories(model);
        var headers = others.Where(o => o.Story.Kind == StoryKind.HeaderFooter).ToList();
        var notes = others.Where(o => o.Story.Kind == StoryKind.Note).ToList();
        var hasSectionBreak = TestEdit.Paragraphs(model).Any(p => p.Paragraph.SectionBreak is not null);
        var ran = new Dictionary<string, int> { ["header"] = 0, ["note"] = 0, ["section"] = 0, ["body"] = 0 };
        for (var step = 0; step < EditsPerFile; step++)
        {
            var label = $"{id} {mode} seed {seed} edit {step}";
            var all = TestEdit.Paragraphs(model);
            if (all.Count == 0)
            {
                break;
            }

            var long_ = step % 2 == 1;
            var kind = random.Next(10);

            // Steps 0 to 3 are fixed so that every file with such stories gets a header or footer edit, a note edit and a section-break paragraph edit. Step 0 is a SHORT
            // header or footer edit (even step: one letter), which keeps the height of the part and takes the chrome-only refresh where the layout can; step 1 is a long one that wraps.
            kind = step switch
            {
                0 when headers.Count > 0 => 100,
                1 when headers.Count > 0 => 100,
                2 when notes.Count > 0 => 101,
                3 when hasSectionBreak => 9,
                _ => kind,
            };
            BlockChange change;
            var through = 1;
            if ((kind is 100 or 101 || (kind == 8 && others.Count > 0)) && (kind == 100 ? headers : kind == 101 ? notes : others) is { Count: > 0 } pool)
            {
                var (story, container, paragraph) = pool[random.Next(pool.Count)];
                change = EditInStory(story, container, paragraph, long_ ? s_long : "z");
                through = random.Next(3);
                ran[story.Kind == StoryKind.HeaderFooter ? "header" : "note"]++;
            }
            else
            {
                var plain = all.Where(p => p.Paragraph.SectionBreak is null).ToList();
                var pickFrom = kind == 9 && all.Any(p => p.Paragraph.SectionBreak is not null) ? all.Where(p => p.Paragraph.SectionBreak is not null).ToList() : plain;
                if (pickFrom.Count == 0)
                {
                    pickFrom = all;
                }

                var pick = pickFrom[random.Next(pickFrom.Count)];
                var length = FirstText(pick.Paragraph).Length;
                ran[pick.Paragraph.SectionBreak is not null ? "section" : "body"]++;
                switch (kind)
                {
                    case 0 or 1 or 2:
                        change = TestEdit.InsertText(model, pick.Paragraph, random.Next(length + 1), long_ ? s_long : "q");
                        break;
                    case 3 when length > 3:
                        change = TestEdit.DeleteText(model, pick.Paragraph, random.Next(length / 2), long_ ? length : 3);
                        break;
                    case 4 when pick.Paragraph.SectionBreak is null:
                        change = TestEdit.SplitParagraph(model, pick.Paragraph, random.Next(length + 1));
                        break;
                    case 5 when pick.Index + 1 < pick.Container.Count && pick.Container[pick.Index + 1] is Paragraph { SectionBreak: null } && pick.Paragraph.SectionBreak is null:
                        change = TestEdit.MergeWithNext(model, pick.Paragraph);
                        break;
                    case 6 when ReferenceEquals(pick.Container, model.Body.Blocks) && pick.Container.Count > 2 && pick.Paragraph.SectionBreak is null:
                        change = TestEdit.RemoveBlock(pick.Container, pick.Index);
                        break;
                    case 7:
                        change = TestEdit.InsertText(model, pick.Paragraph, 0, long_ ? s_long : "k", replace: true);
                        break;
                    default:
                        change = TestEdit.InsertText(model, pick.Paragraph, length, long_ ? s_long : "e");
                        break;
                }

                var top = model.Body.Blocks.Contains(pick.Paragraph) || change.Added > 0 ? Math.Min(change.First, model.Body.Blocks.Count - 1) : 0;
                if (ReferenceEquals(change.Container, model.Body.Blocks) || TryTopLevel(model, pick.Paragraph, out top))
                {
                    through = (layout.Locate(Math.Max(0, Math.Min(top, model.Body.Blocks.Count - 1)))?.Page ?? 0) + 1;
                }
            }

            var refill = layout.ApplyEdit(change, through);
            Assert.True(refill.FirstPage >= 0, label);
            await layout.LayOutRestAsync(null, CancellationToken.None);
            LayoutOracle.AssertEqualToFull(layout, model, new LayoutOptions(mode), label);
        }

        Assert.Equal(EditsPerFile, layout.Stats.Edits);
        var mix = $"{id} {mode} seed {seed}: {layout.Stats.Edits} edits, {layout.Stats.Restarts} restarts, header {ran["header"]}, note {ran["note"]}, section {ran["section"]}, body {ran["body"]}";
        Assert.True(headers.Count == 0 || ran["header"] > 0, "no header or footer edit ran: " + mix);
        Assert.True(notes.Count == 0 || ran["note"] > 0, "no note edit ran: " + mix);
        Assert.True(!hasSectionBreak || ran["section"] > 0, "no section-break paragraph edit ran: " + mix);

        // Formatting: a fixed set of steps (one of each kind, not a multiplier of the edits) by the real editor on the same model, each compared with a full layout.
        var formatting = new FormattingSteps(model, layout, new LayoutOptions(mode));
        await formatting.RunAllAsync(seed, $"{id} {mode} seed {seed}");
        formatting.AssertEveryKindRan($"{id} {mode} seed {seed}");

        TestContext.Current.SendDiagnosticMessage(mix + $", chrome only {layout.Stats.ChromeOnly}, formatting steps {formatting.Compared}, restyles {formatting.Restyles}, reached into {formatting.ReachedInto}");
    }

    [Theory]
    [InlineData(LayoutMode.Pages)]
    [InlineData(LayoutMode.Flow)]
    public Task EveryFormattingKindRunsAndRestylesSomewhereInTheCorpus(LayoutMode mode) => FormattingSteps.AssertCorpusAsync(Root, mode);

    [Fact]
    public async Task AShortHeaderEditIsAChromeOnlyRefreshWhereTheLayoutCanTakeIt()
    {
        var chromeOnly = new List<string>();
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            using var file = DocxFile.Open(Corpus(entry.Id));
            var model = file.Model;
            var headers = OtherStories(model).Where(o => o.Story.Kind == StoryKind.HeaderFooter).ToList();
            if (headers.Count == 0)
            {
                continue;
            }

            var layout = await LaidAsync(model, LayoutMode.Pages);
            var (story, container, paragraph) = headers[0];
            var refill = layout.ApplyEdit(EditInStory(story, container, paragraph, "z"), 1);

            // Either the part is shown and keeps its height (chrome only), or it is shown and does not (restart), or nothing shows it (no effect): never a refill from the first page without a restart.
            Assert.False(refill.Restarted && refill.ChromeOnly, entry.Id);
            if (refill.ChromeOnly)
            {
                chromeOnly.Add(entry.Id);
                Assert.Equal(new EditStats(1, 0, 1), layout.Stats);
            }

            await layout.LayOutRestAsync(null, CancellationToken.None);
            LayoutOracle.AssertEqualToFull(layout, model, label: "short header edit " + entry.Id);
        }

        TestContext.Current.SendDiagnosticMessage("chrome only for: " + string.Join(", ", chromeOnly));
        Assert.Contains("thesis", chromeOnly);
        Assert.True(chromeOnly.Count >= 2, "the corpus takes the chrome-only path in more than one file: " + string.Join(", ", chromeOnly));
    }

    // FNV-1a over the UTF-16 code units: stable across processes (string.GetHashCode is randomized per process).
    private static uint Fnv1a(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text)
        {
            hash = unchecked((hash ^ c) * 16777619u);
        }

        return hash;
    }

    private static bool TryTopLevel(WordDocument model, Paragraph paragraph, out int index)
    {
        try
        {
            index = TestEdit.TopLevelIndex(model, paragraph);
            return true;
        }
        catch (InvalidOperationException)
        {
            index = 0;
            return false;
        }
    }

    [Fact]
    public async Task HeaderEditRelaysEveryPageThatShowsIt()
    {
        using var file = DocxFile.Open(Corpus("thesis"));
        var model = file.Model;
        var layout = await LaidAsync(model, LayoutMode.Pages);
        var (id, part) = model.HeadersFooters.First(h => h.Value.Content.Blocks.OfType<Paragraph>().Any(p => TestEdit.TextOf(p).Length > 0));
        var paragraph = part.Content.Blocks.OfType<Paragraph>().First(p => TestEdit.TextOf(p).Length > 0);
        var original = TestEdit.TextOf(paragraph);
        var showing = Enumerable.Range(0, layout.LaidOutPages).Where(i => layout.Page(i).Text.Contains(original, StringComparison.Ordinal)).ToList();
        Assert.True(showing.Count > 3, "the header is on several pages");

        var change = EditInStory(new StoryRef(StoryKind.HeaderFooter, id), part.Content.Blocks, paragraph, " EDITED");
        var refill = layout.ApplyEdit(change, 3);

        // " EDITED" keeps the height of the header on every page: only the chrome of the pages that show it is made again (not a restart; every page at once, not only up to page 3).
        Assert.False(refill.Restarted);
        Assert.True(refill.ChromeOnly);
        Assert.Equal(showing[0], refill.FirstPage);
        for (var i = 0; i <= 3 && i < layout.LaidOutPages; i++)
        {
            if (showing.Contains(i))
            {
                Assert.Contains("EDITED", layout.Page(i).Text, StringComparison.Ordinal);
            }
        }

        await layout.LayOutRestAsync(null, CancellationToken.None);
        foreach (var i in showing)
        {
            Assert.Contains("EDITED", layout.Page(i).Text, StringComparison.Ordinal);
        }

        Assert.Equal(showing.Count, Enumerable.Range(0, layout.LaidOutPages).Count(i => layout.Page(i).Text.Contains("EDITED", StringComparison.Ordinal)));
        LayoutOracle.AssertEqualToFull(layout, model, label: "header edit");
        Assert.Equal(0, layout.Stats.Restarts);
        Assert.Equal(1, layout.Stats.ChromeOnly);
    }

    private static Dictionary<string, string> Parts(params (string Id, string Xml)[] parts) => parts.ToDictionary(p => p.Id, p => p.Xml);

    private static WordDocument NumberedPages(int lines)
    {
        var parts = Parts(("fX", FooterPart(Para(Text("p") + Field("PAGE", "0") + Text(" of ") + Field("NUMPAGES", "0")))));
        return Document(Body("body", lines) + Tall(10, Footer("default", "fX")), parts);
    }

    [Fact]
    public async Task FooterWithNumPagesIsNotAFallbackByItself()
    {
        var model = NumberedPages(80);
        var layout = await LaidAsync(model, LayoutMode.Pages);
        var pages = layout.LaidOutPages;
        Assert.True(pages > 3);
        Assert.Contains($"of {pages}", PageTestKit.Lines(layout.Page(0))[^1], StringComparison.Ordinal);

        var target = (Paragraph)model.Body.Blocks[5];
        var refill = layout.ApplyEdit(TestEdit.InsertText(model, target, 2, "x"), 1);

        Assert.False(refill.Restarted);
        Assert.True(layout.IsComplete);
        Assert.Equal(pages, layout.LaidOutPages);
        Assert.Equal(0, Enumerable.Range(0, layout.LaidOutPages).Count(i => layout.Page(i).IsProvisional));
        for (var i = 0; i < pages; i++)
        {
            Assert.Contains($"of {pages}", PageTestKit.Lines(layout.Page(i))[^1], StringComparison.Ordinal);
        }

        LayoutOracle.AssertEqualToFull(layout, model, label: "numpages footer");
        Assert.Equal(new EditStats(1, 0), layout.Stats);
    }

    [Fact]
    public async Task SectionBreakEditRestarts()
    {
        using var file = DocxFile.Open(Corpus("thesis"));
        var model = file.Model;
        var layout = await LaidAsync(model, LayoutMode.Pages);
        var ender = TestEdit.Paragraphs(model).First(p => p.Paragraph.SectionBreak is not null && model.Body.Blocks.Contains(p.Paragraph)).Paragraph;
        var refill = layout.ApplyEdit(TestEdit.InsertText(model, ender, 0, "Edited "), layout.Locate(TestEdit.TopLevelIndex(model, ender))!.Value.Page + 1);

        Assert.True(refill.Restarted);
        Assert.Equal(new EditStats(1, 1), layout.Stats);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(layout, model, label: "section break");
    }

    [Fact]
    public async Task FallbackStillShowsTheEditedPage()
    {
        using var file = DocxFile.Open(Corpus("thesis"));
        var model = file.Model;
        var layout = await LaidAsync(model, LayoutMode.Pages);
        var (id, part) = model.HeadersFooters.First(h => h.Value.Content.Blocks.OfType<Paragraph>().Any(p => TestEdit.TextOf(p).Length > 0));
        var paragraph = part.Content.Blocks.OfType<Paragraph>().First(p => TestEdit.TextOf(p).Length > 0);

        // Long enough to make the header taller, so that the fallback (the restart) is what runs; the edit that keeps the height is a chrome-only refresh (ChromeRefreshTests).
        var refill = layout.ApplyEdit(EditInStory(new StoryRef(StoryKind.HeaderFooter, id), part.Content.Blocks, paragraph, string.Concat(Enumerable.Repeat(" FORCED header words that wrap", 12))), 5);

        Assert.True(refill.Restarted);
        Assert.False(layout.IsComplete);
        Assert.True(layout.LaidOutPages >= 6);

        // Every page through the one asked for is already what a full layout makes.
        var full = DocumentLayout.Create(model, PageTestKit.Fonts, new LayoutOptions());
        full.LayOutThrough(5);
        for (var i = 0; i <= 5; i++)
        {
            Assert.Equal(full.Page(i).Text, layout.Page(i).Text);
            Assert.Equal(full.Page(i).Items.Count, layout.Page(i).Items.Count);
        }
    }

    private static string LongChapters(string reference)
    {
        var body = new System.Text.StringBuilder();
        for (var chapter = 0; chapter < 5; chapter++)
        {
            body.Append("<w:p><w:pPr><w:pageBreakBefore/></w:pPr><w:r><w:t>Chapter " + chapter.ToString(CultureInfo.InvariantCulture) + "</w:t></w:r></w:p>");
            for (var i = 0; i < 18; i++)
            {
                body.Append(i == 1 && chapter == 0 ? Para(Text("chapter 0 paragraph 1 with the reference") + reference) : Line($"chapter {chapter} paragraph {i} with some words that fill a couple of lines of the page and some more"));
            }
        }

        return body.ToString();
    }

    [Fact]
    public async Task ANoteEditRefillsFromItsReferenceWithoutStartingOver()
    {
        // A long footnote that runs over the chapter starts. Edits of its text lay out again from the page of the reference to the last page that shows it.
        var note = Footnote(1, string.Join(' ', Enumerable.Repeat("the footnote that runs on over several pages", 300)));
        var model = Document(LongChapters(FootnoteRef(1)), footnotes: FootnotesPart(note));
        var layout = await LaidAsync(model, LayoutMode.Pages);
        var storyRef = new StoryRef(StoryKind.Note, "1");
        var paragraph = model.Footnotes!.Find(1)!.Content.Blocks.OfType<Paragraph>().First();
        Assert.True(layout.Page(3).TextLines.Any(l => l.Story == storyRef), "the note runs over the chapter starts");

        foreach (var (text, label) in new[] { (" and a little more", "append"), (string.Concat(Enumerable.Repeat(" a lot more words", 200)), "grow"), ("!", "again") })
        {
            var refill = layout.ApplyEdit(EditInStory(storyRef, model.Footnotes.Find(1)!.Content.Blocks, paragraph, text), 1);
            Assert.False(refill.Restarted, label);
            Assert.Equal(0, refill.FirstPage);
            await layout.LayOutRestAsync(null, CancellationToken.None);
            LayoutOracle.AssertEqualToFull(layout, model, label: "note " + label);
        }

        // Shortening it to one line: the pages after the first no longer show it.
        var content = paragraph.Inlines.OfType<Run>().SelectMany(r => r.Content.OfType<TextContent>()).First();
        var before = paragraph.Clone();
        content.Text = "short";
        var shortened = layout.ApplyEdit(new BlockChange(storyRef, model.Footnotes!.Find(1)!.Content.Blocks, 0, 1, 1, [before]), 1);
        Assert.False(shortened.Restarted);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(layout, model, label: "note shortened");
        Assert.Equal(0, layout.Stats.Restarts);
    }

    [Fact]
    public async Task AShortNoteEditStopsAtTheNextPageThatIsAsItWas()
    {
        // The note is on one page of many pages and short: the refill makes that page, sees that the next one starts as it did, and stops.
        var body = new System.Text.StringBuilder();
        for (var i = 0; i < 400; i++)
        {
            body.Append(i == 70 ? Para(Text("the cited paragraph") + FootnoteRef(1)) : Line("paragraph " + i.ToString(CultureInfo.InvariantCulture)));
        }

        var model = Document(body.ToString() + Tall(40), footnotes: FootnotesPart(Footnote(1, "short note")));
        var layout = await LaidAsync(model, LayoutMode.Pages);
        Assert.True(layout.LaidOutPages > 6);
        var storyRef = new StoryRef(StoryKind.Note, "1");
        var paragraph = model.Footnotes!.Find(1)!.Content.Blocks.OfType<Paragraph>().First();
        var refill = layout.ApplyEdit(EditInStory(storyRef, model.Footnotes.Find(1)!.Content.Blocks, paragraph, " and one word more"), 1000);

        Assert.False(refill.Restarted);
        Assert.True(layout.PagesLaidOut <= 3, "pages laid out: " + layout.PagesLaidOut);
        Assert.True(layout.IsComplete);
        LayoutOracle.AssertEqualToFull(layout, model, label: "short note");
    }

    [Fact]
    public async Task AnEndnoteEditStaysEqualToAFullLayout()
    {
        using var file = DocxFile.Open(Corpus("poi-endnotes"));
        var model = file.Model;
        var layout = await LaidAsync(model, LayoutMode.Pages);
        var endnote = model.Endnotes!.Notes.First(n => n.Kind == NoteKind.Normal && n.Content.Blocks.OfType<Paragraph>().Any());
        var story = new StoryRef(StoryKind.Note, "endnote:" + endnote.Id.ToString(CultureInfo.InvariantCulture));
        var paragraph = endnote.Content.Blocks.OfType<Paragraph>().First();
        var refill = layout.ApplyEdit(EditInStory(story, endnote.Content.Blocks, paragraph, " an edited endnote"), 1);

        Assert.False(refill.Restarted);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(layout, model, label: "endnote");
    }

    [Fact]
    public async Task ANoteThatTookNoRoomAndNowDoesStillMatches()
    {
        var model = Document(LongChapters(FootnoteRef(1)), footnotes: FootnotesPart("<w:footnote w:id=\"1\"><w:p/></w:footnote>"));
        var layout = await LaidAsync(model, LayoutMode.Pages);
        var note = model.Footnotes!.Find(1)!;
        var paragraph = note.Content.Blocks.OfType<Paragraph>().First();
        var refill = layout.ApplyEdit(EditInStory(new StoryRef(StoryKind.Note, "1"), note.Content.Blocks, paragraph, string.Join(' ', Enumerable.Repeat("now it has words", 80))), 1);

        Assert.True(refill.FirstPage >= 0);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(layout, model, label: "empty note filled");
    }

    [Fact]
    public async Task EditsThatNothingLaidOutShowsChangeNothing()
    {
        var parts = Parts(("hUsed", HeaderPart(Line("shown"))), ("hUnused", HeaderPart(Line("never shown"))));
        var model = Document(Body("body", 30) + Tall(10, Header("default", "hUsed")), parts);
        var layout = await LaidAsync(model, LayoutMode.Pages);
        var revision = layout.Revision;

        var unused = model.HeadersFooters["hUnused"].Content.Blocks;
        var none = layout.ApplyEdit(EditInStory(new StoryRef(StoryKind.HeaderFooter, "hUnused"), unused, (Paragraph)unused[0], " edited"), 1);
        Assert.False(none.Restarted);
        Assert.True(none.LastPage < none.FirstPage);

        var comment = layout.ApplyEdit(new BlockChange(new StoryRef(StoryKind.Comment, "7"), new List<Block>(), 0, 1, 1, [new Paragraph()]), 1);
        Assert.False(comment.Restarted);
        Assert.True(comment.LastPage < comment.FirstPage);
        Assert.Equal(revision, layout.Revision);
        Assert.Equal(new EditStats(2, 0), layout.Stats);
        LayoutOracle.AssertEqualToFull(layout, model, label: "no effect");

        // The same edit in the writing view, where no header, note or comment is laid out at all.
        var flow = await LaidAsync(model, LayoutMode.Flow);
        var used = model.HeadersFooters["hUsed"].Content.Blocks;
        var refill = flow.ApplyEdit(EditInStory(new StoryRef(StoryKind.HeaderFooter, "hUsed"), used, (Paragraph)used[0], " edited"), 1);
        Assert.False(refill.Restarted);
        LayoutOracle.AssertEqualToFull(flow, model, new LayoutOptions(LayoutMode.Flow), "flow header");
    }

    [Fact]
    public async Task TheWritingViewIgnoresSectionBreaksSoTakingOneAwayIsNoRestart()
    {
        using var file = DocxFile.Open(Corpus("thesis"));
        var model = file.Model;
        var layout = await LaidAsync(model, LayoutMode.Flow);
        var ender = TestEdit.Paragraphs(model).First(p => p.Paragraph.SectionBreak is not null && model.Body.Blocks.Contains(p.Paragraph));
        var before = ender.Paragraph.Clone();
        ender.Paragraph.SectionBreak = null;
        var refill = layout.ApplyEdit(new BlockChange(StoryRef.Body, ender.Container, ender.Index, 1, 1, [before]), 1000);

        Assert.False(refill.Restarted);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(layout, model, new LayoutOptions(LayoutMode.Flow), "section break taken away in the writing view");
    }

    [Fact]
    public async Task ANoteThatWasCarriedWholeOntoLaterPagesIsLaidOutFromItsReferencePage()
    {
        // Note 1 is so long that note 2, cited on the same page, is pushed whole onto later pages: no line of it is on the page of its reference, and the
        // page where its first line is has the note in the notes that run over. Editing it must not resume from that page.
        var first = Footnote(1, string.Join(' ', Enumerable.Repeat("the first footnote that runs on and on over pages", 300)));
        var second = Footnote(2, string.Join(' ', Enumerable.Repeat("the second note", 40)));
        var model = Document(LongChapters(FootnoteRef(1) + FootnoteRef(2)), footnotes: FootnotesPart(first, second));
        var layout = await LaidAsync(model, LayoutMode.Pages);
        var story = new StoryRef(StoryKind.Note, "2");
        Assert.False(layout.Page(0).TextLines.Any(l => l.Story == story), "the second note starts after the page of its reference");
        Assert.Contains(Enumerable.Range(0, layout.LaidOutPages), i => layout.Page(i).TextLines.Any(l => l.Story == story));

        var note = model.Footnotes!.Find(2)!;
        var refill = layout.ApplyEdit(EditInStory(story, note.Content.Blocks, note.Content.Blocks.OfType<Paragraph>().First(), " and a few words more"), 1000);

        Assert.False(refill.Restarted);
        Assert.Equal(0, refill.FirstPage);
        LayoutOracle.AssertEqualToFull(layout, model, label: "note carried whole");
    }
}
