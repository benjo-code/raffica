using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Xunit;
using static Raffica.Write.Tests.Layout.ChromeTestKit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Layout;

/// <summary>
/// An edit of a header or footer that leaves the height of every page's header and footer as it was changes no body item: only the chrome of the pages that show the
/// part is made again (<see cref="EditRefill.ChromeOnly"/>, not a restart). An edit that changes a height, and anything that cannot be proved unchanged, still restarts.
/// The exact-equality oracle decides in every case.
/// </summary>
public sealed class ChromeRefreshTests
{
    private static Dictionary<string, string> Parts(params (string Id, string Xml)[] parts) => parts.ToDictionary(p => p.Id, p => p.Xml, StringComparer.Ordinal);

    private static async Task<DocumentLayout> LaidAsync(WordDocument model)
    {
        var layout = DocumentLayout.Create(model, Fonts, new LayoutOptions());
        await layout.LayOutRestAsync(null, CancellationToken.None);
        Assert.True(layout.IsComplete);
        return layout;
    }

    /// <summary>Appends text to the first paragraph of the header or footer part, in place.</summary>
    private static BlockChange Edit(WordDocument model, string id, string text)
    {
        var blocks = model.HeadersFooters[id].Content.Blocks;
        var paragraph = (Paragraph)blocks[0];
        var before = paragraph.Clone();
        var content = paragraph.Inlines.OfType<Run>().SelectMany(r => r.Content.OfType<TextContent>()).Last();
        content.Text += text;
        return new BlockChange(new StoryRef(StoryKind.HeaderFooter, id), blocks, 0, 1, 1, [before]);
    }

    private static string Document8Pages(string sectionExtra) => Body("body", 80) + Tall(10, sectionExtra);

    [Fact]
    public async Task AHeaderEditThatKeepsItsHeightRefreshesOnlyTheChrome()
    {
        var model = Document(Document8Pages(Header("default", "hD")), Parts(("hD", HeaderPart(Line("DEFAULT")))));
        var layout = await LaidAsync(model);
        var pages = layout.LaidOutPages;
        Assert.True(pages > 5);
        var revision = layout.Revision;

        var refill = layout.ApplyEdit(Edit(model, "hD", " X"), 1);

        Assert.False(refill.Restarted);
        Assert.True(refill.ChromeOnly);
        Assert.Equal(new EditStats(1, 0, 1), layout.Stats);
        Assert.True(layout.IsComplete);
        Assert.Equal(pages, layout.LaidOutPages);
        Assert.True(layout.Revision > revision);

        // Every page already shows the new header, not only the pages up to the one asked for.
        for (var i = 0; i < pages; i++)
        {
            Assert.Equal("DEFAULT X", FirstLine(layout.Page(i)));
        }

        LayoutOracle.AssertEqualToFull(layout, model, label: "chrome only header");

        // And again, so that the refreshed pages are themselves refreshed correctly.
        Assert.True(layout.ApplyEdit(Edit(model, "hD", "Y"), 1).ChromeOnly);
        LayoutOracle.AssertEqualToFull(layout, model, label: "chrome only header twice");
        Assert.Equal(new EditStats(2, 0, 2), layout.Stats);
    }

    [Fact]
    public async Task AHeaderEditThatChangesItsHeightStillRestartsAndIsCorrect()
    {
        var model = Document(Document8Pages(Header("default", "hD")), Parts(("hD", HeaderPart(Line("DEFAULT")))));
        var layout = await LaidAsync(model);

        // Long enough to wrap onto more lines: the header is taller, the body moves.
        var refill = layout.ApplyEdit(Edit(model, "hD", string.Concat(Enumerable.Repeat(" a long header text that wraps", 12))), 1);

        Assert.True(refill.Restarted);
        Assert.False(refill.ChromeOnly);
        Assert.Equal(new EditStats(1, 1, 0), layout.Stats);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(layout, model, label: "taller header");
    }

