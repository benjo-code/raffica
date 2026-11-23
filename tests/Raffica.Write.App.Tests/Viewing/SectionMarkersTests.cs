using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Panes;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;
using Raffica.WriteApp.Tests.Commands;
using Xunit;

namespace Raffica.WriteApp.Tests.Viewing;

/// <summary>The section markers of the page view (milestone 4.4b, Task 14): one chip for each section after the first, over the pages and never part of them.</summary>
public sealed class SectionMarkersTests : IDisposable
{
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(60);
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-write-markers-").FullName;

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

    private const string Break = "<w:pPr><w:sectPr><w:type w:val=\"nextPage\"/></w:sectPr></w:pPr>";

    private static string Run(string text) => $"<w:r><w:t>{text}</w:t></w:r>";

    // Three sections of one page each: the break paragraphs end the first two, the body's own properties end the third.
    private static string[] ThreeSections() => [Break + Run("one"), Break + Run("two"), Run("three")];

    private async Task<ViewHarness> OpenAsync(string[] paragraphs, WriteView mode = WriteView.Page, double width = 900, double height = 1400)
    {
        var path = StyledDocx.Write(_folder, Guid.NewGuid().ToString("N") + ".docx", StyledDocx.Styles, paragraphs);
        var harness = new ViewHarness(path, width, height, mode);
        harness.View.Runner.LeaseWaitTimeout = GateTimeout;
        harness.View.Runner.Start();
        await ViewHarness.WaitAsync(() => harness.View.Runner.FullModelReady || harness.View.Runner.IsFaulted, 30000);
        Assert.True(harness.View.Runner.FullModelReady);
        await harness.View.Runner.PageTask;
        await ViewHarness.WaitAsync(() => harness.View.FirstSharpPageShown, 30000);
        Settle(harness);
        return harness;
    }

    // The chips are brought up to date after the model's read (posted at the background priority) and the layout pass: a few turns of the dispatcher.
    private static void Settle(ViewHarness h)
    {
        for (var i = 0; i < 4; i++)
        {
            ViewHarness.Pump();
            h.Window.UpdateLayout();
        }
    }

    // Fits the page, zooms out until the pages are small and waits for the chips (the first frame comes from the render thread, the chips after it).
    private static async Task ZoomOutAsync(ViewHarness h)
    {
        h.View.FitPage();
        for (var i = 0; i < 4; i++)
        {
            h.View.ZoomOut();
        }

        Settle(h);
        await ViewHarness.WaitAsync(() =>
        {
            Settle(h);
            return h.View.ChipLayer is { Count: > 0 };
        }, 15000);
    }

    // Scrolls to a page and waits until a chip is shown there (the section whose chip is expected, or any chip).
    private static async Task ShowPageAsync(ViewHarness h, int page, int? section = null)
    {
        h.View.ScrollToPage(page);
        await ViewHarness.WaitAsync(
            () =>
            {
                Settle(h);
                return Chips(h).Any(c => c.Shown && (section is null || c.Section == section));
            },
            15000);
    }

    private static IReadOnlyList<(int Section, Button Button, bool Shown, bool Compact)> Chips(ViewHarness h) =>
        h.View.ChipLayer is { } layer ? [.. layer.Chips.OrderBy(c => c.Section)] : [];

