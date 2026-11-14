using Raffica.Write.Layout;
using Xunit;
using static Raffica.Write.Tests.Layout.ChromeTestKit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Layout;

public sealed class HeaderFooterTests
{
    private const string Break = "<w:pageBreakBefore/>";

    private static Dictionary<string, string> Parts(params (string Id, string Xml)[] parts) =>
        parts.ToDictionary(p => p.Id, p => p.Xml, StringComparer.Ordinal);

    [Fact]
    public void FirstPageAndEvenOddHeaders()
    {
        var parts = Parts(
            ("hD", HeaderPart(Line("DEFAULT"))),
            ("hF", HeaderPart(Line("FIRST"))),
            ("hE", HeaderPart(Line("EVEN"))));
        var section = Tall(4, Header("default", "hD") + Header("first", "hF") + "<w:titlePg/>");
        var first = Laid(Document(Body("body", 11) + PEnd("body 12", section), parts));
        Assert.Equal(3, first.LaidOutPages);
        Assert.Equal("FIRST", FirstLine(first.Page(0)));
        Assert.Equal("DEFAULT", FirstLine(first.Page(1)));
        Assert.Equal("DEFAULT", FirstLine(first.Page(2)));

        // The header sits in the top margin: between the header distance (36 pt) and the body.
        var run = Runs(first.Page(0))[0];
        Assert.InRange(run.Baseline, 36f, 72f);

        var evenSection = Tall(4, Header("default", "hD") + Header("even", "hE"));
        var even = Laid(Document(Body("body", 11) + PEnd("body 12", evenSection), parts, settingsInner: "<w:evenAndOddHeaders/>"));
        Assert.Equal("DEFAULT", FirstLine(even.Page(0)));
        Assert.Equal("EVEN", FirstLine(even.Page(1)));
        Assert.Equal("DEFAULT", FirstLine(even.Page(2)));

        // Without the setting the even header is never used.
        var plain = Laid(Document(Body("body", 11) + PEnd("body 12", evenSection), parts));
        Assert.Equal("DEFAULT", FirstLine(plain.Page(1)));
    }

    [Fact]
    public void TitlePageWithoutFirstHeaderShowsNoHeaderOnTheFirstPage()
    {
        var parts = Parts(("hD", HeaderPart(Line("DEFAULT"))));
        var section = Tall(4, Header("default", "hD") + "<w:titlePg/>");
        var layout = Laid(Document(Body("body", 7) + PEnd("body 8", section), parts));
        Assert.Equal("body 1", FirstLine(layout.Page(0)));
        Assert.Equal("DEFAULT", FirstLine(layout.Page(1)));
    }

    [Fact]
    public void SectionWithoutOwnReferenceInheritsThePreviousOne()
    {
        var parts = Parts(
            ("h1", HeaderPart(Line("ONE"))),
            ("h3", HeaderPart(Line("THREE"))),
            ("f1", FooterPart(Line("FOOT"))));
        var s1 = Sect(Header("default", "h1") + Footer("default", "f1"));
        var s2 = Sect();
        var s3 = Sect(Header("default", "h3"));
        var body = PEnd("a", s1) + PEnd("b", s2) + PEnd("c", s3);
        var layout = Laid(Document(body, parts));
        Assert.Equal(3, layout.LaidOutPages);
        Assert.Equal("ONE", FirstLine(layout.Page(0)));
        Assert.Equal("ONE", FirstLine(layout.Page(1)));
        Assert.Equal("THREE", FirstLine(layout.Page(2)));

        // The footer is inherited by every section that has none of its own.
        Assert.Equal("FOOT", LastLine(layout.Page(1)));
        Assert.Equal("FOOT", LastLine(layout.Page(2)));
    }