    [Fact]
    public async Task FirstPageAndEvenOddHeadersAreEachRefreshedOrRestartedCorrectly()
    {
        var parts = Parts(("hD", HeaderPart(Line("DEFAULT"))), ("hF", HeaderPart(Line("FIRST"))), ("hE", HeaderPart(Line("EVEN"))));
        var model = Document(Document8Pages(Header("default", "hD") + Header("first", "hF") + Header("even", "hE") + "<w:titlePg/>"), parts, settingsInner: "<w:evenAndOddHeaders/>");
        var layout = await LaidAsync(model);
        Assert.Equal("FIRST", FirstLine(layout.Page(0)));
        Assert.Equal("EVEN", FirstLine(layout.Page(1)));
        Assert.Equal("DEFAULT", FirstLine(layout.Page(2)));

        foreach (var id in new[] { "hF", "hE", "hD" })
        {
            var refill = layout.ApplyEdit(Edit(model, id, " X"), 1);
            Assert.True(refill.ChromeOnly, id);
            Assert.False(refill.Restarted, id);
            LayoutOracle.AssertEqualToFull(layout, model, label: "variant " + id);
        }

        Assert.Equal("FIRST X", FirstLine(layout.Page(0)));
        Assert.Equal("EVEN X", FirstLine(layout.Page(1)));
        Assert.Equal("DEFAULT X", FirstLine(layout.Page(2)));
        Assert.Equal("EVEN X", FirstLine(layout.Page(3)));
    }

    [Fact]
    public async Task AHeaderWithAPageFieldKeepsEachPagesNumber()
    {
        var model = Document(Document8Pages(Header("default", "hD")), Parts(("hD", HeaderPart(Para(Field("PAGE", "0") + Text(" pg"))))));
        var layout = await LaidAsync(model);
        Assert.Equal("1 pg", FirstLine(layout.Page(0)));

        var refill = layout.ApplyEdit(Edit(model, "hD", "!"), 1);

        Assert.True(refill.ChromeOnly);
        for (var i = 0; i < layout.LaidOutPages; i++)
        {
            Assert.Equal($"{i + 1} pg!", FirstLine(layout.Page(i)));
        }

        LayoutOracle.AssertEqualToFull(layout, model, label: "page field header");
    }

    [Fact]
    public async Task ASectionThatInheritsTheHeaderShowsTheEditToo()
    {
        var parts = Parts(("h1", HeaderPart(Line("ONE"))), ("h3", HeaderPart(Line("THREE"))));
        var body = Body("a", 8) + PEnd("a end", Tall(4, Header("default", "h1"))) + Body("b", 8) + PEnd("b end", Tall(4)) + Body("c", 8) + PEnd("c end", Tall(4, Header("default", "h3")));
        var model = Document(body, parts);
        var layout = await LaidAsync(model);
        var pages = layout.LaidOutPages;
        Assert.True(pages > 6);
        Assert.Equal("ONE", FirstLine(layout.Page(0)));
        Assert.Equal("THREE", FirstLine(layout.Page(pages - 1)));

        var refill = layout.ApplyEdit(Edit(model, "h1", " X"), 1);

        Assert.True(refill.ChromeOnly);
        Assert.Equal("ONE X", FirstLine(layout.Page(0)));
        Assert.Equal("THREE", FirstLine(layout.Page(pages - 1)));
        Assert.Equal(pages, Enumerable.Range(0, pages).Count(i => FirstLine(layout.Page(i)) is "ONE X" or "THREE"));
        Assert.True(Enumerable.Range(0, pages).Count(i => FirstLine(layout.Page(i)) == "ONE X") > 3, "the inheriting section shows the edit");
        LayoutOracle.AssertEqualToFull(layout, model, label: "inherited header");
    }

