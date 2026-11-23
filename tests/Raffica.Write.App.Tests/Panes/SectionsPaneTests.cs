using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Write;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Panes;
using Raffica.Write.Viewing;
using Raffica.WriteApp.Tests.Commands;
using Xunit;

namespace Raffica.WriteApp.Tests.Panes;

/// <summary>The Sections pane (milestone 4.4b, Task 13): a row acts on the section it names, a stale row does nothing, the pane shares the slot with the formatting pane, marks the active section, reads nothing per key and joins the F6 ring.</summary>
public sealed class SectionsPaneTests
{
    private static readonly XNamespace Xw = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static string Text(string text) => $"<w:r><w:t>{text}</w:t></w:r>";

    private static string EndsSection(string text, string properties = "") => $"<w:pPr><w:sectPr><w:type w:val=\"nextPage\"/>{properties}</w:sectPr></w:pPr>{Text(text)}";

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

    private static TextPosition Caret(EditSession session, WriteTab tab, int paragraph)
    {
        var at = new TextPosition(StoryRef.Body, P(tab, paragraph), 0);
        session.SetSelection(new TextRange(at, at));
        return at;
    }

    private static IReadOnlyList<SectionInfo> Sections(WriteTab tab) => SectionList.Of(tab.Docx.Model);

    private static string? FooterReference(WriteTab tab, int section) =>
        Sections(tab)[section].Properties.HeaderFooterReferences.FirstOrDefault(r => r.Kind == HeaderFooterKind.Footer && r.Type == HeaderFooterType.Default)?.RelationshipId;

    private static bool FirstPageDifferent(WriteTab tab, int section) => Sections(tab)[section].Properties.GetFlag(Xw + "titlePg");

    private static void Press(Control control)
    {
        control.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WindowFixture.Pump();
    }

    private static SectionsPane Open(WriteWindow window, WriteTab tab)
    {
        if (!window.IsSectionsOpen)
        {
            Assert.True(window.RunCommand(WriteCommandIds.SectionsPane));
        }

        WindowFixture.Pump();
        var pane = window.SectionsOf(tab)!;
        Settle(window, pane);
        return pane;
    }

    private static void Settle(WriteWindow window, SectionsPane pane)
    {
        pane.Refresh.Flush();
        WindowFixture.Pump();
        window.UpdateLayout();
        WindowFixture.Pump();
    }

    private static async Task EnterFooterAsync(WriteWindow window, PageView view, int pageIndex)
    {
        view.SetZoom(0.4);
        WindowFixture.Pump();
        view.ScrollToPage(pageIndex);
        WindowFixture.Pump();
        await WindowFixture.WaitAsync(() => view.PageBounds(pageIndex) is not null);
        Assert.True(view.Runner.PageLayout.TryGetPage(pageIndex, out var page));
        var bounds = view.PageBounds(pageIndex)!.Value;
        var scale = bounds.Height / page!.Height;
        var inView = new Point(bounds.X + (bounds.Width / 2), bounds.Y + bounds.Height - (41 * scale));
        var point = view.TranslatePoint(inView, window)!.Value;
        for (var i = 0; i < 2; i++)
        {
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
        }

        WindowFixture.Pump();
    }

    // A pane on its own is laid out 256 DIPs wide, as the slot of the window makes it.
    private static Window Host(SectionsPane pane)
    {
        var window = new Window { Width = 256, Height = 900, Content = pane };
        window.Show();
        window.UpdateLayout();
        WindowFixture.Pump();
        pane.Measure(new Size(256, double.PositiveInfinity));
        return window;
    }