    [Fact]
    public void PageFieldShowsTheDisplayNumber()
    {
        var parts = Parts(
            ("fA", FooterPart(Para("<w:fldSimple w:instr=\" PAGE \"><w:r><w:t>X</w:t></w:r></w:fldSimple>"))),
            ("fB", FooterPart(Para(Field("PAGE", "99")))),
            ("fC", FooterPart(Para(Text("A-") + Field("PAGE", "98")))));
        var front = Sect(Footer("default", "fA") + "<w:pgNumType w:fmt=\"lowerRoman\"/>");
        var main = Sect(Footer("default", "fB") + "<w:pgNumType w:start=\"1\"/>");
        var appendix = Sect(Footer("default", "fC") + "<w:pgNumType w:fmt=\"decimal\" w:start=\"1\"/>");
        var body = P("a") + PEnd("b", front, Break) + P("c") + PEnd("d", main, Break) + PEnd("e", appendix);
        var layout = Laid(Document(body, parts));
        Assert.Equal(5, layout.LaidOutPages);
        Assert.Equal(["i", "ii", "1", "2", "A-1"], Pages(layout).Select(LastLine).ToArray());
        Assert.Equal(["i", "ii", "1", "2", "1"], Pages(layout).Select(p => p.Number).ToArray());
    }

    [Fact]
    public void FieldWithoutAResultStillShowsItsValue()
    {
        var parts = Parts(("fA", FooterPart(Para("<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText> PAGE </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>"))));
        var layout = Laid(Document(P("a") + PEnd("b", Sect(Footer("default", "fA")), Break), parts));
        Assert.Equal("1", LastLine(layout.Page(0)));
        Assert.Equal("2", LastLine(layout.Page(1)));
    }

    [Fact]
    public void NumPagesAndSectionPages()
    {
        var parts = Parts(("fX", FooterPart(Para(
            Text("p") + Field("PAGE", "0") + Text(" of ") + Field("NUMPAGES", "0") + Text(" s") + Field("SECTION", "0") + Text(" of ") + Field("SECTIONPAGES", "0")))));
        var s1 = Sect(Footer("default", "fX"));
        var body = P("a") + PEnd("b", s1, Break) + P("c") + PEnd("d", Sect(), Break) + PEnd("e", Sect());
        var layout = DocumentLayout.Create(Document(body, parts), Fonts, new LayoutOptions());

        // While the layout goes on the total is not known: the page says so instead of a wrong number, and the page is replaced when the layout is complete.
        layout.LayOutFirstPage();
        var provisional = layout.Page(0);
        Assert.Equal("p1 of … s1 of …", LastLine(provisional));

        Finish(layout);
        Assert.True(layout.IsComplete);
        var texts = Pages(layout).Select(LastLine).ToArray();
        Assert.Equal(["p1 of 5 s1 of 2", "p2 of 5 s1 of 2", "p3 of 5 s2 of 2", "p4 of 5 s2 of 2", "p5 of 5 s3 of 1"], texts);

        // The page object read earlier is a snapshot; the layout hands out the final one.
        Assert.Equal("p1 of … s1 of …", LastLine(provisional));
        Assert.NotSame(provisional, layout.Page(0));
        Assert.Equal(provisional.Items.Count, layout.Page(0).Items.Count);
        Assert.Equal(provisional.Spans.Length, layout.Page(0).Spans.Length);
    }

    private static Raffica.Write.Model.WordDocument CountedDocument(bool withTotal)
    {
        var footer = withTotal
            ? Text("p") + Field("PAGE", "0") + Text(" of ") + Field("NUMPAGES", "0")
            : Text("p") + Field("PAGE", "0");
        var parts = Parts(("fX", FooterPart(Para(footer))));
        return Document(Body("body", 5) + PEnd("six", Sect(Footer("default", "fX")), Break) + PEnd("seven", Sect(), Break), parts);
    }