    [Fact]
    public async Task AFooterWithATotalIsRefreshedWithTheRealTotal()
    {
        var parts = Parts(("fX", FooterPart(Para(Text("p") + Field("PAGE", "0") + Text(" of ") + Field("NUMPAGES", "0") + Text(" tail")))));
        var model = Document(Document8Pages(Footer("default", "fX")), parts);
        var layout = await LaidAsync(model);
        var pages = layout.LaidOutPages;

        var refill = layout.ApplyEdit(Edit(model, "fX", "!"), 1);

        Assert.True(refill.ChromeOnly);
        Assert.Equal(0, Enumerable.Range(0, pages).Count(i => layout.Page(i).IsProvisional));
        for (var i = 0; i < pages; i++)
        {
            Assert.Contains($"of {pages} tail!", LastLine(layout.Page(i)), StringComparison.Ordinal);
        }

        LayoutOracle.AssertEqualToFull(layout, model, label: "footer with total");
    }

    [Fact]
    public async Task AHeaderEditWhileOnlyPartOfTheLayoutExistsStaysEqualToFullLayout()
    {
        var parts = Parts(("hD", HeaderPart(Line("DEFAULT"))), ("fX", FooterPart(Para(Text("of ") + Field("NUMPAGES", "0") + Text(" tail")))));
        var model = Document(Document8Pages(Header("default", "hD") + Footer("default", "fX")), parts);
        var layout = DocumentLayout.Create(model, Fonts, new LayoutOptions());
        layout.LayOutThrough(2);
        Assert.False(layout.IsComplete);
        var laid = layout.LaidOutPages;
        Assert.True(laid >= 3);

        var refill = layout.ApplyEdit(Edit(model, "hD", " X"), 1);
        Assert.False(refill.Restarted);
        Assert.False(layout.IsComplete);
        for (var i = 0; i < layout.LaidOutPages; i++)
        {
            Assert.Equal("DEFAULT X", FirstLine(layout.Page(i)));
            Assert.True(layout.Page(i).IsProvisional, "the footer total is still unknown");
        }

        var footer = layout.ApplyEdit(Edit(model, "fX", "!"), 1);
        Assert.False(footer.Restarted);

        await layout.LayOutRestAsync(null, CancellationToken.None);
        Assert.True(layout.IsComplete);
        Assert.Equal(0, Enumerable.Range(0, layout.LaidOutPages).Count(i => layout.Page(i).IsProvisional));
        for (var i = 0; i < layout.LaidOutPages; i++)
        {
            Assert.Equal("DEFAULT X", FirstLine(layout.Page(i)));
            Assert.Contains($"of {layout.LaidOutPages} tail!", LastLine(layout.Page(i)), StringComparison.Ordinal);
        }

        LayoutOracle.AssertEqualToFull(layout, model, label: "incomplete layout");
    }

    [Fact]
    public async Task ABodyEditAfterAChromeRefreshKeepsTheRefreshedChrome()
    {
        var model = Document(Document8Pages(Header("default", "hD")), Parts(("hD", HeaderPart(Line("DEFAULT")))));
        var layout = await LaidAsync(model);
        Assert.True(layout.ApplyEdit(Edit(model, "hD", " X"), 1).ChromeOnly);

        // Edits of the body after it (one that moves a page break too) keep the new header on every page, kept or made again.
        var target = (Paragraph)model.Body.Blocks[3];
        layout.ApplyEdit(TestEdit.InsertText(model, target, 2, "x"), 2);
        var middle = (Paragraph)model.Body.Blocks[40];
        layout.ApplyEdit(TestEdit.InsertText(model, middle, 0, string.Concat(Enumerable.Repeat("more words on this line ", 30))), 3);
        await layout.LayOutRestAsync(null, CancellationToken.None);

        for (var i = 0; i < layout.LaidOutPages; i++)
        {
            Assert.Equal("DEFAULT X", FirstLine(layout.Page(i)));
        }

        LayoutOracle.AssertEqualToFull(layout, model, label: "body after chrome refresh");
    }

