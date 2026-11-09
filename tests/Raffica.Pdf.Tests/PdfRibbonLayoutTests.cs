using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;
using Xunit;

namespace Raffica.Pdf.Tests;

/// <summary>
/// M2.7 Task 10: the ribbon of the PDF app never hides a command. At the default window width the full band of several tabs is wider
/// than the window; the band then switches to the compact layout by itself and, when even that does not fit, scrolls with chevron
/// buttons. A command counts as reachable when its button lies fully inside the visible part of the band, or when the chevrons bring
/// it there (a chevron that is missing or hidden while the button is out of sight fails the test).
/// </summary>
public sealed class PdfRibbonLayoutTests
{
    public static TheoryData<string, int> LanguagesAndWidths => new()
    {
        { "en", 1280 },
        { "en", 1024 },
        { "de", 1280 },
        { "de", 1024 },
    };

    private static void Layout(PdfWindow window)
    {
        ViewFixture.Pump();
        window.UpdateLayout();
        ViewFixture.Pump();
        window.UpdateLayout();
    }

    private static WindowFixture Show(string language, double width)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        ShellStrings.Culture = culture;
        PdfStrings.Culture = culture;
        return WindowFixture.Create(width: width, height: 700);
    }

    private static async Task<WindowFixture> ShowWithDocumentAsync(string language, double width)
    {
        var fixture = Show(language, width);
        await fixture.OpenAsync("text-rich.pdf");
        Layout(fixture.Window);
        return fixture;
    }

    private static T Part<T>(Visual root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    private static ScrollViewer Scroller(PdfWindow window) => Part<ScrollViewer>(window.Ribbon.Band, "PART_BandScroll");

    private static IEnumerable<RibbonButton> Buttons(PdfWindow window) =>
        window.Ribbon.Band.GetVisualDescendants().OfType<RibbonButton>();

    private static Rect BoundsIn(Control control, Visual root) =>
        new(control.TranslatePoint(default, root) ?? default, control.Bounds.Size);

    private static bool FullyVisible(Control button, ScrollViewer scroller)
    {
        var bounds = BoundsIn(button, scroller);
        return bounds.Left >= -0.5 && bounds.Right <= scroller.Bounds.Width + 0.5;
    }

    private static double BandRowHeight(PdfWindow window) => Part<Border>(window.Frame, "PART_BandRow").Bounds.Height;

    private static Button Chevron(PdfWindow window, string name) => Part<Button>(window.Ribbon.Band, name);

    private static void Press(PdfWindow window, Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Layout(window);
    }

    /// <summary>Brings the button into the visible part with the chevrons; returns false when the way is missing (no chevron, or no progress).</summary>
    private static bool Reveal(PdfWindow window, RibbonButton button)
    {
        var scroller = Scroller(window);
        for (var i = 0; i < 60 && !FullyVisible(button, scroller); i++)
        {
            var bounds = BoundsIn(button, scroller);
            var chevron = Chevron(window, bounds.Left < 0 ? "PART_ScrollLeft" : "PART_ScrollRight");
            if (!chevron.IsEffectivelyVisible)
            {
                return false;
            }

            var before = scroller.Offset.X;
            Press(window, chevron);
            if (scroller.Offset.X == before)
            {
                return false;
            }
        }

        return FullyVisible(button, scroller);
    }

    [AvaloniaTheory]
    [MemberData(nameof(LanguagesAndWidths))]
    public async Task EveryCommandOfEveryTabIsVisibleOrReachableThroughTheChevrons(string language, int width)
    {
        using var f = await ShowWithDocumentAsync(language, width);
        var window = f.Window;
        Assert.True(window.Frame.Bounds.Width >= width - 1, $"the headless window is {window.Frame.Bounds.Width} DIP wide, expected {width}");

        foreach (var tab in window.Ribbon.Definition!.Tabs)
        {
            window.Ribbon.ActiveTab = tab;
            Layout(window);

            var inMenus = window.Ribbon.Band.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Classes.Contains("ribbon-more") && b.Flyout is MenuFlyout)
                .SelectMany(b => ((MenuFlyout)b.Flyout!).Items.OfType<MenuItem>())
                .Select(i => ((CommandDefinition)i.Tag!).Id)
                .ToHashSet();

            foreach (var command in tab.Groups.SelectMany(g => g.Commands))
            {
                var button = Buttons(window).FirstOrDefault(b => ReferenceEquals(b.Definition, command));
                if (button is null)
                {
                    Assert.True(inMenus.Contains(command.Id), $"[{language} {width}] {tab.Name}/{command.Id} has neither a button nor an overflow item");
                    continue;
                }

                Assert.True(Reveal(window, button), $"[{language} {width}] {tab.Name}/{command.Id} is clipped and no chevron leads to it");
            }

            // The overflow buttons are themselves commands the user must reach.
            foreach (var more in window.Ribbon.Band.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("ribbon-more")))
            {
                var scroller = Scroller(window);
                for (var i = 0; i < 60 && !FullyVisible(more, scroller); i++)
                {
                    var chevron = Chevron(window, BoundsIn(more, scroller).Left < 0 ? "PART_ScrollLeft" : "PART_ScrollRight");
                    Assert.True(chevron.IsEffectivelyVisible, $"[{language} {width}] {tab.Name}: a group menu is out of sight and no chevron leads to it");
                    Press(window, chevron);
                }

                Assert.True(FullyVisible(more, scroller), $"[{language} {width}] {tab.Name}: a group menu stays clipped");
            }
        }
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public async Task At1280TheBandIsCompactExactlyWhenTheFullBandDoesNotFit(string language)
    {
        // The full width of every tab, read from a window that is wide enough for all of them.
        var fullRight = new Dictionary<string, double>();
        using (var wide = await ShowWithDocumentAsync(language, 3200))
        {
            foreach (var tab in wide.Window.Ribbon.Definition!.Tabs)
            {
                wide.Window.Ribbon.ActiveTab = tab;
                Layout(wide.Window);
                Assert.False(wide.Window.Ribbon.IsEffectivelyCompact, $"{language}: {tab.Name} is compact at 3200 DIP");
                var scroller = Scroller(wide.Window);
                fullRight[tab.Name] = Buttons(wide.Window).Max(b => BoundsIn(b, scroller).Right);
            }
        }

        using var f = await ShowWithDocumentAsync(language, 1280);
        var sawCompact = false;
        var sawFull = false;
        foreach (var tab in f.Window.Ribbon.Definition!.Tabs)
        {
            f.Window.Ribbon.ActiveTab = tab;
            Layout(f.Window);
            var compact = f.Window.Ribbon.IsEffectivelyCompact;
            sawCompact |= compact;
            sawFull |= !compact;
            if (fullRight[tab.Name] > 1280)
            {
                Assert.True(compact, $"{language}: {tab.Name} needs {fullRight[tab.Name]:F0} DIP at full size and is not compact at 1280");
            }
            else if (fullRight[tab.Name] + 20 < 1280)
            {
                Assert.False(compact, $"{language}: {tab.Name} needs only {fullRight[tab.Name]:F0} DIP at full size and is compact at 1280");
            }

            Assert.Equal(compact ? 40 : 82, BandRowHeight(f.Window), 0.5);
        }

        Assert.True(sawCompact, $"{language}: the review premise (some tab does not fit at 1280) no longer holds; adjust the test");
        Assert.True(sawFull || language == "de", $"{language}: no tab keeps the full band at 1280");
    }

    [AvaloniaFact]
    public async Task TheManualCompactSettingMakesEveryTabCompactAt1920()
    {
        using var f = await ShowWithDocumentAsync("en", 1920);
        f.Window.SetCompact(true);
        Layout(f.Window);
        foreach (var tab in f.Window.Ribbon.Definition!.Tabs)
        {
            f.Window.Ribbon.ActiveTab = tab;
            Layout(f.Window);
            Assert.True(f.Window.Ribbon.IsEffectivelyCompact, $"{tab.Name} is not compact");
            Assert.Equal(40, BandRowHeight(f.Window), 0.5);
            Assert.DoesNotContain(Buttons(f.Window), b => b.Kind == RibbonButtonKind.Tile);
        }
    }

    [AvaloniaFact]
    public async Task EveryButtonStaysReachableWhenTheTabsChangeBackAndForth()
    {
        // The measure of a tab is kept per tab; going back to a tab must give the same answer as the first visit.
        using var f = await ShowWithDocumentAsync("en", 1280);
        foreach (var tab in f.Window.Ribbon.Definition!.Tabs.Concat(f.Window.Ribbon.Definition.Tabs.Reverse()))
        {
            f.Window.Ribbon.ActiveTab = tab;
            Layout(f.Window);
            foreach (var button in Buttons(f.Window))
            {
                Assert.True(Reveal(f.Window, button), $"{tab.Name}/{button.Definition.Id} is clipped");
            }
        }
    }
}