    [Fact]
    public void ProvisionalPagesAreMarkedAndReplacedOnceWhenTheLayoutCompletes()
    {
        var layout = DocumentLayout.Create(CountedDocument(withTotal: true), Fonts, new LayoutOptions());
        layout.LayOutThrough(1);
        Assert.Equal(0, layout.Revision);
        var early = new[] { layout.Page(0), layout.Page(1) };
        Assert.All(early, p => Assert.True(p.IsProvisional));

        Finish(layout);
        Assert.True(layout.IsComplete);
        Assert.Equal(1, layout.Revision);
        var pages = Pages(layout);
        Assert.Equal(3, pages.Count);
        Assert.All(pages, p => Assert.False(p.IsProvisional));
        Assert.NotSame(early[0], pages[0]);
        Assert.NotSame(early[1], pages[1]);
        Assert.True(early[0].IsProvisional);
        Assert.Equal("p1 of …", LastLine(early[0]));
        Assert.Equal("p1 of 3", LastLine(pages[0]));
        Assert.Equal("p3 of 3", LastLine(pages[2]));

        // The body is the same objects and the same text; only the footer was made again.
        Assert.Same(early[0].Items[0], pages[0].Items[0]);
        Assert.Equal(early[0].Items.Count, pages[0].Items.Count);
        Assert.Equal(Lines(early[0])[..^1], Lines(pages[0])[..^1]);
        Assert.Equal(1, layout.Revision);
        Finish(layout);
        Assert.Equal(1, layout.Revision);
    }

    [Fact]
    public void WithoutATotalNoPageIsProvisionalAndTheRevisionNeverChanges()
    {
        var layout = DocumentLayout.Create(CountedDocument(withTotal: false), Fonts, new LayoutOptions());
        layout.LayOutThrough(0);
        Assert.False(layout.Page(0).IsProvisional);
        Finish(layout);
        Assert.Equal(0, layout.Revision);
        Assert.All(Pages(layout), p => Assert.False(p.IsProvisional));
        Assert.Equal("p3", LastLine(layout.Page(2)));
    }

    [Fact]
    public void NoPageBuilderIsKeptAfterItsPageIsMade()
    {
        var layout = DocumentLayout.Create(CountedDocument(withTotal: true), Fonts, new LayoutOptions());
        WeakReference? first = null;
        layout.PageFinished = builder =>
        {
            if (builder.Index == 0)
            {
                first = new WeakReference(builder);
            }
        };
        layout.LayOutThrough(1);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.NotNull(first);
        Assert.False(first.IsAlive);

        // What waits for the page count holds pages, not builders.
        var kept = typeof(PageFiller).GetNestedType("Revisable", System.Reflection.BindingFlags.NonPublic)!;
        Assert.DoesNotContain(kept.GetProperties(), p => p.PropertyType == typeof(PageBuilder));
        Assert.DoesNotContain(kept.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public), f => f.FieldType == typeof(PageBuilder));
        var list = typeof(PageFiller).GetField("_revisable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.Equal(kept, list.FieldType.GetGenericArguments()[0]);
    }

    [Fact]
    public void UnknownFieldShowsItsCachedResult()
    {
        var nested = "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText> MYFIELD </w:instrText></w:r>"
            + Field("PAGE", "7")
            + "<w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>cached-x</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";
        var parts = Parts(("hA", HeaderPart(
            Para(Field("TOC \\o \"1-3\"", "Contents 12"))
            + Para("<w:fldSimple w:instr=\" REF target \\h \"><w:r><w:t>see 4</w:t></w:r></w:fldSimple>")
            + Para(nested)
            + Para(Text("after")))));
        var layout = Laid(Document(P("body") + Sect(Header("default", "hA")), parts));
        Assert.Equal(["Contents 12", "see 4", "cached-x", "after"], Lines(layout.Page(0)).Take(4).ToArray());
    }