    /// <summary>Replaces the first paragraph of the part by the one the XML makes (a field added or taken away, as an undo does), as one change.</summary>
    private static BlockChange Replace(WordDocument model, string id, string paragraphXml)
    {
        var blocks = model.HeadersFooters[id].Content.Blocks;
        var before = (Paragraph)blocks[0];
        var donor = Document(string.Empty, Parts(("donor", HeaderPart(paragraphXml))));
        var replacement = (Paragraph)donor.HeadersFooters["donor"].Content.Blocks[0];
        blocks[0] = replacement;
        return new BlockChange(new StoryRef(StoryKind.HeaderFooter, id), blocks, 0, 1, 1, [before]);
    }

    private static string WithTotal => Para(Text("DEFAULT ") + Field("NUMPAGES", "0"));

    private static string WithoutTotal => Para(Text("DEFAULT"));

    private static async Task AssertNoStaleTotals(DocumentLayout layout, WordDocument model, string label)
    {
        await layout.LayOutRestAsync(null, CancellationToken.None);
        Assert.True(layout.IsComplete);
        Assert.Equal(0, Enumerable.Range(0, layout.LaidOutPages).Count(i => layout.Page(i).IsProvisional));
        LayoutOracle.AssertEqualToFull(layout, model, label: label);
    }

    [Fact]
    public async Task AddingATotalFieldToAHeaderWhileTheLayoutIsIncompleteEndsWithTheRealTotalEverywhere()
    {
        var model = Document(Document8Pages(Header("default", "hD")), Parts(("hD", HeaderPart(Line("DEFAULT")))));
        var layout = DocumentLayout.Create(model, Fonts, new LayoutOptions());
        layout.LayOutThrough(2);
        Assert.False(layout.IsComplete);

        layout.ApplyEdit(Replace(model, "hD", WithTotal), 1);
        await AssertNoStaleTotals(layout, model, "total added while incomplete");

        Assert.Equal($"DEFAULT {layout.LaidOutPages}", FirstLine(layout.Page(0)));
        Assert.Equal($"DEFAULT {layout.LaidOutPages}", FirstLine(layout.Page(layout.LaidOutPages - 1)));
    }

    [Fact]
    public async Task TakingATotalFieldOutOfAHeaderWhileTheLayoutIsIncompleteLeavesNothingProvisional()
    {
        var model = Document(Document8Pages(Header("default", "hD")), Parts(("hD", HeaderPart(WithTotal))));
        var layout = DocumentLayout.Create(model, Fonts, new LayoutOptions());
        layout.LayOutThrough(2);
        Assert.False(layout.IsComplete);
        Assert.True(layout.Page(0).IsProvisional);

        layout.ApplyEdit(Replace(model, "hD", WithoutTotal), 1);
        await AssertNoStaleTotals(layout, model, "total removed while incomplete");

        Assert.Equal("DEFAULT", FirstLine(layout.Page(0)));
    }

    [Fact]
    public async Task AddingATotalFieldToACompleteLayoutIsRightAndStaysRightWhenThePageCountChanges()
    {
        var model = Document(Document8Pages(Header("default", "hD")), Parts(("hD", HeaderPart(Line("DEFAULT")))));
        var layout = await LaidAsync(model);
        var pages = layout.LaidOutPages;

        // A part that gains a page count field cannot be refreshed in place (the filler revises only pages that showed one): the restart makes the right pages.
        var refill = layout.ApplyEdit(Replace(model, "hD", WithTotal), 1);
        Assert.True(refill.Restarted);
        Assert.False(refill.ChromeOnly);
        await AssertNoStaleTotals(layout, model, "total added to a complete layout");
        Assert.Equal($"DEFAULT {pages}", FirstLine(layout.Page(0)));

        // Body edits that add pages: every page, those kept before the edit included, must show the new total.
        var middle = (Paragraph)model.Body.Blocks[40];
        layout.ApplyEdit(TestEdit.InsertText(model, middle, 0, string.Concat(Enumerable.Repeat("a long insert that wraps and adds lines ", 90))), 3);
        await AssertNoStaleTotals(layout, model, "page count changed after a total was added");
        Assert.True(layout.LaidOutPages > pages);
        Assert.Equal($"DEFAULT {layout.LaidOutPages}", FirstLine(layout.Page(0)));
    }

