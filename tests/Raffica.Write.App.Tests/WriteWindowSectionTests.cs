using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Shell.Frame;
using Raffica.Write;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.WriteApp.Tests.Commands;
using Xunit;

namespace Raffica.WriteApp.Tests;

/// <summary>The cards and the status line of the section commands in the window (milestone 4.4b, Task 12): the real cards end to end, a card answered late, a command during a save and the shared footer.</summary>
public sealed class WriteWindowSectionTests
{
    private static readonly XNamespace Xw = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private const string PageBreak = "<w:pPr><w:pageBreakBefore/></w:pPr>";

    private static string Text(string text) => $"<w:r><w:t>{text}</w:t></w:r>";

    private static string EndsSection(string text, string cols = "") =>
        $"<w:pPr><w:sectPr><w:type w:val=\"nextPage\"/>{cols}</w:sectPr></w:pPr>{Text(text)}";

    private static CommandDefinition Command(WriteWindow window, string id) =>
        window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id);

    private static async Task<(WriteWindow Window, WriteTab Tab, EditSession Session)> OpenAsync(WindowFixture fixture, params string[] paragraphs)
    {
        var path = StyledDocx.Write(fixture.Folder, "doc.docx", StyledDocx.Styles, paragraphs);
        var window = fixture.Show();
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(path)).Tab;
        tab.Runner.LeaseWaitTimeout = TimeSpan.FromSeconds(60);
        tab.Runner.Start();
        await WindowFixture.WaitAsync(() => tab.Runner.FullModelReady || tab.Runner.IsFaulted);
        Assert.True(tab.Runner.FullModelReady);
        await WindowFixture.WaitAsync(() => window.ActiveView is { IsLayoutComplete: true });
        var session = tab.EnsureEditor();
        _ = session.Selection;
        return (window, tab, session);
    }

    private static Paragraph P(WriteTab tab, int index) => (Paragraph)tab.Docx.Model.Body.Blocks[index];

    private static TextPosition Caret(EditSession session, WriteTab tab, int paragraph, int offset = 0)
    {
        var at = new TextPosition(StoryRef.Body, P(tab, paragraph), offset);
        session.SetSelection(new TextRange(at, at));
        return at;
    }

    private static void Settle(WriteWindow window)
    {
        window.FormatRefresh?.Flush();
        window.ViewStatesRefresh?.Flush();
        WindowFixture.Pump();
        window.Ribbon.RefreshCommandStates();
        WindowFixture.Pump();
    }

    private static IReadOnlyList<SectionInfo> Sections(WriteTab tab) => SectionList.Of(tab.Docx.Model);

    private static XElement? NumType(WriteTab tab, int section) => Sections(tab)[section].Properties.Get(Xw + "pgNumType");

    private static string? FooterReference(WriteTab tab, int section) =>
        Sections(tab)[section].Properties.HeaderFooterReferences.FirstOrDefault(r => r.Kind == HeaderFooterKind.Footer && r.Type == HeaderFooterType.Default)?.RelationshipId;

    private static Button DialogButton(WriteWindow window, string name) =>
        window.Host.ActiveDialog!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == name);

    private static T DialogField<T>(WriteWindow window, string name)
        where T : Control =>
        window.Host.ActiveDialog!.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static void Press(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WindowFixture.Pump();
    }

    private static int GlyphRuns(DocumentLayout layout, int page)
    {
        Assert.True(layout.TryGetPage(page, out var laid));
        return laid!.Items.OfType<GlyphRunItem>().Count();
    }

    [AvaloniaFact]
    public async Task TheOneCommandMakesThePageThreeNumberAndUndoRestores()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, Text("one"), PageBreak + Text("two"), PageBreak + Text("three"));
        var layout = tab.Runner.PageLayout;
        Assert.Equal(3, layout.LaidOutPages);
        Caret(session, tab, 2);
        Settle(window);
        var before = (Sections: Sections(tab).Count, Revision: tab.Docx.Model.Edits.Revision, Runs: GlyphRuns(layout, 2));

        // The real card opens (nothing is applied yet); the caret's block is the first of page 3, so the card says nothing about a new page.
        Command(window, WriteCommandIds.NumberPagesFrom).Run!();
        WindowFixture.Pump();
        Assert.NotNull(window.Host.ActiveDialog);
        Assert.Equal(WriteStrings.NumPagesTitle, window.Host.ActiveDialog!.Title);
        Assert.Equal("1", DialogField<Avalonia.Controls.TextBox>(window, "PART_Start").Text);
        Assert.DoesNotContain(WriteStrings.NumPagesPageStartLine, WindowFixture.CardTexts(window));
        Assert.Equal(before.Revision, tab.Docx.Model.Edits.Revision);

        // OK is the one answer: start at 1, 1 2 3.
        Press(DialogButton(window, "PART_Ok"));
        await WindowFixture.WaitAsync(() => tab.Docx.Model.Edits.Revision != before.Revision);
        await WindowFixture.WaitAsync(() => window.ActiveView is { IsLayoutComplete: true });
        layout = tab.Runner.PageLayout;
        Assert.Null(window.Host.ActiveDialog);
        Assert.Equal(2, Sections(tab).Count);
        Assert.Equal("1", (string?)NumType(tab, 1)?.Attribute(Xw + "start"));

        // Page 3 prints "1" and pages 1 and 2 print nothing: the footer exists for the section of page 3 only.
        Assert.True(layout.TryGetPage(2, out var third));
        Assert.Equal("1", third!.Number);
        Assert.Null(FooterReference(tab, 0));
        var footer = FooterReference(tab, 1);
        Assert.NotNull(footer);
        Assert.Contains(tab.Docx.Model.HeadersFooters[footer!].Content.Blocks.OfType<Paragraph>(), p => ParagraphProjection.Build(p, ProjectionHost.Plain).Text.Length > 0);
        Assert.True(GlyphRuns(layout, 2) > before.Runs);
        Assert.Equal(GlyphRuns(layout, 0), GlyphRuns(layout, 1));
        Assert.Equal(1, session.Editor.History.UndoCount);

        // Undo restores the document and its numbers; redo brings it back.
        Assert.True(window.RunCommand(WriteCommandIds.Undo));
        WindowFixture.Pump();
        await WindowFixture.WaitAsync(() => window.ActiveView is { IsLayoutComplete: true });
        layout = tab.Runner.PageLayout;
        Assert.Equal(before.Sections, Sections(tab).Count);
        Assert.True(layout.TryGetPage(2, out var restored));
        Assert.Equal("3", restored!.Number);
        Assert.Equal(before.Runs, GlyphRuns(layout, 2));
        Assert.True(window.RunCommand(WriteCommandIds.Redo));
        WindowFixture.Pump();
        await WindowFixture.WaitAsync(() => window.ActiveView is { IsLayoutComplete: true });
        Assert.True(tab.Runner.PageLayout.TryGetPage(2, out var again));
        Assert.Equal("1", again!.Number);
    }

    [AvaloniaFact]
    public async Task TheCardAsksTheLayoutWhetherTheCaretIsMidPageAndNamesTheFollowingSections()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, EndsSection("a"), EndsSection("b"), Text("c"), Text("d"));
        var cards = new ScriptedCards();
        window.SectionCards = cards;

        // The first block of a page: no "new page" line; the second block of the last page: the line; both with the sections that follow.
        Caret(session, tab, 2);
        Settle(window);
        var view = window.ActiveView!;
        Assert.True(window.RunCommand(WriteCommandIds.NumberPagesFrom));
        Assert.Equal(1, cards.NumberingAsked);
        Assert.False(cards.LastNumbering!.StartsNewPage);
        Assert.Equal(0, cards.LastNumbering.Facts.FollowingSections);

        Caret(session, tab, 3);
        Settle(window);
        Assert.True(window.RunCommand(WriteCommandIds.NumberPagesFrom));
        Assert.True(cards.LastNumbering!.StartsNewPage);
        Assert.True(view.CaretBlockStartsNewPage(cards.LastNumbering.Facts));

        // The caret in the first section: two sections follow.
        Caret(session, tab, 0);
        Settle(window);
        Assert.True(window.RunCommand(WriteCommandIds.NumberPagesFrom));
        Assert.Equal(2, cards.LastNumbering!.Facts.FollowingSections);
        Assert.True(cards.LastNumbering.Facts.AtSectionStart);
        Assert.False(cards.LastNumbering.StartsNewPage);
    }

    [AvaloniaFact]
    public async Task ThePageSetupCardGetsTheRegionUnitsAndTheColumnsOfTheSection()
    {
        using var fixture = new WindowFixture();
        const string Unequal = "<w:cols w:num=\"2\" w:equalWidth=\"0\"><w:col w:w=\"3000\" w:space=\"400\"/><w:col w:w=\"6000\"/></w:cols>";
        var (window, tab, session) = await OpenAsync(fixture, EndsSection("a", Unequal), Text("b"));
        var cards = new ScriptedCards();
        window.SectionCards = cards;
        window.ActiveView!.MarginsInInches = () => true;

        Caret(session, tab, 0);
        Settle(window);
        Assert.True(window.RunCommand(WriteCommandIds.PageSetup));
        Assert.True(cards.LastSetup!.ColumnsUnequal);
        Assert.True(cards.LastSetup.Inches);
        Assert.Equal(2, cards.LastSetup.Current.Columns);
        Assert.Equal(2, cards.LastSetup.SectionCount);
        Assert.Equal(ApplyScope.ThisSection, cards.LastSetup.DefaultScope);

        window.ActiveView.MarginsInInches = () => false;
        Caret(session, tab, 1);
        Settle(window);
        Assert.True(window.RunCommand(WriteCommandIds.PageSetup));
        Assert.False(cards.LastSetup.ColumnsUnequal);
        Assert.False(cards.LastSetup.Inches);
    }

    [AvaloniaFact]
    public async Task ANumberCommandWhileSavingIsQueuedAndReplayed()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, Text("one"), PageBreak + Text("two"));
        var cards = new ScriptedCards { NumberingGate = new TaskCompletionSource<PageNumbering?>() };
        window.SectionCards = cards;
        Caret(session, tab, 1);
        Settle(window);
        var revision = tab.Docx.Model.Edits.Revision;
        Assert.True(window.RunCommand(WriteCommandIds.NumberPagesFrom));

        // The card is open when a save starts; the answer comes during the save: it is queued, nothing changes until the save ends.
        var save = session.BeginSave();
        cards.NumberingGate.SetResult(new PageNumbering(5, PageNumberFormat.UpperRoman, string.Empty));
        WindowFixture.Pump();
        Assert.Equal(1, session.PendingCount);
        Assert.Single(Sections(tab));
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);

        save.Dispose();
        WindowFixture.Pump();
        Assert.Equal(0, session.PendingCount);
        Assert.Equal(2, Sections(tab).Count);
        Assert.Equal("5", (string?)NumType(tab, 1)?.Attribute(Xw + "start"));
        Assert.Equal("upperRoman", (string?)NumType(tab, 1)?.Attribute(Xw + "fmt"));
        Assert.Equal(1, session.Editor.History.UndoCount);
    }

    [AvaloniaFact]
    public async Task ACardAnsweredAfterTheDocumentChangedIsRefused()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, Text("one"), PageBreak + Text("two"));
        var cards = new ScriptedCards { NumberingGate = new TaskCompletionSource<PageNumbering?>(), PageSetupGate = new TaskCompletionSource<PageSetupRequest?>() };
        window.SectionCards = cards;
        Caret(session, tab, 1);
        Settle(window);

        // The numbering card is open; the document changes (an edit); the answer comes: refused with a note, nothing applied.
        Assert.True(window.RunCommand(WriteCommandIds.NumberPagesFrom));
        Assert.NotNull(window.ActiveView!.ApplyOperation(ed => ed.SetLeftRightPages(true, Caret(session, tab, 1))));
        var changed = tab.Docx.Model.Edits.Revision;
        cards.NumberingGate.SetResult(new PageNumbering(3, PageNumberFormat.Arabic, string.Empty));
        WindowFixture.Pump();
        Assert.Single(Sections(tab));
        Assert.Null(NumType(tab, 0));
        Assert.Equal(changed, tab.Docx.Model.Edits.Revision);
        Assert.Equal(WriteStrings.NoteCardStale, window.Status.Hint);

        // The same for the page setup card, and for an undo while it is open.
        window.Status.Hint = null;
        Assert.True(window.RunCommand(WriteCommandIds.PageSetup));
        Assert.True(window.RunCommand(WriteCommandIds.Undo));
        WindowFixture.Pump();
        var undone = tab.Docx.Model.Edits.Revision;
        var setup = SectionPageSetup.Read(Sections(tab)[0].Properties);
        cards.PageSetupGate.SetResult(new PageSetupRequest(new PageSetupChange(setup with { Columns = 3 }, PageSetupParts.Columns), ApplyScope.WholeDocument));
        WindowFixture.Pump();
        Assert.Equal(1, SectionPageSetup.Read(Sections(tab)[0].Properties).Columns);
        Assert.Equal(undone, tab.Docx.Model.Edits.Revision);
        Assert.Equal(WriteStrings.NoteCardStale, window.Status.Hint);

        // A card answered while the document is as it was runs.
        var live = new ScriptedCards { PageSetupGate = new TaskCompletionSource<PageSetupRequest?>() };
        window.SectionCards = live;
        Assert.True(window.RunCommand(WriteCommandIds.PageSetup));
        live.PageSetupGate.SetResult(new PageSetupRequest(new PageSetupChange(setup with { Columns = 3 }, PageSetupParts.Columns), ApplyScope.WholeDocument));
        WindowFixture.Pump();
        Assert.Equal(3, SectionPageSetup.Read(Sections(tab)[0].Properties).Columns);
    }

    [AvaloniaFact]
    public async Task ThePageNumberStepsKeepTheBodyCaretAcrossUndoAndRedo()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, Text("one"), Text("two"));
        var caret = Caret(session, tab, 1, 2);
        Settle(window);

        // The page number goes into the footer, the caret stays where it was in the body, also across undo and redo.
        Assert.True(window.RunCommand(WriteCommandIds.InsertPageNumber, "footer:center:plain"));
        WindowFixture.Pump();
        Assert.Equal(1, session.Editor.History.UndoCount);
        AssertCaretIn(window, caret);
        Assert.True(window.RunCommand(WriteCommandIds.Undo));
        WindowFixture.Pump();
        AssertCaretIn(window, caret);
        Assert.True(window.RunCommand(WriteCommandIds.Redo));
        WindowFixture.Pump();
        AssertCaretIn(window, caret);
        Assert.Null(window.ActiveView!.ActiveStory);
    }

    private static void AssertCaretIn(WriteWindow window, TextPosition expected)
    {
        var caret = window.ActiveView!.Caret!.Value;
        Assert.Equal(StoryKind.Body, caret.Story.Kind);
        Assert.Equal(expected.Offset, caret.Offset);
        Assert.Equal(ParagraphProjection.Build(expected.Paragraph, ProjectionHost.Plain).Text, ParagraphProjection.Build(caret.Paragraph, ProjectionHost.Plain).Text);
    }

    [AvaloniaFact]
    public async Task APageNumberInAFooterOtherSectionsShareIsSaidInTheStatusLine()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, EndsSection("a"), EndsSection("b"), EndsSection("c"), Text("d"));
        Caret(session, tab, 1);
        Settle(window);

        // The footer made in section 2 is inherited by sections 3 and 4: the note names the three.
        Assert.True(window.RunCommand(WriteCommandIds.InsertPageNumber, "footer:center:plain"));
        WindowFixture.Pump();
        Assert.Equal("The footer is shared by sections 2 to 4: the page number appears in all of them.", window.Status.Hint);

        // A footer of the only section is shared with nobody: no note.
        using var second = new WindowFixture();
        var (other, otherTab, otherSession) = await OpenAsync(second, Text("one"), Text("two"));
        Caret(otherSession, otherTab, 1);
        Assert.True(other.RunCommand(WriteCommandIds.InsertPageNumber, "footer:center:plain"));
        WindowFixture.Pump();
        Assert.True(string.IsNullOrEmpty(other.Status.Hint));
    }

    [AvaloniaFact]
    public async Task TheStatusLineNamesTheSharedFooter()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, EndsSection("a"), EndsSection("b"), EndsSection("c"), Text("d"));
        Assert.Equal(4, Sections(tab).Count);
        Caret(session, tab, 1);
        Settle(window);

        // The footer is made in section 2 (counted from 1); sections 3 and 4 inherit it, section 1 has none.
        Assert.True(window.RunCommand(WriteCommandIds.InsertPageNumber, "footer:center:plain"));
        WindowFixture.Pump();
        await WindowFixture.WaitAsync(() => window.ActiveView is { IsLayoutComplete: true });
        Assert.Null(FooterReference(tab, 0));
        Assert.NotNull(FooterReference(tab, 1));
        Assert.Null(FooterReference(tab, 2));
        Assert.Empty(window.Status.Count ?? string.Empty);

        // A double click in the footer of page 2 enters it, and the status line says whose it is and with whom it is shared.
        var view = window.ActiveView!;
        view.SetZoom(0.4);
        WindowFixture.Pump();
        view.ScrollToPage(1);
        WindowFixture.Pump();
        await WindowFixture.WaitAsync(() => view.PageBounds(1) is not null);
        Assert.True(view.Runner.PageLayout.TryGetPage(1, out var page));
        var bounds = view.PageBounds(1)!.Value;
        var scale = bounds.Height / page!.Height;
        var inView = new Point(bounds.X + (bounds.Width / 2), bounds.Y + bounds.Height - (41 * scale));
        var point = view.TranslatePoint(inView, window)!.Value;
        for (var i = 0; i < 2; i++)
        {
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        }

        WindowFixture.Pump();
        Assert.Equal(StoryKind.HeaderFooter, view.ActiveStory?.Kind);
        Assert.Equal("Footer, section 2, shared with sections 3 and 4", window.Status.Count);
        Assert.Equal("Footer, section 2, shared with sections 3 and 4", WriteWindow.StoryStatusText(view));

        // Esc leaves it and the words go.
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        WindowFixture.Pump();
        Assert.Null(view.ActiveStory);
        Assert.True(string.IsNullOrEmpty(window.Status.Count));
    }


    [AvaloniaTheory]
    [InlineData(new int[0], "")]
    [InlineData(new[] { 3 }, "3")]
    [InlineData(new[] { 3, 4 }, "3 and 4")]
    [InlineData(new[] { 3, 4, 5 }, "3 to 5")]
    [InlineData(new[] { 3, 5 }, "3 and 5")]
    [InlineData(new[] { 2, 3, 4, 6 }, "2 to 4 and 6")]
    [InlineData(new[] { 1, 3, 4, 6 }, "1, 3, 4 and 6")]
    [InlineData(new[] { 5, 3, 4, 3 }, "3 to 5")]
    public void SectionsAreNamedByRunsAndLists(int[] sections, string expected) =>
        Assert.Equal(expected, SectionNumberList.Describe(sections));

    private sealed class ScriptedCards : ISectionCards
    {
        public TaskCompletionSource<PageNumbering?>? NumberingGate { get; set; }

        public TaskCompletionSource<PageSetupRequest?>? PageSetupGate { get; set; }

        public NumberPagesContext? LastNumbering { get; private set; }

        public PageSetupContext? LastSetup { get; private set; }

        public int NumberingAsked { get; private set; }

        public Task<PageNumbering?> AskNumberingAsync(NumberPagesContext context)
        {
            NumberingAsked++;
            LastNumbering = context;
            return NumberingGate?.Task ?? Task.FromResult<PageNumbering?>(null);
        }

        public Task<PageSetupRequest?> AskPageSetupAsync(PageSetupContext context)
        {
            LastSetup = context;
            return PageSetupGate?.Task ?? Task.FromResult<PageSetupRequest?>(null);
        }
    }
}