    [Fact]
    public void HeaderTallerThanTheMarginPushesTheBody()
    {
        var header = string.Concat(Enumerable.Range(1, 8).Select(i => Line($"head {i}")));
        var parts = Parts(("hA", HeaderPart(header)));
        var plain = Laid(Document(Body("body", 200) + Sect()));
        var tall = Laid(Document(Body("body", 200) + Sect(Header("default", "hA")), parts));
        var line = LineHeight();

        var plainBase = Baselines(plain.Page(0));
        var tallBase = Baselines(tall.Page(0));
        var headerBottom = 36f + (8 * line);
        Assert.InRange(headerBottom, 73f, 200f);
        Assert.InRange(tallBase[8] - plainBase[0], headerBottom - 72f - 0.5f, headerBottom - 72f + 0.5f);
        Assert.True(CountLines(tall.Page(0), "body") < CountLines(plain.Page(0), "body"));
        Assert.True(tallBase[7] < tallBase[8]);
    }

    [Fact]
    public void FooterTallerThanTheMarginPullsTheBodyUp()
    {
        var footer = string.Concat(Enumerable.Range(1, 8).Select(i => Line($"foot {i}")));
        var parts = Parts(("fA", FooterPart(footer)));
        var plain = Laid(Document(Body("body", 200) + Sect()));
        var tall = Laid(Document(Body("body", 200) + Sect(Footer("default", "fA")), parts));
        var footerTop = 792f - 36f - (8 * LineHeight());
        var page = tall.Page(0);
        var bodyCount = CountLines(page, "body");
        var bodyBaselines = Baselines(page).Take(bodyCount).ToList();
        Assert.True(bodyBaselines[^1] < footerTop);
        Assert.True(bodyCount < CountLines(plain.Page(0), "body"));
        Assert.Equal("foot 8", LastLine(page));
    }

    [Fact]
    public void ContinuousSectionKeepsOutOfATallFooter()
    {
        var footer = string.Concat(Enumerable.Range(1, 8).Select(i => Line($"foot {i}")));
        var parts = Parts(("fA", FooterPart(footer)));
        var first = Sect(Footer("default", "fA"));
        var second = Sect("<w:type w:val=\"continuous\"/>");
        var body = Body("one", 3) + PEnd("one 4", first) + Body("two", 120) + second;
        var layout = Laid(Document(body, parts));
        var footerTop = 792f - 36f - (8 * LineHeight());
        Assert.True(layout.LaidOutPages >= 2);
        foreach (var page in Pages(layout))
        {
            var bodyBaselines = Baselines(page).Take(CountLines(page, "one") + CountLines(page, "two")).ToList();
            Assert.NotEmpty(bodyBaselines);
            Assert.True(bodyBaselines[^1] < footerTop, $"page {page.Index}: {bodyBaselines[^1]} below {footerTop}");
        }
    }

    [Fact]
    public void HeadersAndFootersAreLaidOutOncePerPageNumberText()
    {
        var parts = Parts(
            ("hA", HeaderPart(Line("static header"))),
            ("fA", FooterPart(Para(Text("page ") + Field("PAGE", "0")))));
        var section = Sect(Header("default", "hA") + Footer("default", "fA"));
        var layout = Laid(Document(Body("body", 900) + section, parts));
        var pages = layout.LaidOutPages;
        Assert.True(pages > 10);

        // One layout for the header (its text does not change), one per page for the footer.
        Assert.Equal(pages + 1, layout.ChromeLayouts);
        Assert.Equal("static header", FirstLine(layout.Page(pages - 1)));
    }

    [Fact]
    public void MissingPartsAndHostileInstructionsNeverThrow()
    {
        var parts = Parts(("hA", HeaderPart(Para(Field(string.Empty, "empty") + "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText> PAGE </w:instrText></w:r>"))));
        var section = Sect(Header("default", "hA") + Header("first", "missing") + Footer("default", "gone") + "<w:titlePg/>");
        var layout = Laid(Document(P("body") + section, parts));
        Assert.Equal(1, layout.LaidOutPages);
        Assert.Contains("body", layout.Page(0).Text, StringComparison.Ordinal);
    }
}