    [Fact]
    public async Task TakingATotalFieldOutOfACompleteLayoutStaysRightWhenThePageCountChanges()
    {
        var model = Document(Document8Pages(Header("default", "hD")), Parts(("hD", HeaderPart(WithTotal))));
        var layout = await LaidAsync(model);

        layout.ApplyEdit(Replace(model, "hD", WithoutTotal), 1);
        LayoutOracle.AssertEqualToFull(layout, model, label: "total removed from a complete layout");

        var middle = (Paragraph)model.Body.Blocks[40];
        layout.ApplyEdit(TestEdit.InsertText(model, middle, 0, string.Concat(Enumerable.Repeat("a long insert that wraps and adds lines ", 90))), 3);
        await AssertNoStaleTotals(layout, model, "page count changed after a total was removed");
        Assert.Equal("DEFAULT", FirstLine(layout.Page(0)));
    }

    [Fact]
    public async Task AChromeRefreshOfPageOneRepublishedByReplaceModelEqualsAFullLayout()
    {
        var bytes = File.ReadAllBytes(Path.Combine(TestData.Root, "testdata", "write-corpus", "thesis", "thesis.docx"));
        using var prefix = DocxFile.OpenPrefix(bytes, 64);
        var layout = DocumentLayout.Create(prefix.Model, Fonts, new LayoutOptions());
        layout.LayOutFirstPage();
        using var full = DocxFile.Open(bytes);
        var model = full.Model;
        layout.ReplaceModel(model);

        // Only page 1 exists, republished by ReplaceModel with the prefix model's chrome record: a short edit of a part it shows keeps its height.
        var (id, part) = model.HeadersFooters.First(h => h.Value.Content.Blocks.OfType<Paragraph>().Any(p => TestEdit.TextOf(p).Length > 0)
            && layout.Page(0).Text.Contains(TestEdit.TextOf(h.Value.Content.Blocks.OfType<Paragraph>().First(p => TestEdit.TextOf(p).Length > 0)), StringComparison.Ordinal));
        var paragraph = part.Content.Blocks.OfType<Paragraph>().First(p => TestEdit.TextOf(p).Length > 0);
        var before = paragraph.Clone();
        paragraph.Inlines.OfType<Run>().SelectMany(r => r.Content.OfType<TextContent>()).Last().Text += "!";
        var refill = layout.ApplyEdit(new BlockChange(new StoryRef(StoryKind.HeaderFooter, id), part.Content.Blocks, part.Content.Blocks.IndexOf(paragraph), 1, 1, [before]), 0);

        Assert.True(refill.ChromeOnly, "the short edit keeps the height, so page 1 is refreshed in place");
        Assert.False(refill.Restarted);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        Assert.True(layout.IsComplete);
        LayoutOracle.AssertEqualToFull(layout, model, label: "chrome refresh after ReplaceModel");
    }

    // ---- The window and the background pass: a long document, so that a footer edit has pages far from the caret.

    private const int Window = 8;

    private static string LongDocument(string sectionExtra) => Body("body", 600) + Tall(10, sectionExtra);

    private static WordDocument PageFooterDocument() =>
        Document(LongDocument(Footer("default", "fP") + Header("default", "hD")), Parts(("fP", FooterPart(Para(Text("A-") + Field("PAGE", "0") + Text(" tail")))), ("hD", HeaderPart(Line("HEAD")))));

