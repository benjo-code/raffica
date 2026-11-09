using System.Globalization;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

/// <summary>The file name of a tab (it keeps its extension only when there is room) and the strip with many tabs (arrows, the list of all tabs, offscreen tabs for assistive technology), 08-UI-14.</summary>
public sealed class DocumentTabsOverflowTests
{
    // Eight pixels a character: the width of a text is easy to reason about.
    private static double Width(string text) => text.Length * 8;

    [Theory]
    [InlineData("Report.pdf", 400, "Report.pdf")]
    [InlineData("Quarterly-planning-for-the-regional-offices-FINAL-v12.pdf", 300, "Quarterly-planni…ffices-FINAL-v12.pdf")]
    [InlineData("Quarterly-planning-for-the-regional-offices-FINAL-v12.pdf", 150, "Quarter…AL-v12.pdf")]
    [InlineData("Quarterly-planning-for-the-regional-offices-FINAL-v12.pdf", 120, "Quart…L-v12.pdf")]
    [InlineData("Quarterly-planning-for-the-regional-offices-FINAL-v12.pdf", 100, "Quarte…L-v12")]
    [InlineData("Quarterly-planning-for-the-regional-offices-FINAL-v12.pdf", 48, "Qua…12")]
    [InlineData("Untitled document of the day", 120, "Untitle…the day")]
    [InlineData(".hidden-configuration-file", 100, ".hidde…-file")]
    public void TheNameIsShortenedBeforeTheExtensionAndTheExtensionGoesFirstWhenTheRoomIsShort(string text, double room, string expected)
    {
        var fitted = MiddleEllipsis.FitFileName(text, room, Width);
        Assert.Equal(expected, fitted);
        Assert.True(Width(fitted) <= room || fitted == "…", fitted);
    }

    [Fact]
    public void ANameNeverOverflowsItsRoomAndKeepsTheExtensionOnlyWithEightCharactersOfTheName()
    {
        const string text = "Quarterly-planning-for-the-regional-offices-FINAL-v12.pdf";
        for (double room = 0; room <= Width(text) + 8; room += 4)
        {
            var fitted = MiddleEllipsis.FitFileName(text, room, Width);
            Assert.True(Width(fitted) <= room || fitted == "…", $"{fitted} in {room}");
            if (fitted.EndsWith(".pdf", StringComparison.Ordinal) && fitted != text)
            {
                // The part of the name that is kept (without the ellipsis and the extension) is at least eight characters.
                Assert.True(fitted.Length - ".pdf".Length - 1 >= 8, $"{fitted} in {room}");
            }
        }
    }

    [Theory]
    [InlineData("archive.tar.gz", 60, "arc…tar")]
    [InlineData("a b.c d", 40, "a … d")]
    public void OnlyTheLastShortDotWithoutSpacesIsAnExtension(string text, double room, string expected) =>
        Assert.Equal(expected, MiddleEllipsis.FitFileName(text, room, Width));

    private static (Window Window, DocumentTabs Tabs) ShowMany(int count, double width = 600)
    {
        var (window, tabs) = DocumentTabsTests.Show(Enumerable.Range(0, count).Select(i => new DocumentTab($"Document number {i}.pdf")), width: width);
        return (window, tabs);
    }

    private static T Part<T>(DocumentTabs tabs, string name)
        where T : Control => ControlHarness.Part<T>(tabs, name);

    [AvaloniaFact]
    public void ArrowsAndTheListAppearOnlyWhenTheTabsDoNotFit()
    {
        var (_, few) = ShowMany(2, 1000);
        Assert.False(Part<Button>(few, "PART_ScrollLeft").IsVisible);
        Assert.False(Part<Button>(few, "PART_ScrollRight").IsVisible);
        Assert.False(Part<Button>(few, "PART_AllTabs").IsVisible);

        var (window, many) = ShowMany(30);
        var left = Part<Button>(many, "PART_ScrollLeft");
        var right = Part<Button>(many, "PART_ScrollRight");
        var all = Part<Button>(many, "PART_AllTabs");
        Assert.True(left.IsVisible && right.IsVisible && all.IsVisible);
        Assert.Equal(ShellStrings.TabsScrollLeft, Avalonia.Automation.AutomationProperties.GetName(left));
        Assert.Equal(ShellStrings.TabsScrollRight, Avalonia.Automation.AutomationProperties.GetName(right));
        Assert.Equal(ShellStrings.TabsAll, Avalonia.Automation.AutomationProperties.GetName(all));

        // Nothing is pushed out of the window: the plus button still ends inside it, the arrows are 28 px squares.
        var plus = Part<Button>(many, "PART_New");
        Assert.True(plus.TranslatePoint(new Point(plus.Bounds.Width, 0), window)!.Value.X <= 600.01);
        Assert.Equal(28, left.Bounds.Height, 0.01);
        Assert.True(left.Bounds.Width >= 24);

        // Removing tabs until they fit takes the controls away again.
        while (many.Items.Count > 2)
        {
            many.Items.RemoveAt(many.Items.Count - 1);
        }

        ControlHarness.Settle(window);
        Assert.False(left.IsVisible);
        Assert.False(all.IsVisible);
    }