    private static Point Center(ViewHarness h, Button button) =>
        button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), h.Window) ?? throw new InvalidOperationException("the chip is not on screen");

    private static Paragraph P(ViewHarness h, int index) => (Paragraph)h.View.Tab.Docx.Model.Body.Blocks[index];

    [AvaloniaFact]
    public async Task OneChipPerSectionAfterTheFirst()
    {
        using var h = await OpenAsync(ThreeSections(), height: 3200);
        await ZoomOutAsync(h);

        // At this zoom the three pages are in view: sections 2 and 3 (indexes 1 and 2) have a chip, the first section has none.
        Assert.True(h.View.PageCount >= 3);
        var chips = Chips(h);
        Assert.Equal([1, 2], chips.Select(c => c.Section));
        Assert.All(chips, c => Assert.True(c.Shown));
        Assert.NotNull(h.View.Markers);
        Assert.True(h.View.Markers!.MarkersMade >= 2);
    }

    [AvaloniaFact]
    public async Task AChipSitsOnThePageWhereItsSectionStarts()
    {
        using var h = await OpenAsync(ThreeSections());
        var layout = h.View.Tab.Layout;
        var sections = SectionList.Of(h.View.Tab.Docx.Model);
        foreach (var index in new[] { 1, 2 })
        {
            var expected = layout.Locate(sections[index].FirstBlock)!.Value.Page;
            await ShowPageAsync(h, expected, index);
            var chip = Assert.Single(Chips(h), c => c.Section == index);
            var bounds = h.View.PageBounds(expected)!.Value;
            var left = Canvas.GetLeft(chip.Button);
            var top = Canvas.GetTop(chip.Button);

            // The chip is on that page's band of the view, at the page's right edge: inside the margin or beside the page, never over the text area (72 pt, the margin of the document).
            Assert.InRange(top, bounds.Top - 0.5, bounds.Bottom);
            var textRight = bounds.Right - (1440 / 20.0 * h.View.Scroller!.Scale);
            Assert.True(left >= textRight - 0.5, $"chip at {left} reaches into the text area (right edge {textRight})");
            Assert.Equal(index, h.View.ChipLayer!.Chips.Single(c => c.Button == chip.Button).Section);
        }
    }

    [AvaloniaFact]
    public async Task TheChipSaysSectionAndTheNumbering()
    {
        var paragraphs = new[]
        {
            Break + Run("one"),
            "<w:pPr><w:sectPr><w:type w:val=\"nextPage\"/><w:pgNumType w:start=\"3\"/></w:sectPr></w:pPr>" + Run("two"),
            Run("three"),
        };
        using var h = await OpenAsync(paragraphs, height: 3200);
        await ZoomOutAsync(h);
        var chips = Chips(h);
        var second = chips.Single(c => c.Section == 2).Button;
        var third = chips.Single(c => c.Section == 1).Button;
        var secondTexts = second.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
        var thirdTexts = third.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
        Assert.Equal("Section 3", secondTexts[0]);
        Assert.Equal(WriteStrings.SectionChipNumbersContinue, secondTexts[1]);
        Assert.Equal("Section 2", thirdTexts[0]);
        Assert.Equal(WriteStrings.Fill(WriteStrings.SectionChipNumbersStartFormat, "3"), thirdTexts[1]);
        Assert.Equal("numbers start at 3", thirdTexts[1]);
    }

    [AvaloniaFact]
    public async Task NoChipInTheWritingView()
    {
        using var h = await OpenAsync(ThreeSections(), WriteView.Writing);
        Assert.Null(h.View.ChipLayer);
        Assert.Null(h.View.Markers);

        // Switching from the page view takes the chips away again.
        h.View.Mode = WriteView.Page;
        Settle(h);
        await ZoomOutAsync(h);
        Assert.NotEmpty(Chips(h));
        h.View.Mode = WriteView.Writing;
        Settle(h);
        Assert.Empty(Chips(h));
    }

    [AvaloniaFact]
    public async Task AChipHasAnAccessibleName()
    {
        using var h = await OpenAsync(ThreeSections(), height: 3200);
        await ZoomOutAsync(h);
        var chip = Chips(h).Single(c => c.Section == 2).Button;
        var name = AutomationProperties.GetName(chip);
        Assert.StartsWith("Section 3", name, StringComparison.Ordinal);
        Assert.Contains("page numbers continue", name, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(WriteStrings.Fill(WriteStrings.SectionChipHelpFormat, 3), AutomationProperties.GetHelpText(chip));

        // Assistive technology sees a button with that name that can be invoked (it is not a tab stop: Tab belongs to the page view).
        var peer = ControlAutomationPeer.CreatePeerForElement(chip);
        Assert.Equal(name, peer.GetName());
        Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType());
        Assert.NotNull(peer.GetProvider<Avalonia.Automation.Provider.IInvokeProvider>());
        Assert.False(chip.IsTabStop);
    }

    [AvaloniaFact]
    public async Task ClickingTheChipOpensThePopoverAndRemoveMergesTheSection()
    {
        using var h = await OpenAsync(ThreeSections(), height: 3200);
        await ZoomOutAsync(h);
        var session = h.View.Tab.EnsureEditor();
        var undo = session.Editor.History.UndoCount;
        var chip = Chips(h).Single(c => c.Section == 1).Button;

        h.Window.MouseDown(Center(h, chip), MouseButton.Left);
        h.Window.MouseUp(Center(h, chip), MouseButton.Left);
        ViewHarness.Pump();

        var flyout = Assert.IsType<Flyout>(chip.Flyout);
        Assert.True(flyout.IsOpen);
        var content = Assert.IsAssignableFrom<Control>(flyout.Content);
        Assert.Equal(WriteStrings.Fill(WriteStrings.SectionPopoverNameFormat, 2), AutomationProperties.GetName(content));
        var remove = content.GetVisualDescendants().OfType<Button>().Single(b => b.Name == SectionPopover.RemoveName);
        Assert.Equal(WriteStrings.SectionRemoveMarker, remove.Content);
        Assert.True(remove.IsEnabled);
        Assert.Equal(3, SectionList.Of(h.View.Tab.Docx.Model).Count);

        remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Settle(h);

        Assert.False(flyout.IsOpen);
        Assert.Equal(2, SectionList.Of(h.View.Tab.Docx.Model).Count);
        Assert.Equal(undo + 1, session.Editor.History.UndoCount);

        // One undo step brings the section and its chip back.
        Assert.True(h.View.RunEditCommand(WriteCommandIds.Undo));
        Settle(h);
        Assert.Equal(3, SectionList.Of(h.View.Tab.Docx.Model).Count);
        await ViewHarness.WaitAsync(() => Chips(h).Count(c => c.Shown) == 2, 10000);
        Assert.Equal([1, 2], Chips(h).Where(c => c.Shown).Select(c => c.Section));
    }

    [AvaloniaFact]
    public async Task NumberFromHereAndTheSwitchesGoThroughTheWindowsRouteAndTheMergeWordsAreOnThePopover()
    {
        var paragraphs = new[]
        {
            Break + Run("one"),
            "<w:pPr><w:sectPr><w:type w:val=\"nextPage\"/><w:cols w:num=\"2\"/></w:sectPr></w:pPr>" + Run("two"),
            Run("three"),
        };
        using var h = await OpenAsync(paragraphs, height: 3200);
        await ZoomOutAsync(h);
        var asked = new List<SectionRowAction>();
        h.View.SectionRowRunner = action =>
        {
            asked.Add(action);
            return true;
        };
        var chip = Chips(h).Single(c => c.Section == 1).Button;
        var flyout = (Flyout)chip.Flyout!;
        flyout.ShowAt(chip);
        ViewHarness.Pump();
        var content = (Control)flyout.Content!;

        // Section 2 has two columns, the section before it one: the sentence says what Remove marker takes over, on the popover itself.
        Assert.Contains(content.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == SectionsRowWording.RemoveTip(SectionsSource.RowFor(h.View.Tab.Docx.Model, SectionList.Of(h.View.Tab.Docx.Model), 1, 1), SectionsSource.RowFor(h.View.Tab.Docx.Model, SectionList.Of(h.View.Tab.Docx.Model), 0, 0)));
        Assert.Contains("page setup", content.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Single(t => t is not null && t.Contains("takes over", StringComparison.Ordinal)), StringComparison.Ordinal);

        content.GetVisualDescendants().OfType<Button>().Single(b => b.Name == SectionPopover.NumberName).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(flyout.IsOpen);
        flyout.ShowAt(chip);
        ViewHarness.Pump();
        ((Control)flyout.Content!).GetVisualDescendants().OfType<CheckBox>().Single(b => b.Name == SectionPopover.FooterName).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        var identity = SectionList.Of(h.View.Tab.Docx.Model)[1].Properties;
        Assert.Equal([SectionRowKind.NumberFrom, SectionRowKind.OwnFooter], asked.Select(a => a.Kind));
        Assert.All(asked, a => Assert.Equal(1, a.Section));
        Assert.All(asked, a => Assert.Same(identity, a.Identity));
        Assert.True(asked[1].On);
    }

    [AvaloniaFact]
    public async Task AClickOnTheChipDoesNotMoveTheCaret()
    {
        using var h = await OpenAsync(ThreeSections(), height: 3200);
        await ZoomOutAsync(h);
        var session = h.View.Tab.EnsureEditor();
        _ = session.Selection;
        var at = new TextPosition(StoryRef.Body, P(h, 0), 2);
        session.SetSelection(new TextRange(at, at));
        var chip = Chips(h).Single(c => c.Section == 2).Button;

        h.Window.MouseDown(Center(h, chip), MouseButton.Left);
        h.Window.MouseUp(Center(h, chip), MouseButton.Left);
        ViewHarness.Pump();

        Assert.True(((Flyout)chip.Flyout!).IsOpen);
        Assert.Equal(new TextRange(at, at), session.Selection);
    }

    [AvaloniaFact]
    public async Task TheStaleChipOfARemovedSectionOpensAnExplanationAndChangesNothing()
    {
        using var h = await OpenAsync(ThreeSections(), height: 3200);
        await ZoomOutAsync(h);
        var notes = new List<string>();
        h.View.StructureNote += notes.Add;
        var chip = Chips(h).Single(c => c.Section == 2).Button;
        var session = h.View.Tab.EnsureEditor();

        // The section is merged behind the chip's back (the chip has not been updated yet): the popover says so and the document is as it was.
        Assert.NotNull(session.Editor.RemoveSectionBreak(2));
        var revision = h.View.Tab.Docx.Model.Edits.Revision;
        ((Flyout)chip.Flyout!).ShowAt(chip);
        ViewHarness.Pump();

        Assert.Contains(WriteStrings.NoteSectionRowStale, notes);
        Assert.Equal(revision, h.View.Tab.Docx.Model.Edits.Revision);
        Assert.IsType<TextBlock>(((Flyout)chip.Flyout!).Content);
    }

    [AvaloniaFact]
    public async Task TheChipsFollowTheScrollAndTheZoom()
    {
        using var h = await OpenAsync(ThreeSections(), height: 700);
        var layout = h.View.Tab.Layout;
        var sections = SectionList.Of(h.View.Tab.Docx.Model);
        var page = layout.Locate(sections[1].FirstBlock)!.Value.Page;
        h.View.ScrollToStart();
        Settle(h);

        // At the top only page 1 is in view: no chip. Scrolled to the page of section 2 the chip is there, on that page.
        Assert.DoesNotContain(Chips(h), c => c.Shown);
        await ShowPageAsync(h, page, 1);
        var chip = Assert.Single(Chips(h), c => c.Section == 1 && c.Shown);
        var before = (Canvas.GetLeft(chip.Button), Canvas.GetTop(chip.Button));

        // A zoom keeps the chip on its page and moves it with the page; the chip keeps its own size.
        var size = chip.Button.Bounds.Size;
        h.View.ZoomIn();
        h.View.ScrollToPage(page);
        Settle(h);
        var zoomed = Assert.Single(Chips(h), c => c.Section == 1 && c.Shown);
        var bounds = h.View.PageBounds(page)!.Value;
        Assert.NotEqual(before, (Canvas.GetLeft(zoomed.Button), Canvas.GetTop(zoomed.Button)));
        Assert.InRange(Canvas.GetTop(zoomed.Button), bounds.Top - 0.5, bounds.Bottom);
        Assert.Equal(size.Width, zoomed.Button.Bounds.Width, 1.0);

        // Scrolled away again, the chip is let go of.
        h.View.ScrollToStart();
        Settle(h);
        Assert.DoesNotContain(Chips(h), c => c.Shown);
    }

    [AvaloniaFact]
    public async Task AChipForASectionWhosePageIsNotLaidOutIsNotMade()
    {
        // Only page 1 is laid out (the runner is not started): the markers of the later sections have no page to stand on.
        var path = StyledDocx.Write(_folder, "unlaid.docx", StyledDocx.Styles, ThreeSections());
        using var h = new ViewHarness(path, 900, 1400);
        var layout = h.View.Tab.Layout;
        Assert.True(layout.LaidOutPages < 3);
        var markers = new SectionMarkers();
        var found = markers.InView(h.View.Tab.Docx.Model, layout, 0, 50);
        Assert.All(found, m => Assert.True(m.Page < layout.LaidOutPages));
        Assert.True(found.Count < 2);
    }

    [AvaloniaFact]
    public async Task NothingIsComputedBeforeTheFirstSectionBreakIsKnown()
    {
        // A document with one section: the layout's own section count says so, the body is not read for markers and nothing of them exists, after the open, after typing and after scrolling.
        using var one = await OpenAsync([Run("one"), Run("two")]);
        one.View.ChipRefreshDelay = TimeSpan.FromHours(1);
        Assert.Null(one.View.ChipLayer);
        Assert.Null(one.View.Markers);
        var session = one.View.Tab.EnsureEditor();
        _ = session.Selection;
        var at = new TextPosition(StoryRef.Body, P(one, 0), 0);
        session.SetSelection(new TextRange(at, at));
        one.View.Focus();
        for (var i = 0; i < 20; i++)
        {
            one.Window.KeyTextInput("x");
        }

        one.View.FlushSectionChips();
        one.View.ScrollToEnd();
        one.View.ScrollToStart();
        Settle(one);
        Assert.Null(one.View.ChipLayer);
        Assert.Null(one.View.Markers);

        // A document that is still opening (a prefix) shows no chip and reads nothing.
        var path = StyledDocx.Write(_folder, "long.docx", StyledDocx.Styles, [.. Enumerable.Range(0, 200).Select(i => (i % 50 == 49 ? Break : string.Empty) + Run($"p{i}"))]);
        using var prefix = new ViewHarness(path, 900, 1400);
        Assert.True(prefix.View.Tab.Docx.IsPrefix);
        ViewHarness.Pump();
        Assert.Null(prefix.View.ChipLayer);
        Assert.Equal(0, prefix.View.Markers?.Reads ?? 0);
    }

    [AvaloniaFact]
    public async Task ARenderPassTouchesNoVisual()
    {
        using var h = await OpenAsync(ThreeSections(), height: 3200);
        await ZoomOutAsync(h);
        var layer = h.View.ChipLayer!;
        Assert.NotEmpty(Chips(h));
        var syncs = layer.Syncs;
        var made = layer.ChipsMade;
        var children = layer.Children.ToArray();
        var visualCount = h.View.GetVisualChildren().Count();
        var renders = h.View.RenderCount;
        var positions = Chips(h).Select(c => (Canvas.GetLeft(c.Button), Canvas.GetTop(c.Button), c.Shown)).ToArray();

        Assert.All(Chips(h), c => Assert.True(c.Button.IsMeasureValid && c.Button.IsArrangeValid));
        h.View.InvalidateVisual();
        _ = h.Capture();
        Assert.All(Chips(h), c => Assert.True(c.Button.IsMeasureValid && c.Button.IsArrangeValid, "the render pass invalidated a chip"));

        // A frame was rendered, and the layer was neither brought up to date nor changed by it.
        Assert.True(h.View.RenderCount > renders);
        Assert.Equal(syncs, layer.Syncs);
        Assert.Equal(made, layer.ChipsMade);
        Assert.Equal(children, layer.Children.ToArray());
        Assert.Equal(visualCount, h.View.GetVisualChildren().Count());
        Assert.Equal(positions, Chips(h).Select(c => (Canvas.GetLeft(c.Button), Canvas.GetTop(c.Button), c.Shown)).ToArray());
    }

    [AvaloniaFact]
    public async Task TheChipIsDrawnOnTheFrameAndLeavesTheTextAreaAlone()
    {
        using var h = await OpenAsync(ThreeSections(), height: 3200);
        await ZoomOutAsync(h);
        var chip = Chips(h).Single(c => c.Section == 1);
        var shot = h.Capture();
        var left = (int)Math.Floor(Canvas.GetLeft(chip.Button));
        var top = (int)Math.Floor(Canvas.GetTop(chip.Button));
        var width = (int)Math.Ceiling(chip.Button.Bounds.Width);
        var height = (int)Math.Ceiling(chip.Button.Bounds.Height);

        // Its text is on the frame inside the chip's box, and the box is outside the text area of the page.
        Assert.True(shot.DarkPixels(left, top, width, height) > 0, "the text of the chip is not on the frame");
        var bounds = h.View.PageBounds(h.View.Tab.Layout.Locate(SectionList.Of(h.View.Tab.Docx.Model)[1].FirstBlock)!.Value.Page)!.Value;
        var textRight = bounds.Right - (1440 / 20.0 * h.View.Scroller!.Scale);
        Assert.True(left >= textRight - 1, $"the chip ({left}) is over the text area (right edge {textRight})");
        Assert.True(chip.Button.Bounds.Width >= 40 && chip.Button.Bounds.Height >= 12, $"the chip is {chip.Button.Bounds.Size}");
    }

    [AvaloniaFact]
    public async Task PrintAndExportNeverSeeTheChips()
    {
        using var h = await OpenAsync(ThreeSections(), height: 3200);
        await ZoomOutAsync(h);
        Assert.NotEmpty(Chips(h));

        // Write has no print or export path yet, so this pins what such a path will read: the pages of the layout. The pages the view shows are the pages of the layout, and a layout made from the same document with no view at all has the same pages and items: the chips are not among them.
        var shown = h.View.Tab.Layout;
        var fresh = DocumentLayout.Create(h.View.Tab.Docx.Model, FontCatalog.CreateShipped(), new LayoutOptions(LayoutMode.Pages));
        await fresh.LayOutRestAsync(null, CancellationToken.None);
        Assert.Equal(shown.LaidOutPages, fresh.LaidOutPages);
        for (var i = 0; i < shown.LaidOutPages; i++)
        {
            Assert.Equal(shown.Page(i).Items.Count, fresh.Page(i).Items.Count);
            Assert.Equal(shown.Page(i).Text, fresh.Page(i).Text);
        }

        // The layout assembly knows nothing of a chip or a marker.
        Assert.DoesNotContain(typeof(LaidOutPage).Assembly.GetTypes(), t => t.Name.Contains("Chip", StringComparison.Ordinal) || t.Name.Contains("Marker", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task TheChipOfAPrefixDocumentIsNotOffered()
    {
        var path = StyledDocx.Write(_folder, "prefix.docx", StyledDocx.Styles, [.. Enumerable.Range(0, 300).Select(i => (i % 3 == 2 ? Break : string.Empty) + Run($"p{i}"))]);
        using var h = new ViewHarness(path, 900, 3200);
        Assert.True(h.View.Tab.Docx.IsPrefix);
        h.View.FitPage();
        Settle(h);
        await Task.Yield();
        Assert.Empty(Chips(h));
    }

    [AvaloniaFact]
    public async Task TheKeyNoteNamesTheMarkerOnlyWhenItsChipIsShown()
    {
        using var h = await OpenAsync(ThreeSections(), height: 3200);
        await ZoomOutAsync(h);
        var notes = new List<string>();
        h.View.StructureNote += notes.Add;
        var session = h.View.Tab.EnsureEditor();
        _ = session.Selection;
        var at = new TextPosition(StoryRef.Body, P(h, 1), 0);
        session.SetSelection(new TextRange(at, at));
        h.View.Focus();
        Assert.Contains(Chips(h), c => c.Section == 1 && c.Shown);

        // Backspace at the start of the paragraph after the first break: the marker of section 2 is on screen, the note names it.
        h.Window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        h.Window.KeyReleaseQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        ViewHarness.Pump();
        Assert.Equal([WriteStrings.NoteSectionBreakKey], notes);

        // In the writing view there is no marker: the note points to Remove section and the Sections pane instead.
        notes.Clear();
        h.View.Mode = WriteView.Writing;
        Settle(h);
        Assert.DoesNotContain(Chips(h), c => c.Shown);
        session.SetSelection(new TextRange(at, at));
        h.Window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        h.Window.KeyReleaseQwerty(PhysicalKey.Backspace, RawInputModifiers.None);
        ViewHarness.Pump();
        Assert.Equal([WriteStrings.NoteSectionBreakNoMarker], notes);
        Assert.DoesNotContain("marker", WriteStrings.NoteSectionBreakNoMarker, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(WriteStrings.Get("CmdRemoveSection"), WriteStrings.NoteSectionBreakNoMarker, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ARightOrMiddleClickOnTheChipDoesNotMoveTheCaret()
    {
        using var h = await OpenAsync(ThreeSections(), height: 3200);
        await ZoomOutAsync(h);
        var session = h.View.Tab.EnsureEditor();
        _ = session.Selection;
        var at = new TextPosition(StoryRef.Body, P(h, 0), 2);
        session.SetSelection(new TextRange(at, at));
        var chip = Chips(h).Single(c => c.Section == 2).Button;
        var point = Center(h, chip);
        foreach (var button in new[] { MouseButton.Right, MouseButton.Middle })
        {
            h.Window.MouseDown(point, button);
            h.Window.MouseUp(point, button);
            ViewHarness.Pump();
            Assert.Equal(new TextRange(at, at), session.Selection);
        }
    }

    [AvaloniaFact]
    public async Task ChipsOfSectionsThatStartCloseTogetherOnOnePageDoNotCoverEachOther()
    {
        const string Continuous = "<w:pPr><w:sectPr><w:type w:val=\"continuous\"/></w:sectPr></w:pPr>";
        using var h = await OpenAsync([Continuous + Run("a"), Continuous + Run("b"), Continuous + Run("c"), Run("d")], height: 3200);
        await ZoomOutAsync(h);
        await ViewHarness.WaitAsync(() => Chips(h).Count(c => c.Shown) == 3, 10000);
        var page = h.View.PageBounds(0)!.Value;
        var chips = Chips(h).Where(c => c.Shown && Canvas.GetTop(c.Button) < page.Bottom).OrderBy(c => Canvas.GetTop(c.Button)).ToArray();

        // Sections 2 and 3 start a text line apart on the first page (the last one starts the next page); the two chips are one under the other with no overlap.
        for (var i = 1; i < chips.Length; i++)
        {
            Assert.True(Canvas.GetTop(chips[i].Button) >= Canvas.GetTop(chips[i - 1].Button) + chips[i - 1].Button.Bounds.Height, $"chip {i} covers chip {i - 1}");
        }

        Assert.Equal(2, chips.Length);
        Assert.All(chips, c => Assert.InRange(Canvas.GetTop(c.Button) + c.Button.Bounds.Height, page.Top, page.Bottom + 0.5));
    }

    [AvaloniaFact]
    public async Task AThousandSectionsCostAHandfulOfChipsAndTypingReadsTheSectionsOncePerBurst()
    {
        var paragraphs = Enumerable.Range(0, 1000).Select(i => Break + Run($"s{i}")).Append(Run("end")).ToArray();
        using var h = await OpenAsync(paragraphs, height: 3200);
        await ZoomOutAsync(h);
        await ShowPageAsync(h, 500);
        var markers = h.View.Markers!;
        Assert.InRange(Chips(h).Count(c => c.Shown), 1, 16);
        Assert.True(markers.MarkersMade < 40, $"{markers.MarkersMade} markers were made for a view of a few pages");

        // A scroll over the memo makes only the few markers in view and allocates a small, fixed amount (no list of the 1001 sections).
        var madeBefore = markers.MarkersMade;
        var readsBefore = markers.Reads;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 20; i++)
        {
            h.View.ScrollToPage(500 + (i % 2));
        }

        var perScroll = (GC.GetAllocatedBytesForCurrentThread() - allocated) / 20;
        Assert.Equal(readsBefore, markers.Reads);
        Assert.True(markers.MarkersMade - madeBefore <= 20 * 16);
        Assert.True(perScroll < 256 * 1024, $"{perScroll} bytes per scroll");

        // Fifty typed keys read nothing of the sections while they go on (the chips wait for a pause; the pause is driven by hand here, so a slow machine cannot fire it early) and one read when it comes.
        h.View.ChipRefreshDelay = TimeSpan.FromHours(1);
        var session = h.View.Tab.EnsureEditor();
        _ = session.Selection;
        var at = new TextPosition(StoryRef.Body, P(h, 500), 0);
        session.SetSelection(new TextRange(at, at));
        h.View.Focus();
        readsBefore = markers.Reads;
        for (var i = 0; i < 50; i++)
        {
            h.Window.KeyTextInput("x");
        }

        Settle(h);
        Assert.Equal(readsBefore, markers.Reads);
        h.View.FlushSectionChips();
        Assert.Equal(readsBefore + 1, markers.Reads);
    }
}