    private static string[] Texts(DocumentLayout layout) => [.. Enumerable.Range(0, layout.LaidOutPages).Select(i => layout.Page(i).Text)];

    private static string[] FullTexts(WordDocument model)
    {
        var full = DocumentLayout.Create(model, Fonts, new LayoutOptions());
        full.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        return Texts(full);
    }

    private static async Task Drain(DocumentLayout layout) => await layout.LayOutRestAsync(null, CancellationToken.None);

    [Fact]
    public async Task AFooterEditRefreshesTheWindowAtOnceAndTheRestInTheBackground()
    {
        var model = PageFooterDocument();
        var layout = await LaidAsync(model);
        var pages = layout.LaidOutPages;
        Assert.True(pages > 40);
        var before = Texts(layout);
        var oldFirst = layout.Page(0);
        var through = 20;

        var refill = layout.ApplyEdit(Edit(model, "fP", "!"), through);

        Assert.True(refill.ChromeOnly);
        Assert.False(refill.Restarted);
        Assert.Equal(through - Window, refill.FirstPage);
        Assert.Equal(through + Window, refill.LastPage);
        Assert.True(layout.ChromeRefreshPending);
        Assert.False(layout.IsSettled);
        Assert.True(layout.IsComplete);
        for (var i = 0; i < pages; i++)
        {
            var expected = i >= through - Window && i <= through + Window ? "tail!" : "tail";
            Assert.True(LastLine(layout.Page(i)).EndsWith(expected, StringComparison.Ordinal), $"page {i}: {LastLine(layout.Page(i))}");
        }

        Assert.Same(oldFirst, layout.Page(0));
        Assert.Equal(before[0], layout.Page(0).Text);

        await Drain(layout);
        Assert.False(layout.ChromeRefreshPending);
        Assert.True(layout.IsSettled);
        for (var i = 0; i < pages; i++)
        {
            Assert.EndsWith("tail!", LastLine(layout.Page(i)), StringComparison.Ordinal);
        }

        LayoutOracle.AssertEqualToFull(layout, model, label: "footer window and pass");
        Assert.Equal(new EditStats(1, 0, 1), layout.Stats);
    }

    [Fact]
    public async Task AStalePageIsTheOldConsistentPageAndEachBatchRaisesTheRevision()
    {
        var model = PageFooterDocument();
        var layout = await LaidAsync(model);
        var pages = layout.LaidOutPages;
        var oldTexts = Texts(layout);
        layout.ApplyEdit(Edit(model, "fP", "!"), 20);
        var newTexts = FullTexts(model);
        Assert.NotEqual(oldTexts[0], newTexts[0]);

        var batches = 0;
        var revision = layout.Revision;
        while (layout.ChromeRefreshPending)
        {
            // One page per batch: every page, read at any moment, is the old one or the new one, never a mixture.
            Assert.True(layout.RefreshChromeBatch(0));
            batches++;
            Assert.True(layout.Revision > revision, "each batch raises the revision");
            revision = layout.Revision;
            for (var i = 0; i < pages; i++)
            {
                Assert.True(layout.TryGetPage(i, out var page));
                Assert.True(page!.Text == oldTexts[i] || page.Text == newTexts[i], $"page {i} after batch {batches}");
            }
        }

        Assert.True(batches >= pages - ((2 * Window) + 1), $"batches: {batches}");
        Assert.False(layout.RefreshChromeBatch(0), "nothing left to do");
        LayoutOracle.AssertEqualToFull(layout, model, label: "batches");
    }