    [AvaloniaFact]
    public void TheArrowsScrollByATabAndAreDisabledAtTheEnds()
    {
        var (window, tabs) = ShowMany(30);
        tabs.Selected = tabs.Items[0];
        ControlHarness.Settle(window);
        var scroll = Part<ScrollViewer>(tabs, "PART_Scroll");
        var left = Part<Button>(tabs, "PART_ScrollLeft");
        var right = Part<Button>(tabs, "PART_ScrollRight");

        Assert.Equal(0, scroll.Offset.X, 0.01);
        Assert.False(left.IsEnabled);
        Assert.True(right.IsEnabled);

        right.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ControlHarness.Settle(window);
        Assert.True(scroll.Offset.X > 0);
        Assert.True(left.IsEnabled);
        var first = scroll.Offset.X;

        // Each click brings the next tab on that side fully into view; the view never moves past the end.
        for (var i = 0; i < 60; i++)
        {
            right.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ControlHarness.Settle(window);
        }

        Assert.True(scroll.Offset.X > first);
        Assert.Equal(scroll.Extent.Width - scroll.Viewport.Width, scroll.Offset.X, 0.5);
        Assert.False(right.IsEnabled);
        Assert.True(left.IsEnabled);

        left.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ControlHarness.Settle(window);
        Assert.True(scroll.Offset.X < scroll.Extent.Width - scroll.Viewport.Width);
        Assert.True(right.IsEnabled);
    }

    [AvaloniaFact]
    public void TheListNamesEveryTabAndAClickSelectsItAndBringsItIntoView()
    {
        var (window, tabs) = ShowMany(30);
        tabs.Selected = tabs.Items[0];
        ControlHarness.Settle(window);
        var all = Part<Button>(tabs, "PART_AllTabs");

        var flyout = Assert.IsAssignableFrom<MenuFlyout>(all.Flyout);
        flyout.ShowAt(all);
        ControlHarness.Settle(window);
        var items = flyout.Items.OfType<MenuItem>().ToList();
        Assert.Equal(30, items.Count);
        Assert.Equal(tabs.Items.Select(t => t.Title), items.Select(i => (string)i.Header!));
        Assert.True(items[0].IsChecked);
        Assert.Equal(1, items.Count(i => i.IsChecked));

        items[17].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        ControlHarness.Settle(window);
        flyout.Hide();
        ControlHarness.Settle(window);

        Assert.Same(tabs.Items[17], tabs.Selected);
        var view = DocumentTabsTests.Views(tabs).First(v => v.Model == tabs.Items[17]);
        Assert.False(AutomationOf(view).IsOffscreen());
    }

    private static AutomationPeer AutomationOf(Control control) => ControlAutomationPeer.CreatePeerForElement(control);

    [AvaloniaFact]
    public void TabsOutsideTheStripReportOffscreen()
    {
        var (window, tabs) = ShowMany(30);
        tabs.Selected = tabs.Items[0];
        ControlHarness.Settle(window);
        var views = DocumentTabsTests.Views(tabs).ToList();

        Assert.False(AutomationOf(views[0]).IsOffscreen());
        Assert.True(AutomationOf(views[^1]).IsOffscreen());
        Assert.True(AutomationOf(views[20]).IsOffscreen());

        // A tab that is only partly in view counts as on screen (the user sees it and can click it).
        var scroll = Part<ScrollViewer>(tabs, "PART_Scroll");
        var visible = views.Where(v => !AutomationOf(v).IsOffscreen()).ToList();
        Assert.NotEmpty(visible);
        Assert.True(visible.Count < views.Count);
        Assert.All(visible, v => Assert.True(v.TranslatePoint(default, scroll)!.Value.X < scroll.Viewport.Width));

        tabs.Selected = tabs.Items[^1];
        ControlHarness.Settle(window);
        window.UpdateLayout();
        for (var i = 0; i < 5; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        Assert.False(AutomationOf(views[^1]).IsOffscreen());
        Assert.True(AutomationOf(views[0]).IsOffscreen());
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public void TheStripControlsHaveNamesInBothLanguages(string language)
    {
        var previous = ShellStrings.Culture;
        try
        {
            ShellStrings.Culture = CultureInfo.GetCultureInfo(language);
            var (_, tabs) = ShowMany(30);
            foreach (var name in new[] { "PART_ScrollLeft", "PART_ScrollRight", "PART_AllTabs" })
            {
                Assert.False(string.IsNullOrWhiteSpace(Avalonia.Automation.AutomationProperties.GetName(Part<Button>(tabs, name))));
            }

            if (language == "de")
            {
                Assert.Equal("Alle Dokumente", Avalonia.Automation.AutomationProperties.GetName(Part<Button>(tabs, "PART_AllTabs")));
            }
        }
        finally
        {
            ShellStrings.Culture = previous;
        }
    }

    [AvaloniaFact]
    public void AFileNameWithoutRoomLosesItsExtensionInTheTab()
    {
        var (window, tabs) = ShowMany(30);
        ControlHarness.Settle(window);
        var texts = DocumentTabsTests.Views(tabs).Select(v => ControlHarness.Part<MiddleTrimTextBlock>(v, "PART_Title")).ToList();

        // 30 tabs of 96 px leave about 44 px for the name beside the close button: no ".pdf" there, but the start of the name.
        Assert.All(texts, t => Assert.DoesNotContain(".pdf", t.DisplayText, StringComparison.Ordinal));
        Assert.All(texts, t => Assert.StartsWith("D", t.DisplayText, StringComparison.Ordinal));
    }
}
