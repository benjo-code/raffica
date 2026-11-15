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
}