    [Fact]
    public async Task ASecondEditSupersedesThePassInTheMiddleOfIt()
    {
        var model = PageFooterDocument();
        var layout = await LaidAsync(model);
        layout.ApplyEdit(Edit(model, "fP", "!"), 20);
        for (var i = 0; i < 10; i++)
        {
            Assert.True(layout.RefreshChromeBatch(0));
        }

        // The same part again, and then the other part (the header), while pages are still stale for the first edit.
        layout.ApplyEdit(Edit(model, "fP", "?"), 30);
        for (var i = 0; i < 5; i++)
        {
            layout.RefreshChromeBatch(0);
        }

        layout.ApplyEdit(Edit(model, "hD", "+"), 5);
        Assert.True(layout.ChromeRefreshPending);
        await Drain(layout);
        for (var i = 0; i < layout.LaidOutPages; i++)
        {
            Assert.Equal("HEAD+", FirstLine(layout.Page(i)));
            Assert.EndsWith("tail!?", LastLine(layout.Page(i)), StringComparison.Ordinal);
        }

        LayoutOracle.AssertEqualToFull(layout, model, label: "superseded pass");
        Assert.Equal(new EditStats(3, 0, 3), layout.Stats);
    }

    [Fact]
    public async Task ABodyEditWhileThePassIsPendingKeepsEveryPageRight()
    {
        var model = PageFooterDocument();
        var layout = await LaidAsync(model);
        layout.ApplyEdit(Edit(model, "fP", "!"), 20);
        layout.RefreshChromeBatch(0);

        // Before the caret page, after it, and one that adds lines (a page break moves): the pages they fill get fresh chrome, the stale ones stay stale and are finished by the pass.
        layout.ApplyEdit(TestEdit.InsertText(model, (Paragraph)model.Body.Blocks[20], 1, "x"), 2);
        layout.ApplyEdit(TestEdit.InsertText(model, (Paragraph)model.Body.Blocks[380], 1, "x"), 40);
        var middle = (Paragraph)model.Body.Blocks[200];
        layout.ApplyEdit(TestEdit.InsertText(model, middle, 0, string.Concat(Enumerable.Repeat("a long insert that wraps and adds lines ", 90))), 25);
        await Drain(layout);

        for (var i = 0; i < layout.LaidOutPages; i++)
        {
            Assert.EndsWith("tail!", LastLine(layout.Page(i)), StringComparison.Ordinal);
        }

        LayoutOracle.AssertEqualToFull(layout, model, label: "body edits during the pass");
    }

    [Fact]
    public async Task RevertingTheEditInTheMiddleOfThePassEndsWithTheOriginalChrome()
    {
        var model = PageFooterDocument();
        var layout = await LaidAsync(model);
        var original = Texts(layout);
        layout.ApplyEdit(Edit(model, "fP", "!"), 20);
        for (var i = 0; i < 12; i++)
        {
            layout.RefreshChromeBatch(0);
        }

        // Undo: the text goes back (as the history does, the paragraph is replaced by its earlier state).
        var blocks = model.HeadersFooters["fP"].Content.Blocks;
        var current = (Paragraph)blocks[0];
        var before = current.Clone();
        current.Inlines.OfType<Run>().SelectMany(r => r.Content.OfType<TextContent>()).Last().Text = " tail";
        layout.ApplyEdit(new BlockChange(new StoryRef(StoryKind.HeaderFooter, "fP"), blocks, 0, 1, 1, [before]), 20);
        await Drain(layout);

        Assert.Equal(original, Texts(layout));
        LayoutOracle.AssertEqualToFull(layout, model, label: "reverted mid pass");
    }

    [Fact]
    public async Task ACancelledPassResumesWhereItStopped()
    {
        var model = PageFooterDocument();
        var layout = await LaidAsync(model);
        layout.ApplyEdit(Edit(model, "fP", "!"), 20);
        using var cancel = new CancellationTokenSource();
        var batches = 0;
        var progress = new SyncProgress(() =>
        {
            if (++batches == 1)
            {
                cancel.Cancel();
            }
        });

        // The pass stops at a batch boundary when the token is cancelled (a lease does that), leaves the work that is left and goes on later.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => layout.LayOutRestAsync(progress, cancel.Token));
        Assert.True(layout.ChromeRefreshPending);
        await Drain(layout);
        Assert.True(layout.IsSettled);
        LayoutOracle.AssertEqualToFull(layout, model, label: "cancelled and resumed pass");
    }

