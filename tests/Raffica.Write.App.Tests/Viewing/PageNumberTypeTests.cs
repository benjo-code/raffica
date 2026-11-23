using Avalonia.Headless.XUnit;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;
using Raffica.WriteApp.Tests.Commands;
using Xunit;

namespace Raffica.WriteApp.Tests.Viewing;

/// <summary>
/// Insert page number takes the type of the part and the cached results from the layout (milestone 4.4b, fix round of Task 11): the page the caret is on says which header and footer type it used
/// (<see cref="LaidOutPage.ChromeType"/>) and what number it prints, and the command writes into that part; nothing is re-derived from the text of a page number.
/// </summary>
public sealed class PageNumberTypeTests : IDisposable
{
    private static readonly System.Xml.Linq.XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-write-pagenumber-type-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // The system cleans the temp folder.
        }
    }

    // A paragraph that ends a section: its w:sectPr holds the start type and the given children (in schema order: numbering before titlePg).
    private static string Ends(string type = "nextPage", string numbering = "", string titlePg = "") =>
        $"<w:pPr><w:sectPr><w:type w:val=\"{type}\"/>{numbering}{titlePg}</w:sectPr></w:pPr>";

    private static string Numbering(int start, string? format = null) =>
        $"<w:pgNumType w:start=\"{start}\"" + (format is null ? string.Empty : $" w:fmt=\"{format}\"") + "/>";

    private const string TitlePg = "<w:titlePg/>";

    private static string Text(string text) => $"<w:r><w:t>{text}</w:t></w:r>";

    private async Task<ViewHarness> OpenAsync(WriteView mode, params string[] paragraphs)
    {
        var path = StyledDocx.Write(_folder, Guid.NewGuid().ToString("N") + ".docx", StyledDocx.Styles, paragraphs);
        var h = new ViewHarness(path, 900, 1400, mode);
        h.View.Runner.LeaseWaitTimeout = TimeSpan.FromSeconds(60);
        h.View.Runner.Start();
        await ViewHarness.WaitAsync(() => h.View.Runner.FullModelReady || h.View.Runner.IsFaulted, 30000);
        Assert.True(h.View.Runner.FullModelReady);
        if (mode == WriteView.Page)
        {
            await ViewHarness.WaitAsync(() => h.View.Runner.PageLayout.IsComplete, 30000);
        }

        return h;
    }

    private static Task<ViewHarness> OpenPagesAsync(PageNumberTypeTests self, params string[] paragraphs) => self.OpenAsync(WriteView.Page, paragraphs);

    private static TextPosition Place(ViewHarness h, int paragraph)
    {
        var session = h.View.Tab.EnsureEditor();
        _ = session.Selection;
        var at = new TextPosition(StoryRef.Body, (Paragraph)h.View.Tab.Docx.Model.Body.Blocks[paragraph], 0);
        session.SetSelection(new TextRange(at, at));
        h.View.Focus();
        ViewHarness.Pump();
        return at;
    }

    private static HeaderFooterReference? Footer(ViewHarness h, int section, HeaderFooterType type) =>
        SectionList.Of(h.View.Tab.Docx.Model)[section].Properties.HeaderFooterReferences.FirstOrDefault(r => r.Kind == HeaderFooterKind.Footer && r.Type == type);

    private static string FooterText(ViewHarness h, HeaderFooterReference reference) =>
        string.Concat(h.View.Tab.Docx.Model.HeadersFooters[reference.RelationshipId].Content.Blocks.OfType<Paragraph>().Select(p => ParagraphProjection.Build(p, ProjectionHost.Plain).Text));

    [AvaloniaFact]
    public async Task ASectionWithADifferentFirstPageGetsTheNumberInItsFirstPageFooterOnItsFirstPage()
    {
        // Section 0 has a different first page and one page; the caret is on that page: the First footer of section 0 gets the number, the default one is not made.
        using var h = await OpenPagesAsync(this, Ends(titlePg: TitlePg) + Text("one"), Text("two"));
        var caret = Place(h, 0);
        var (type, number, total) = h.View.PageNumberContext(caret);
        Assert.Equal((HeaderFooterType.First, "1", "2"), (type, number, total));

        Assert.True(h.View.RunSectionCommand(WriteCommandIds.InsertPageNumber, "footer:center:plain"));
        ViewHarness.Pump();
        Assert.NotNull(Footer(h, 0, HeaderFooterType.First));
        Assert.Null(Footer(h, 0, HeaderFooterType.Default));
        Assert.Contains("1", FooterText(h, Footer(h, 0, HeaderFooterType.First)!));

        // The page after it (section 1, no different first page) takes the default part.
        var second = h.View.PageNumberContext(Place(h, 1));
        Assert.Equal((HeaderFooterType.Default, "2", "2"), second);
    }

    [AvaloniaFact]
    public async Task OnAnEvenPageAfterARestartTheEvenPartIsUsedByTheNumberNotThePhysicalIndex()
    {
        // Left and right pages are on. Page 0 is number 1, page 1 restarts at 3 (odd although it is the second page), page 2 restarts at 4 (even although it is the third page) and page 3 is "vi" in roman.
        using var h = await OpenPagesAsync(
            this,
            Ends() + Text("a"),
            Ends(numbering: Numbering(3)) + Text("b"),
            Ends(numbering: Numbering(4)) + Text("c"),
            Text("d"));
        Assert.NotNull(h.View.ApplyOperation(ed => ed.SetLeftRightPages(true, Place(h, 0))));
        ViewHarness.Pump();
        Assert.True(h.View.Tab.Docx.Model.Settings!.EvenAndOddHeaders);

        Assert.Equal((HeaderFooterType.Default, "1", "4"), h.View.PageNumberContext(Place(h, 0)));
        Assert.Equal((HeaderFooterType.Default, "3", "4"), h.View.PageNumberContext(Place(h, 1)));
        var even = h.View.PageNumberContext(Place(h, 2));
        Assert.Equal((HeaderFooterType.Even, "4", "4"), even);

        // The command writes the number into the Even part of the section the caret is in, and the layout agrees with the page it made it for (the page says its own type).
        Place(h, 2);
        Assert.True(h.View.RunSectionCommand(WriteCommandIds.InsertPageNumber, "footer:right:slash"));
        ViewHarness.Pump();
        Assert.NotNull(Footer(h, 2, HeaderFooterType.Even));
        Assert.Contains("4 / 4", FooterText(h, Footer(h, 2, HeaderFooterType.Even)!));
        await ViewHarness.WaitAsync(() => h.View.Runner.PageLayout.IsComplete, 30000);
        Assert.True(h.View.Runner.PageLayout.TryGetPage(2, out var page) && page is not null);
        Assert.Equal(HeaderFooterType.Even, page!.ChromeType);
        Assert.Equal(4, page.NumberValue);
    }

    [AvaloniaFact]
    public async Task WithRomanNumbersTheEvenPartFollowsTheValueOfTheNumber()
    {
        using var h = await OpenPagesAsync(
            this,
            Ends() + Text("a"),
            Ends(numbering: Numbering(6, "lowerRoman")) + Text("b"),
            Ends(numbering: Numbering(7, "lowerRoman")) + Text("c"),
            Text("d"));
        Assert.NotNull(h.View.ApplyOperation(ed => ed.SetLeftRightPages(true, Place(h, 0))));
        ViewHarness.Pump();

        // "vi" is 6: even. "vii" is 7: odd. The page text says which, the layout says what it did with it.
        Assert.Equal((HeaderFooterType.Even, "vi", "4"), h.View.PageNumberContext(Place(h, 1)));
        Assert.Equal((HeaderFooterType.Default, "vii", "4"), h.View.PageNumberContext(Place(h, 2)));
        Place(h, 1);
        Assert.True(h.View.RunSectionCommand(WriteCommandIds.InsertPageNumber, "footer:center:page"));
        ViewHarness.Pump();
        Assert.Equal(WriteStrings.PageNumberWordPage + " vi", FooterText(h, Footer(h, 1, HeaderFooterType.Even)!));
    }

    [AvaloniaFact]
    public async Task AContinuousSectionThatStartsInTheMiddleOfAPageUsesWhatTheLayoutUsedForThatPage()
    {
        // Section 1 starts in the middle of page 0 (continuous) and has a different first page; the page began in section 0, which has none: the layout shows the default parts on that page,
        // and the number goes into section 1's default footer, not into a first-page footer the page never shows.
        using var h = await OpenPagesAsync(this, Ends() + Text("one"), Ends("continuous", titlePg: TitlePg) + Text("two"), Text("three"));
        var context = h.View.PageNumberContext(Place(h, 1));
        Assert.True(h.View.Runner.PageLayout.TryGetPage(0, out var first) && first is not null);
        Assert.Equal(first!.ChromeType ?? HeaderFooterType.Default, context.Type);
        Assert.Equal(HeaderFooterType.Default, context.Type);
        Assert.Equal(first.Number, context.Number);

        Assert.True(h.View.RunSectionCommand(WriteCommandIds.InsertPageNumber, "footer:center:plain"));
        ViewHarness.Pump();
        Assert.NotNull(Footer(h, 1, HeaderFooterType.Default));
        Assert.Null(Footer(h, 1, HeaderFooterType.First));
    }

    [AvaloniaFact]
    public async Task TheWritingViewAlwaysUsesTheDefaultPartAndTheStripNumber()
    {
        using var h = await OpenAsync(WriteView.Writing, Ends(titlePg: TitlePg) + Text("one"), Text("two"));
        var (type, number, total) = h.View.PageNumberContext(Place(h, 0));
        Assert.Equal(HeaderFooterType.Default, type);
        Assert.Equal("1", number);
        Assert.True(int.Parse(total, System.Globalization.CultureInfo.InvariantCulture) >= 1);

        // Although section 0 has a different first page, the number goes into the default footer (a strip has no first page).
        Assert.True(h.View.RunSectionCommand(WriteCommandIds.InsertPageNumber, "footer:center:plain"));
        ViewHarness.Pump();
        Assert.NotNull(Footer(h, 0, HeaderFooterType.Default));
        Assert.Null(Footer(h, 0, HeaderFooterType.First));
    }

    [AvaloniaFact]
    public async Task TheCachedResultsAreThePrintedNumberAfterARestartAndTheTotal()
    {
        // Page 1 is the second page and prints "5": the field results of the footer say "Page 5 of 2".
        using (var h = await OpenPagesAsync(this, Ends() + Text("one"), Ends(numbering: Numbering(5)) + Text("two")))
        {
            Place(h, 1);
            Assert.True(h.View.RunSectionCommand(WriteCommandIds.InsertPageNumber, "footer:center:pageOfTotal"));
            ViewHarness.Pump();
            var footer = Footer(h, 1, HeaderFooterType.Default);
            Assert.NotNull(footer);
            Assert.Equal(WriteStrings.PageNumberWordPage + " 5 " + WriteStrings.PageNumberWordOf + " 2", FooterText(h, footer!));
        }

        // The lower roman first page of a restart prints "i".
        using var roman = await OpenPagesAsync(this, Ends() + Text("one"), Ends(numbering: Numbering(1, "lowerRoman")) + Text("two"));
        Place(roman, 1);
        Assert.True(roman.View.RunSectionCommand(WriteCommandIds.InsertPageNumber, "footer:left:slash"));
        ViewHarness.Pump();
        Assert.Equal("i / 2", FooterText(roman, Footer(roman, 1, HeaderFooterType.Default)!));
    }

    [AvaloniaFact]
    public async Task ACaretWithNoPageInTheLayoutGivesTheDefaultTypeAndOne()
    {
        // A caret outside the body (a note here) has no page to ask: the default type and "1", as the comment of the method says.
        using var h = await OpenPagesAsync(this, Text("one"));
        var session = h.View.Tab.EnsureEditor();
        _ = session.Selection;
        var story = new TextPosition(new StoryRef(StoryKind.Note, "1"), (Paragraph)h.View.Tab.Docx.Model.Body.Blocks[0], 0);
        Assert.Equal((HeaderFooterType.Default, "1", "1"), h.View.PageNumberContext(story));
    }
}