    [AvaloniaFact]
    public async Task NothingOfThePaneExistsBeforeItIsOpened()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, EndsSection("a"), Text("b"));
        Caret(session, tab, 0);
        WindowFixture.Pump();
        Assert.Null(window.SectionsOf(tab));
        Assert.False(window.IsSectionsOpen);
        Assert.Null(window.Frame.RightPane);
        Assert.Null(window.FormattingHost);

        // The command exists, is a switch and needs a document only; its first use makes the pane and reads once.
        var open = Open(window, tab);
        Assert.Same(open, window.SectionsOf(tab));
        Assert.Equal(1, open.Source.Reads);
        Assert.Equal(2, open.View!.Rows.Count);
    }

    [AvaloniaFact]
    public async Task AToggleActsOnTheSectionOfItsRow()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, EndsSection("a"), EndsSection("b"), Text("c"));
        Caret(session, tab, 0);
        var pane = Open(window, tab);
        Assert.Equal(3, pane.Rows.Count);
        var third = Sections(tab)[2].Properties;

        // Section 2's footer becomes its own: one reference in section 2 and nowhere else; the row says so after the change.
        Press(pane.ControlOf("PART_S1_Footer")!);
        Assert.NotNull(FooterReference(tab, 1));
        Assert.Null(FooterReference(tab, 2));
        Settle(window, pane);
        Assert.True(pane.View!.Rows[1].FooterOwn);
        Assert.False(pane.View.Rows[2].FooterOwn);
        Assert.Equal(WriteStrings.SectionFooterOwn, ((CheckBox)pane.ControlOf("PART_S1_Footer")!).Content);

        // The same control again: back to the same as before (one undo step each).
        Press(pane.ControlOf("PART_S1_Footer")!);
        Assert.Null(FooterReference(tab, 1));

        // Different first page of section 3 only.
        Press(pane.ControlOf("PART_S2_First")!);
        Assert.True(FirstPageDifferent(tab, 2));
        Assert.False(FirstPageDifferent(tab, 0));
        Assert.False(FirstPageDifferent(tab, 1));
        Assert.Same(third, Sections(tab)[2].Properties);

        // The document row: left and right pages.
        Assert.False(tab.Docx.Model.Settings?.EvenAndOddHeaders ?? false);
        Press(pane.ControlOf("PART_LeftRight")!);
        Assert.True(tab.Docx.Model.Settings!.EvenAndOddHeaders);

        // The first section always has its own header and footer: its switches are disabled and say why.
        Assert.False(pane.ControlOf("PART_S0_Header")!.IsEnabled);
        Assert.Equal(WriteStrings.SectionFirstOwnTip, ToolTip.GetTip(pane.ControlOf("PART_S0_Header")!));

        // Remove marker of section 2 merges it into section 1 (one step).
        var steps = session.Editor.History.UndoCount;
        Press(pane.ControlOf("PART_S1_Remove")!);
        Assert.Equal(2, Sections(tab).Count);
        Assert.Equal(steps + 1, session.Editor.History.UndoCount);
        Assert.Null(pane.ControlOf("PART_S0_Remove"));
    }

    [AvaloniaFact]
    public async Task AStaleRowCannotActOnANewSelection()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, EndsSection("a"), EndsSection("b"), Text("c"));
        Caret(session, tab, 0);
        var pane = Open(window, tab);
        var staleFirst = pane.ControlOf("PART_S1_First")!;
        var staleLast = pane.ControlOf("PART_S2_First")!;

        // The document changes behind the pane (the refresh waits for typing to pause): section 2 is merged away, so the old row 3 is section 2 now and the old row 2 is gone.
        var view = window.ActiveView!;
        Assert.NotNull(view.ApplyOperation(ed => ed.RemoveSectionBreak(1)));
        Assert.True(pane.Refresh.IsPending);
        Assert.Equal(2, Sections(tab).Count);
        var revision = tab.Docx.Model.Edits.Revision;

        // Both old controls are still on screen; neither acts: the pane reads again first, finds that the row it belongs to is not there, and nothing changes.
        Press(staleLast);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.All(Enumerable.Range(0, 2), i => Assert.False(FirstPageDifferent(tab, i)));
        Assert.Equal(2, pane.View!.Rows.Count);

        Press(staleFirst);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.All(Enumerable.Range(0, 2), i => Assert.False(FirstPageDifferent(tab, i)));

        // The rows made now act on the sections they name.
        Press(pane.ControlOf("PART_S1_First")!);
        Assert.True(FirstPageDifferent(tab, 1));
        Assert.False(FirstPageDifferent(tab, 0));
    }

    [AvaloniaFact]
    public async Task OpeningThePaneClosesTheFormattingPane()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, EndsSection("a"), Text("b"));
        Caret(session, tab, 0);
        Assert.True(window.RunCommand(WriteCommandIds.FormattingPane));
        WindowFixture.Pump();
        var formatting = window.FormattingOf(tab)!;
        Assert.True(window.IsFormattingOpen);
        Assert.Same(formatting, window.FormattingHost!.Content);

        var sections = Open(window, tab);
        Assert.True(window.IsSectionsOpen);
        Assert.False(window.IsFormattingOpen);
        Assert.Same(sections, window.FormattingHost.Content);
        Assert.Equal(WriteStrings.CmdSections, window.FormattingHost.Title);
        Assert.True(window.FormattingHost.IsOpen);

        // The reverse: the formatting pane takes the slot back, the Sections pane lets go of the session and the layout.
        Assert.True(window.RunCommand(WriteCommandIds.FormattingPane));
        WindowFixture.Pump();
        Assert.True(window.IsFormattingOpen);
        Assert.False(window.IsSectionsOpen);
        Assert.Same(formatting, window.FormattingHost.Content);
        Assert.Equal(WriteStrings.CmdFormatting, window.FormattingHost.Title);
        var reads = sections.Source.Reads;
        sections.RequestRefresh();
        Assert.False(sections.Refresh.IsPending);
        Assert.Equal(reads, sections.Source.Reads);

        // The switch closes the slot when its own pane is the one on show.
        Assert.True(window.RunCommand(WriteCommandIds.SectionsPane));
        WindowFixture.Pump();
        Assert.True(window.IsSectionsOpen);
        Assert.True(window.RunCommand(WriteCommandIds.SectionsPane));
        WindowFixture.Pump();
        Assert.False(window.IsSectionsOpen);
        Assert.False(window.FormattingHost.IsOpen);
    }

    [AvaloniaFact]
    public async Task TheRowOfTheActiveFooterIsHighlighted()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, EndsSection("a"), EndsSection("b"), EndsSection("c"), Text("d"));
        Caret(session, tab, 1);
        Assert.True(window.RunCommand(WriteCommandIds.InsertPageNumber, "footer:center:plain"));
        WindowFixture.Pump();
        await WindowFixture.WaitAsync(() => window.ActiveView is { IsLayoutComplete: true });
        var view = window.ActiveView!;

        // A caret in the body marks the section it is in.
        var pane = Open(window, tab);
        Caret(session, tab, 2);
        Settle(window, pane);
        Assert.Equal([2], pane.Rows.Where(r => r.IsActive).Select(r => r.Row.Index));
        Caret(session, tab, 0);
        Settle(window, pane);
        Assert.Equal([0], pane.Rows.Where(r => r.IsActive).Select(r => r.Row.Index));

        // The footer of the third page is section 2's (shared with sections 3 and 4, the status line says so): that row is marked, by a state line as well as by its look.
        await EnterFooterAsync(window, view, 2);
        Assert.Equal(StoryKind.HeaderFooter, view.ActiveStory?.Kind);
        Assert.StartsWith("Footer, section 2", window.Status.Count, StringComparison.Ordinal);
        Settle(window, pane);
        var active = Assert.Single(pane.Rows, r => r.IsActive);
        Assert.Equal(1, active.Row.Index);
        var border = pane.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("active"));
        Assert.Contains(WriteStrings.SectionCurrent, AutomationProperties.GetName(border), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task TypingWithThePaneOpenAddsNoPerKeyRead()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, EndsSection("a"), Text("b"));
        Caret(session, tab, 1);
        var pane = Open(window, tab);
        var source = pane.Source.Reads;
        var refreshes = pane.RefreshCount;
        var facts = window.SectionFactReads;
        var stories = window.StoryStatusReads;

        for (var i = 0; i < 12; i++)
        {
            window.KeyTextInput("x");
            WindowFixture.Pump();
        }

        // The keys only asked for a refresh that waits for typing to pause: at most the one that came due meanwhile has read, the keys themselves read no section.
        Assert.True(pane.Source.Reads - source <= 1, "no read for each key");
        Assert.True(pane.RefreshCount - refreshes <= 1);
        Assert.Equal(facts, window.SectionFactReads);
        Assert.Equal(stories, window.StoryStatusReads);
        Settle(window, pane);
        Assert.True(pane.Source.Reads - source <= 2);
        Assert.True(pane.Source.Reads <= source + 2);
        Assert.False(pane.Refresh.IsPending);
    }

    [AvaloniaFact]
    public async Task ThePaneIsInTheF6Ring()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, EndsSection("a"), Text("b"));
        Caret(session, tab, 0);
        var pane = Open(window, tab);
        window.ActiveView!.Focus();
        WindowFixture.Pump();
        Assert.Same(pane, window.FormattingHost!.Content);
        Assert.True(window.FormattingHost.IsOpen);

        for (var i = 0; i < 3 && !window.FormattingHost.IsPaneFocused; i++)
        {
            window.KeyPressQwerty(PhysicalKey.F6, RawInputModifiers.None);
            WindowFixture.Pump();
        }

        Assert.True(window.FormattingHost.IsPaneFocused);
        Assert.True(window.FormattingHost.IsKeyboardFocusWithin);
        Assert.Equal(WriteStrings.CmdSections, window.FormattingHost.Title);
    }

    [AvaloniaFact]
    public async Task RemoveMarkerNamesWhatChangesWhenTheSetupsDiffer()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, EndsSection("a"), EndsSection("b"), EndsSection("c", "<w:cols w:num=\"2\" w:space=\"720\"/>"), Text("d"));
        Caret(session, tab, 0);
        var pane = Open(window, tab);

        // Sections 2 and 1 have the same setup: nothing to warn about. Section 3 (two columns) and section 4 (back to one) differ from the section before them: the row says what will change.
        var warnings = pane.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("sections-warn")).Select(t => t.Text).ToList();
        Assert.Equal(
        [
            WriteStrings.Fill(WriteStrings.SectionTakesOverFormat, 3, 2),
            WriteStrings.Fill(WriteStrings.SectionTakesOverFormat, 4, 3),
        ], warnings);
        Assert.Equal(WriteStrings.Fill(WriteStrings.SectionRemoveTipFormat, 2, 1), AutomationProperties.GetItemStatus(pane.ControlOf("PART_S1_Remove")!));
        Assert.Equal(WriteStrings.Fill(WriteStrings.SectionTakesOverFormat, 3, 2), AutomationProperties.GetItemStatus(pane.ControlOf("PART_S2_Remove")!));
        Assert.Null(pane.ControlOf("PART_S0_Remove"));

        // The merge does what the line said: section 4 takes section 3's two columns.
        Press(pane.ControlOf("PART_S3_Remove")!);
        Assert.Equal(3, Sections(tab).Count);
        Assert.Equal(2, SectionPageSetup.Read(Sections(tab)[2].Properties).Columns);
    }

    [AvaloniaFact]
    public async Task NumberFromHereAsksItsCardForTheSectionOfItsRow()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, EndsSection("a"), EndsSection("b"), Text("c"));
        Caret(session, tab, 0);
        var cards = new ScriptedCards { Answer = new PageNumbering(5, PageNumberFormat.LowerRoman, string.Empty) };
        window.SectionCards = cards;
        var pane = Open(window, tab);

        Press(pane.ControlOf("PART_S1_Number")!);
        await WindowFixture.WaitAsync(() => cards.Asked == 1);
        Assert.Equal(1, cards.Last!.Facts.Section);
        Assert.True(cards.Last.Facts.AtSectionStart);
        Assert.Equal(1, cards.Last.Facts.FollowingSections);
        await WindowFixture.WaitAsync(() => Sections(tab)[1].Properties.Get(Xw + "pgNumType") is not null);
        var numbering = Sections(tab)[1].Properties.Get(Xw + "pgNumType")!;
        Assert.Equal("5", numbering.Attribute(Xw + "start")!.Value);
        Assert.Equal(3, Sections(tab).Count);
        Assert.Null(Sections(tab)[0].Properties.Get(Xw + "pgNumType"));
    }

    [AvaloniaFact]
    public async Task AControlIsDisabledWithAReasonWhileTheDocumentCannotBeChanged()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, EndsSection("a"), Text("b"));
        Caret(session, tab, 0);
        var pane = Open(window, tab);
        Assert.True(pane.ControlOf("PART_S1_First")!.IsEnabled);

        // A save holds the document: the controls wait and say why (the same reason words as every other command).
        using var save = session.BeginSave();
        pane.RefreshButtons();
        Assert.False(pane.ControlOf("PART_S1_First")!.IsEnabled);
        Assert.Equal(WriteStrings.ReasonEditSaving, ToolTip.GetTip(pane.ControlOf("PART_S1_First")!));
        Assert.False(pane.ControlOf("PART_LeftRight")!.IsEnabled);
    }

    [AvaloniaFact]
    public async Task TheGermanWordsFitThe256PixelPane()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, EndsSection("a"), EndsSection("b", "<w:pgNumType w:start=\"5\" w:fmt=\"lowerRoman\"/><w:titlePg/>"), EndsSection("c", "<w:cols w:num=\"2\" w:space=\"720\"/>"), Text("d"));
        Caret(session, tab, 1);
        var previous = WriteStrings.Culture;
        try
        {
            foreach (var culture in new[] { CultureInfo.GetCultureInfo("en"), CultureInfo.GetCultureInfo("de") })
            {
                WriteStrings.Culture = culture;
                using var pane = new SectionsPane(tab, new SectionsPaneActions(_ => 0, () => 1, () => null, _ => true));
                var host = Host(pane);
                Assert.True(pane.DesiredSize.Width <= 256.5, $"{culture.Name}: {pane.DesiredSize.Width}");
                var texts = pane.GetVisualDescendants().OfType<TextBlock>().Where(t => !string.IsNullOrEmpty(t.Text)).ToList();
                Assert.NotEmpty(texts);
                Assert.All(texts, t => Assert.True(t.DesiredSize.Width <= 256, $"{culture.Name}: {t.Text}"));
                var boxes = pane.GetVisualDescendants().OfType<CheckBox>().ToList();
                Assert.All(boxes, b => Assert.True(b.DesiredSize.Width <= 256, $"{culture.Name}: {b.Content}"));
                host.Close();
            }
        }
        finally
        {
            WriteStrings.Culture = previous;
        }
    }

    [AvaloniaFact]
    public async Task ARowWhoseStartPageIsNotLaidOutSaysSo()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, EndsSection("a"), Text("b"));
        Caret(session, tab, 0);
        using var pane = new SectionsPane(tab, new SectionsPaneActions(block => block == 0 ? 0 : -1, () => null, () => null, _ => true));
        var host = Host(pane);
        var lines = pane.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(WriteStrings.Fill(WriteStrings.SectionStartsOnPageFormat, 1), lines);
        Assert.Contains(WriteStrings.SectionPageNotLaidOut, lines);
        host.Close();
    }

    private sealed class ScriptedCards : ISectionCards
    {
        public PageNumbering? Answer { get; set; }

        public int Asked { get; private set; }

        public NumberPagesContext? Last { get; private set; }

        public Task<PageNumbering?> AskNumberingAsync(NumberPagesContext context)
        {
            Asked++;
            Last = context;
            return Task.FromResult(Answer);
        }

        public Task<PageSetupRequest?> AskPageSetupAsync(PageSetupContext context) => Task.FromResult<PageSetupRequest?>(null);
    }
}