    private sealed class SyncProgress(Action onReport) : IProgress<int>
    {
        public void Report(int value) => onReport();
    }

    [Fact]
    public async Task AFooterEditOnAnIncompleteLayoutIsRightAfterTheRestIsLaidOut()
    {
        var model = PageFooterDocument();
        var layout = DocumentLayout.Create(model, Fonts, new LayoutOptions());
        layout.LayOutThrough(30);
        Assert.False(layout.IsComplete);

        var refill = layout.ApplyEdit(Edit(model, "fP", "!"), 20);

        Assert.True(refill.ChromeOnly);
        Assert.True(layout.ChromeRefreshPending);
        await Drain(layout);
        Assert.True(layout.IsSettled);
        LayoutOracle.AssertEqualToFull(layout, model, label: "incomplete layout, footer window");
    }

    [Fact]
    public async Task HeaderAndVariantEditsTakeTheNewPathToo()
    {
        var parts = Parts(("hD", HeaderPart(Line("DEFAULT"))), ("hF", HeaderPart(Line("FIRST"))), ("hE", HeaderPart(Line("EVEN"))));
        var model = Document(LongDocument(Header("default", "hD") + Header("first", "hF") + Header("even", "hE") + "<w:titlePg/>"), parts, settingsInner: "<w:evenAndOddHeaders/>");
        var layout = await LaidAsync(model);

        foreach (var (id, through) in new[] { ("hE", 30), ("hF", 30), ("hD", 10) })
        {
            var refill = layout.ApplyEdit(Edit(model, id, " X"), through);
            Assert.True(refill.ChromeOnly, id);
            await Drain(layout);
            LayoutOracle.AssertEqualToFull(layout, model, label: "variant " + id);
        }

        Assert.Equal("FIRST X", FirstLine(layout.Page(0)));
        Assert.Equal("EVEN X", FirstLine(layout.Page(35)));
        Assert.Equal("DEFAULT X", FirstLine(layout.Page(36)));
    }

    [Fact]
    public async Task AWrappedLineStillFallsBackToTheRestartAndClearsThePass()
    {
        var model = PageFooterDocument();
        var layout = await LaidAsync(model);
        layout.ApplyEdit(Edit(model, "fP", "!"), 20);
        Assert.True(layout.ChromeRefreshPending);

        var refill = layout.ApplyEdit(Edit(model, "fP", string.Concat(Enumerable.Repeat(" a long footer text that wraps", 12))), 20);

        Assert.True(refill.Restarted);
        Assert.False(refill.ChromeOnly);
        Assert.False(layout.ChromeRefreshPending, "a restart makes every page again");
        await Drain(layout);
        LayoutOracle.AssertEqualToFull(layout, model, label: "wrapped footer after a pending pass");
    }

    [Fact]
    public async Task APartThatShowsATotalIsRefreshedForAllPagesAtOnce()
    {
        var parts = Parts(("fX", FooterPart(Para(Text("p") + Field("PAGE", "0") + Text(" of ") + Field("NUMPAGES", "0") + Text(" tail")))));
        var model = Document(LongDocument(Footer("default", "fX")), parts);
        var layout = await LaidAsync(model);

        var refill = layout.ApplyEdit(Edit(model, "fX", "!"), 20);

        // A part with a page count keeps the synchronous refresh of every page (its pages are revised against the real total).
        Assert.True(refill.ChromeOnly);
        Assert.False(layout.ChromeRefreshPending);
        Assert.Equal(0, refill.FirstPage);
        Assert.Equal(layout.LaidOutPages - 1, refill.LastPage);
        LayoutOracle.AssertEqualToFull(layout, model, label: "total footer, long document");
    }
}
