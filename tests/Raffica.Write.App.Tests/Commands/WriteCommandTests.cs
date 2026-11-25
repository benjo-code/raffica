using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Raffica.Core.Viewing;
using Raffica.Shell.Frame;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;
using Raffica.WriteApp.Tests.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Commands;

public sealed class WriteCommandTests
{
    private static IEnumerable<CommandDefinition> RibbonCommands(WriteWindow window) =>
        window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands);

    private static string[] AllIds() =>
        [.. typeof(WriteCommandIds).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!)];

    [Fact]
    public void EveryCommandIdIsInTheCatalogOnce()
    {
        var ids = AllIds();
        Assert.Equal(ids.Order(StringComparer.Ordinal), WriteCommandCatalog.Commands.Select(c => c.Id).Order(StringComparer.Ordinal));
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.Equal([WriteRibbon.HomeLetter, WriteRibbon.InsertLetter, WriteRibbon.TableLetter, WriteRibbon.LayoutLetter, WriteRibbon.ViewLetter], WriteCommandCatalog.Tabs.Select(t => t.Letter).ToArray());
    }

    [AvaloniaFact]
    public void EveryCommandHasItsOwnIcon()
    {
        // One glyph per meaning: no two commands of Write share an icon.
        foreach (var group in WriteCommandCatalog.Commands.GroupBy(c => c.Icon).Where(g => g.Count() > 1))
        {
            Assert.Fail($"Icon {group.Key} serves {string.Join(", ", group.Select(c => c.Id))}");
        }

        foreach (var command in WriteCommandCatalog.Commands)
        {
            Assert.True(
                Application.Current!.TryGetResource("Raffica.Icon." + command.Icon, Application.Current!.ActualThemeVariant, out var value),
                command.Id + " " + command.Icon);
            Assert.IsAssignableFrom<Geometry>(value);
        }
    }

    [Fact]
    public void EveryNameResolvesInBothLanguages()
    {
        foreach (var language in new[] { "en", "de" })
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo(language);
            foreach (var command in WriteCommandCatalog.Commands)
            {
                Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn(command.NameKey, culture)), command.Id);
            }

            foreach (var group in WriteCommandCatalog.Tabs.SelectMany(t => t.Groups))
            {
                Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn(group.LabelKey, culture)), group.LabelKey);
            }
        }
    }

    [Fact]
    public void ShortcutsAreUniqueAndNoneIsOneThatMacOsReserves()
    {
        foreach (var mac in new[] { false, true })
        {
            var shortcuts = WriteCommandCatalog.Commands.Where(c => c.ShortcutFor(mac) is not null).Select(c => c.ShortcutFor(mac)!.Text(mac)).ToList();
            Assert.Equal(shortcuts.Count, shortcuts.Distinct().Count());
        }

        // The keys macOS keeps for itself (hide, minimize, quit, switch app, Spotlight, window switch): none is a command of Write there.
        Key[] reservedWithCommand = [Key.H, Key.M, Key.Q, Key.Tab, Key.Space, Key.OemTilde];
        foreach (var command in WriteCommandCatalog.Commands)
        {
            if (command.ShortcutFor(true) is { Base: WriteShortcutBase.Command } shortcut)
            {
                Assert.DoesNotContain(shortcut.Keys, reservedWithCommand.Contains);
            }
        }
    }

    [Fact]
    public void TheCatalogFindsAShortcutByItsKeysAndTheCommandSearchIsTheFrames()
    {
        Assert.Equal(WriteCommandIds.Open, WriteCommandCatalog.Find(Key.O, KeyModifiers.Control, KeyModifiers.Control, macOs: false)?.Id);
        Assert.Equal(WriteCommandIds.ZoomIn, WriteCommandCatalog.Find(Key.OemPlus, KeyModifiers.Control | KeyModifiers.Shift, KeyModifiers.Control, macOs: false)?.Id);
        Assert.Equal(WriteCommandIds.ZoomOut, WriteCommandCatalog.Find(Key.Subtract, KeyModifiers.Control, KeyModifiers.Control, macOs: false)?.Id);
        Assert.Equal(WriteCommandIds.ActualSize, WriteCommandCatalog.Find(Key.NumPad0, KeyModifiers.Meta, KeyModifiers.Meta, macOs: true)?.Id);
        Assert.Null(WriteCommandCatalog.Find(Key.O, KeyModifiers.Control | KeyModifiers.Alt, KeyModifiers.Control, macOs: false));
        Assert.Null(WriteCommandCatalog.Find(Key.Q, KeyModifiers.Alt, KeyModifiers.Control, macOs: false));
        Assert.Null(WriteCommandCatalog.Find(Key.K, KeyModifiers.Meta, KeyModifiers.Meta, macOs: true));
    }

    [Fact]
    public void EveryDisabledCommandHasAReason()
    {
        foreach (var hasDocument in new[] { false, true })
        {
            foreach (var zoom in new[] { ZoomSteps.Min, ZoomSteps.Default, ZoomSteps.Max })
            {
                foreach (var outline in new[] { false, true })
                {
                    var rules = new WriteCommandRules(() => hasDocument, () => zoom, () => outline);
                    foreach (var id in AllIds())
                    {
                        var state = $"{id}, document {hasDocument}, zoom {zoom}, outline {outline}";
                        var reason = rules.ReasonFor(id);
                        if (!hasDocument)
                        {
                            // The start view shows no commands, so nothing is told.
                            Assert.Null(reason);
                        }
                        else if (rules.Allows(id))
                        {
                            Assert.True(reason is null, state);
                        }
                        else
                        {
                            Assert.False(string.IsNullOrWhiteSpace(reason), state);
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void TheRulesFollowTheZoomLimitsAndTheOutlinePane()
    {
        var zoom = 1.0;
        var outline = false;
        var rules = new WriteCommandRules(() => true, () => zoom, () => outline);
        Assert.True(rules.Allows(WriteCommandIds.ZoomIn) && rules.Allows(WriteCommandIds.ZoomOut));
        Assert.False(rules.Allows(WriteCommandIds.Outline));
        outline = true;
        Assert.True(rules.Allows(WriteCommandIds.Outline));
        zoom = ZoomSteps.Max;
        Assert.False(rules.Allows(WriteCommandIds.ZoomIn));
        Assert.Equal("Already at the largest zoom.", rules.ReasonFor(WriteCommandIds.ZoomIn));
        zoom = ZoomSteps.Min;
        Assert.False(rules.Allows(WriteCommandIds.ZoomOut));
        Assert.Equal("Already at the smallest zoom.", rules.ReasonFor(WriteCommandIds.ZoomOut));

        // Open, Settings and the command search need no document.
        var none = new WriteCommandRules(() => false, () => 1.0);
        Assert.True(none.Allows(WriteCommandIds.Open) && none.Allows(WriteCommandIds.Settings) && none.Allows(WriteCommandIds.Search));
        Assert.False(none.Allows(WriteCommandIds.CloseTab) || none.Allows(WriteCommandIds.PageView));
    }

    [AvaloniaFact]
    public async Task ViewSwitchKeepsTheAnchor()
    {
        using var harness = new ViewHarness(WindowFixture.Thesis);
        var view = harness.View;
        await harness.LayOutEverythingAsync();
        var layout = view.Runner.PageLayout;
        Assert.True(layout.LaidOutPages > 60);

        // The first block that starts on page 40 stands for the heading.
        var block = -1;
        for (var b = 0; b < 100_000 && block < 0; b++)
        {
            var at = layout.Locate(b);
            if (at is { Page: 39 })
            {
                block = b;
            }
            else if (at is { Page: > 39 })
            {
                break;
            }
        }

        Assert.True(block >= 0, "no block starts on page 40");
        var anchor = new ViewAnchor(block, 0);
        view.ScrollToAnchor(anchor);
        ViewHarness.Pump();
        Assert.InRange(view.CurrentPage, 38, 40);
        var pageOffset = view.Scroller!.OffsetOf(anchor)!.Value;
        Assert.InRange(view.OffsetY, pageOffset - view.Viewport.Height, pageOffset + view.Viewport.Height);
        Assert.Equal(anchor, view.TopAnchor);

        // To the writing view: the same block is near the top once the strips reach it.
        view.Mode = WriteView.Writing;
        await ViewHarness.WaitAsync(
            () => view.Mode == WriteView.Writing && view.Scroller?.OffsetOf(anchor) is { } off && Math.Abs(view.OffsetY - (off - (PageView.StripEdge / 2))) < 1,
            40000);
        var stripOffset = view.Scroller!.OffsetOf(anchor)!.Value;
        Assert.True(Math.Abs(view.OffsetY - stripOffset) < view.Viewport.Height, "the block is not within one screen of the top of the writing view");
        Assert.NotNull(view.Runner.FlowLayout);

        // And back: the page view shows the block again (both layouts were kept).
        var flow = view.Runner.FlowLayout;
        view.Mode = WriteView.Page;
        ViewHarness.Pump();
        Assert.Same(flow, view.Runner.FlowLayout);
        var backOffset = view.Scroller!.OffsetOf(anchor)!.Value;
        Assert.True(Math.Abs(view.OffsetY - backOffset) < view.Viewport.Height, "the block is not within one screen of the top of the page view");
        Assert.InRange(view.CurrentPage, 38, 40);
    }

    [AvaloniaFact]
    public void ZoomModesFitWidthAndFitPage()
    {
        using var harness = new ViewHarness(WindowFixture.Thesis, 900, 600, WriteView.Page, ZoomMode.ActualSize);
        var view = harness.View;
        var page = view.Runner.PageLayout.Page(0);
        var widthPoints = page.Width * PageScroller.DipsPerPoint;
        var heightPoints = page.Height * PageScroller.DipsPerPoint;
        Assert.Equal(1.0, view.Zoom);
        Assert.Equal(ZoomMode.ActualSize, view.ZoomMode);

        view.FitWidth();
        var fitWidth = ZoomSteps.Clamp((900 - (2 * PageView.PageGap)) / widthPoints);
        Assert.Equal(fitWidth, view.Zoom, 6);
        Assert.Equal(ZoomMode.FitWidth, view.ZoomMode);
        Assert.Equal(900 - (2 * PageView.PageGap), view.PageBounds(0)!.Value.Width, 3);

        view.FitPage();
        var fitPage = ZoomSteps.Clamp(Math.Min(fitWidth, (600 - (2 * PageView.PageGap)) / heightPoints));
        Assert.Equal(fitPage, view.Zoom, 6);
        Assert.True(fitPage < fitWidth);
        Assert.Equal(ZoomMode.FitPage, view.ZoomMode);
        Assert.True(view.PageBounds(0)!.Value.Height <= 600 - (2 * PageView.PageGap) + 0.001);

        // A fit mode follows the window; a manual zoom ends it.
        view.FitWidth();
        harness.Window.Width = 1300;
        ViewHarness.Pump();
        Assert.Equal(ZoomSteps.Clamp((1300 - (2 * PageView.PageGap)) / widthPoints), view.Zoom, 6);
        view.ZoomIn();
        Assert.Equal(ZoomMode.Free, view.ZoomMode);
        var manual = view.Zoom;
        harness.Window.Width = 900;
        ViewHarness.Pump();
        Assert.Equal(manual, view.Zoom);

        view.ActualSize();
        Assert.Equal(1.0, view.Zoom);
        Assert.Equal(ZoomMode.ActualSize, view.ZoomMode);
        view.SetZoom(100);
        Assert.Equal(ZoomSteps.Max, view.Zoom);
        view.SetZoom(0.0001);
        Assert.Equal(ZoomSteps.Min, view.Zoom);
    }

    [AvaloniaFact]
    public async Task ClosingATabCancelsItsLayout()
    {
        var unobserved = new List<Exception>();
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e) => unobserved.Add(e.Exception);
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            using var fixture = new WindowFixture();
            var window = fixture.Show();
            var second = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Poi)).Tab;
            var first = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
            WindowFixture.Pump();
            var view = window.ViewOf(first)!;
            var runner = view.Runner;
            runner.Start();
            await ViewHarness.WaitAsync(() => runner.FullModelReady && runner.PageLayout.LaidOutPages > 3);
            Assert.False(runner.PageLayout.IsComplete, "the layout ended before the tab was closed");
            var layout = runner.PageLayout;

            window.Workspace.Close(first);
            WindowFixture.Pump();

            // The token is cancelled, the runner is disposed, the pictures of the view are let go; the layout stops at the end of its page and nothing is thrown.
            Assert.True(first.IsDisposed);
            Assert.True(first.Token.IsCancellationRequested);
            Assert.True(runner.IsDisposed);
            Assert.True(view.IsReleased && view.PicturesReleased);
            await runner.PageTask.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Null(runner.Fault);
            Assert.False(layout.IsComplete);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.Empty(unobserved);

            // The other tab keeps working: it is active now, shows its page view and lays out to the end.
            Assert.Same(second, window.Workspace.Active);
            var other = window.ViewOf(second)!;
            other.Runner.Start();
            await ViewHarness.WaitAsync(() => other.Runner.PageLayout.IsComplete, 30000);
            Assert.False(other.Runner.IsDisposed);
            Assert.Null(other.Runner.Fault);
            Assert.False(second.IsDisposed);
            Assert.True(other.PageCount >= 1);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    [AvaloniaFact]
    public async Task ClosingTheWindowWhileLayingOutIsClean()
    {
        var unobserved = new List<Exception>();
        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e) => unobserved.Add(e.Exception);
        TaskScheduler.UnobservedTaskException += OnUnobserved;
        try
        {
            using var fixture = new WindowFixture();
            var window = fixture.Show();
            var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
            WindowFixture.Pump();
            var view = window.ViewOf(tab)!;
            var runner = view.Runner;
            runner.Start();
            await ViewHarness.WaitAsync(() => runner.FullModelReady && runner.PageLayout.LaidOutPages > 3);
            Assert.False(runner.PageLayout.IsComplete);

            window.Close();
            WindowFixture.Pump();

            Assert.True(tab.IsDisposed && tab.Token.IsCancellationRequested);
            Assert.True(runner.IsDisposed);
            Assert.True(view.PicturesReleased);
            await runner.PageTask.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.Null(runner.Fault);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.Empty(unobserved);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }
    }

    [AvaloniaFact]
    public async Task DefaultViewAndZoomFromSettings()
    {
        using var fixture = new WindowFixture();
        var window = fixture.ShowWith(new WriteSettings(WriteView.Writing, ZoomMode.FitPage));
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Poi)).Tab;
        WindowFixture.Pump();
        var view = window.ViewOf(tab)!;
        Assert.Equal(WriteView.Writing, view.Mode);
        Assert.Equal(ZoomMode.FitPage, view.ZoomMode);

        var commands = RibbonCommands(window).ToDictionary(c => c.Id);
        Assert.True(commands[WriteCommandIds.WritingView].IsChecked);
        Assert.False(commands[WriteCommandIds.PageView].IsChecked);
        Assert.True(commands[WriteCommandIds.FitPage].IsChecked);

        // Without settings a tab opens in the page view, fitted to the width.
        using var other = new WindowFixture();
        var defaults = other.Show();
        var plain = Assert.IsType<Opened>(await defaults.Flow.OpenAsync(WindowFixture.Poi)).Tab;
        WindowFixture.Pump();
        Assert.Equal(WriteView.Page, defaults.ViewOf(plain)!.Mode);
        Assert.Equal(ZoomMode.FitWidth, defaults.ViewOf(plain)!.ZoomMode);
    }

    [AvaloniaFact]
    public async Task TheRibbonHasHomeLayoutAndViewAndItsCommandsRunOnTheActiveView()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        Assert.Equal([WriteRibbon.HomeLetter, WriteRibbon.InsertLetter, WriteRibbon.TableLetter, WriteRibbon.LayoutLetter, WriteRibbon.ViewLetter], window.Ribbon.Definition!.Tabs.Select(t => t.Letter).ToArray());

        // No document: the start view; the frame holds no ribbon parts.
        Assert.Null(window.Frame.Band);
        Assert.True(RibbonCommands(window).Single(c => c.Id == WriteCommandIds.Open).IsEnabled);
        Assert.False(RibbonCommands(window).Single(c => c.Id == WriteCommandIds.ZoomIn).IsEnabled);

        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Poi)).Tab;
        WindowFixture.Pump();
        Assert.Same(window.Ribbon.Band, window.Frame.Band);
        Assert.Same(window.Ribbon.TabRow, window.Frame.TabRow);
        var view = window.ViewOf(tab)!;
        var commands = RibbonCommands(window).ToDictionary(c => c.Id);
        // A fresh document has no selection, no history and no change: undo, redo, cut, copy and Save wait (with their reasons); paste, select all, Save as and every other command are on.
        // Number headings also waits: the POI document lacks the nine heading styles, and the command says so. Back to the text waits for a caret in a note.
        string[] waiting = [WriteCommandIds.Undo, WriteCommandIds.Redo, WriteCommandIds.Cut, WriteCommandIds.Copy, WriteCommandIds.Save, WriteCommandIds.NumberHeadings, WriteCommandIds.NoteBack];
        // Every command has an action behind it (the value commands are pickers or, for the size steps, buttons).
        Assert.All(commands.Values.Where(c => !waiting.Contains(c.Id)), c => Assert.True(c.IsEnabled, c.Id));
        Assert.All(commands.Values.Where(c => waiting.Contains(c.Id)), c => Assert.False(c.IsEnabled || string.IsNullOrEmpty(c.CurrentDisabledReason), c.Id));

        commands[WriteCommandIds.WritingView].Run!();
        Assert.Equal(WriteView.Writing, view.Mode);
        Assert.True(commands[WriteCommandIds.WritingView].IsChecked);
        commands[WriteCommandIds.PageView].Run!();
        Assert.Equal(WriteView.Page, view.Mode);

        commands[WriteCommandIds.ActualSize].Run!();
        Assert.Equal(1.0, view.Zoom);
        Assert.True(commands[WriteCommandIds.ActualSize].IsChecked);
        commands[WriteCommandIds.ZoomIn].Run!();
        Assert.Equal(1.25, view.Zoom);
        Assert.False(commands[WriteCommandIds.ActualSize].IsChecked);
        commands[WriteCommandIds.FitWidth].Run!();
        Assert.Equal(ZoomMode.FitWidth, view.ZoomMode);
        commands[WriteCommandIds.FitPage].Run!();
        Assert.Equal(ZoomMode.FitPage, view.ZoomMode);

        // The zoom limits disable the zoom commands (with their reason) once the view reaches them.
        view.SetZoom(ZoomSteps.Max);
        window.Ribbon.RefreshCommandStates();
        Assert.False(commands[WriteCommandIds.ZoomIn].IsEnabled);
        Assert.Equal("Already at the largest zoom.", commands[WriteCommandIds.ZoomIn].CurrentDisabledReason);
        Assert.True(commands[WriteCommandIds.ZoomOut].IsEnabled);
    }

    [AvaloniaFact]
    public async Task TheShortcutsOfTheCatalogRunTheCommandsOfTheWindow()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Poi)).Tab;
        WindowFixture.Pump();
        var view = window.ViewOf(tab)!;
        window.Focus();
        view.Focus();
        view.ActualSize();
        Assert.Equal(1.0, view.Zoom);

        window.KeyPressQwerty(PhysicalKey.Equal, RawInputModifiers.Control | RawInputModifiers.Shift);
        Assert.Equal(1.25, view.Zoom);
        window.KeyPressQwerty(PhysicalKey.Minus, RawInputModifiers.Control);
        Assert.Equal(1.0, view.Zoom);
        window.KeyPressQwerty(PhysicalKey.Digit2, RawInputModifiers.Control);
        Assert.Equal(ZoomMode.FitPage, view.ZoomMode);
        window.KeyPressQwerty(PhysicalKey.Digit0, RawInputModifiers.Control);
        Assert.Equal(ZoomMode.ActualSize, view.ZoomMode);

        // Ctrl+W closes the tab (the window stays: the start view shows).
        window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Control);
        WindowFixture.Pump();
        Assert.Empty(window.Workspace.Tabs);
        Assert.True(window.IsVisible);
    }

    [AvaloniaFact]
    public async Task TheStatusBarShowsThePageAndThePlusUntilTheLayoutIsComplete()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
        WindowFixture.Pump();
        var view = window.ViewOf(tab)!;

        // Before the rest is laid out the count may still grow: a plus.
        Assert.Equal("Page 1 of 1+", window.Status.Position);
        Assert.Equal(WriteWindow.ZoomText(view.Zoom), window.Status.ZoomText);
        Assert.Equal(view.Zoom * 100, window.Status.Zoom, 6);

        await ViewHarness.WaitAsync(() => view.IsLayoutComplete && window.Status.Position?.EndsWith('+') == false, 40000);
        var count = view.PageCount;
        Assert.True(count > 100);
        Assert.Equal($"Page 1 of {count}", window.Status.Position);

        view.ScrollToPage(2);
        ViewHarness.Pump();
        Assert.Equal($"Page 3 of {count}", window.Status.Position);

        // The zoom slider of the status bar zooms the view.
        var slider = window.Status.GetVisualDescendants().OfType<Slider>().Single();
        slider.Value = 150;
        ViewHarness.Pump();
        Assert.Equal(1.5, view.Zoom, 6);
        Assert.Equal("150%", window.Status.ZoomText);

        // No document, no page cell.
        window.Workspace.Close(tab);
        WindowFixture.Pump();
        Assert.Null(window.Frame.StatusBar);
    }

    [AvaloniaFact]
    public async Task CloseTabOrWindowClosesTheWindowWhenNoTabIsOpen()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(WindowFixture.Poi)).Tab;
        WindowFixture.Pump();
        window.CloseTabOrWindow();
        WindowFixture.Pump();
        Assert.True(tab.IsDisposed);
        Assert.True(window.IsVisible);

        window.CloseTabOrWindow();
        WindowFixture.Pump();
        Assert.False(window.IsVisible);
    }
}
