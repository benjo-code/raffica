using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

public sealed class DocumentTabsTests
{
    private static readonly string[] CaptionNames = ["PART_CaptionMinimize", "PART_CaptionMaximize", "PART_CaptionClose"];

    internal static (Window Window, DocumentTabs Tabs) Show(IEnumerable<DocumentTab> items, ThemeMode mode = ThemeMode.Light, double width = 1000)
    {
        var tabs = new DocumentTabs { Height = 36, VerticalAlignment = VerticalAlignment.Top };
        foreach (var item in items)
        {
            tabs.Items.Add(item);
        }

        var window = ControlHarness.Show(tabs, mode);
        window.Width = width;
        ControlHarness.Settle(window);
        return (window, tabs);
    }

    internal static IEnumerable<DocumentTabItem> Views(Control root) => ControlHarness.Descendants(root).OfType<DocumentTabItem>();

    internal static void Click(Window window, Control control)
    {
        var centre = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(centre, MouseButton.Left);
        window.MouseUp(centre, MouseButton.Left);
        ControlHarness.Settle(window);
    }

    [AvaloniaTheory]
    [InlineData(ThemeMode.Light)]
    [InlineData(ThemeMode.Dark)]
    public void SelectedTabHasThe3pxInkTopEdge(ThemeMode mode)
    {
        var first = new DocumentTab("Report.docx");
        var second = new DocumentTab("Notes.docx");
        var (window, tabs) = Show([first, second], mode);
        tabs.Selected = first;
        ControlHarness.Settle(window);

        var selected = Views(tabs).First(v => v.Model == first);
        var other = Views(tabs).First(v => v.Model == second);
        var edge = ControlHarness.Part<Border>(selected, "PART_TopEdge");

        Assert.Equal(28, selected.Bounds.Height, 0.01);
        Assert.Equal(3, edge.Bounds.Height, 0.01);
        ControlHarness.AssertToken("Raffica.Brush.Ink", edge.Background, "selected top edge");
        ControlHarness.AssertToken("Raffica.Brush.Surface.Band", selected.Background, "selected fill");
        ControlHarness.AssertToken("Raffica.Brush.Rule", selected.BorderBrush, "selected border");
        Assert.Equal(1, selected.BorderThickness.Left);
        Assert.Equal(1, selected.BorderThickness.Right);
        Assert.Equal(Colors.Transparent, ControlHarness.ColorOf(ControlHarness.Part<Border>(other, "PART_TopEdge").Background));
        Assert.Equal(Colors.Transparent, ControlHarness.ColorOf(other.Background));
    }

    [AvaloniaFact]
    public void DirtyTabShowsAMarkAndKeepsItsWidth()
    {
        var tab = new DocumentTab("Report.docx");
        var (window, tabs) = Show([tab, new DocumentTab("Notes.docx")]);
        var view = Views(tabs).First(v => v.Model == tab);
        var mark = ControlHarness.Part<Shape>(view, "PART_DirtyMark");
        var title = ControlHarness.Part<MiddleTrimTextBlock>(view, "PART_Title");
        var widthClean = view.Bounds.Width;
        var titleWidthClean = title.Bounds.Width;
        Assert.False(tab.IsDirty);
        Assert.Equal(0, mark.Opacity);

        tab.IsDirty = true;
        ControlHarness.Settle(window);

        Assert.Equal(1, mark.Opacity);
        ControlHarness.AssertToken("Raffica.Brush.Text.Secondary", mark.Fill, "dirty mark");
        Assert.Equal(widthClean, view.Bounds.Width, 0.01);
        Assert.Equal(titleWidthClean, title.Bounds.Width, 0.01);
        var close = ControlHarness.Part<Button>(view, "PART_Close");
        Assert.True(mark.TranslatePoint(default, window)!.Value.X < close.TranslatePoint(default, window)!.Value.X, "the mark sits before the close button");

        tab.IsDirty = false;
        ControlHarness.Settle(window);
        Assert.Equal(0, mark.Opacity);
    }

    [AvaloniaFact]
    public void DirtyMarkIsInTheAutomationName()
    {
        var tab = new DocumentTab("Report.docx");
        var (window, tabs) = Show([tab]);
        var view = Views(tabs).Single();
        Assert.Equal("Report.docx", AutomationProperties.GetName(view));

        tab.IsDirty = true;
        ControlHarness.Settle(window);

        var name = AutomationProperties.GetName(view);
        Assert.StartsWith("Report.docx", name);
        Assert.Contains(ShellStrings.TabUnsavedChanges, name);

        tab.IsDirty = false;
        Assert.Equal("Report.docx", AutomationProperties.GetName(view));
    }

    [AvaloniaFact]
    public void ClosedTabItemIsNotKeptAliveByItsModel()
    {
        var kept = new DocumentTab("Kept.docx");
        var closed = new DocumentTab("Closed.docx");
        var (window, tabs) = Show([kept, closed]);
        var view = Views(tabs).First(v => v.Model == closed);

        tabs.Items.Remove(closed);
        ControlHarness.Settle(window);
        closed.IsDirty = true;

        // The detached item no longer listens to its model.
        Assert.Equal("Closed.docx", AutomationProperties.GetName(view));

        // Re-adding the tab shows its current state again.
        tabs.Items.Add(closed);
        ControlHarness.Settle(window);
        var again = Views(tabs).First(v => v.Model == closed);
        Assert.Contains(ShellStrings.TabUnsavedChanges, AutomationProperties.GetName(again));
        closed.IsDirty = false;
        Assert.Equal("Closed.docx", AutomationProperties.GetName(again));
    }

    [AvaloniaFact]
    public void SelectedTabIsExposedToAutomation()
    {
        var first = new DocumentTab("Report.docx");
        var second = new DocumentTab("Notes.docx");
        var (window, tabs) = Show([first, second]);
        tabs.Selected = first;
        ControlHarness.Settle(window);

        var firstPeer = ControlAutomationPeer.CreatePeerForElement(Views(tabs).First(v => v.Model == first));
        var secondPeer = ControlAutomationPeer.CreatePeerForElement(Views(tabs).First(v => v.Model == second));
        Assert.Equal(AutomationControlType.TabItem, firstPeer.GetAutomationControlType());
        var firstItem = Assert.IsAssignableFrom<ISelectionItemProvider>(firstPeer);
        var secondItem = Assert.IsAssignableFrom<ISelectionItemProvider>(secondPeer);
        Assert.True(firstItem.IsSelected);
        Assert.False(secondItem.IsSelected);

        secondItem.Select();
        ControlHarness.Settle(window);

        Assert.Same(second, tabs.Selected);
        Assert.False(firstItem.IsSelected);
        Assert.True(secondItem.IsSelected);
    }

    [AvaloniaFact]
    public void TabsKeepReferenceEqualityEvenWhenDirty()
    {
        var a = new DocumentTab("Same.docx") { IsDirty = true };
        var b = new DocumentTab("Same.docx") { IsDirty = true };
        Assert.NotEqual(a, b);
        Assert.Equal(a, a);
        Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
        var raised = new List<string?>();
        a.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        a.IsDirty = false;
        a.IsDirty = false;
        Assert.Equal([nameof(DocumentTab.IsDirty)], raised);
    }

    [AvaloniaFact]
    public void CloseButtonIs24By24AndNamed()
    {
        var (_, tabs) = Show([new DocumentTab("Report.docx")]);
        var close = ControlHarness.Part<Button>(Views(tabs).Single(), "PART_Close");

        Assert.Equal(24, close.Bounds.Width, 0.01);
        Assert.Equal(24, close.Bounds.Height, 0.01);
        Assert.Equal(ShellStrings.CloseDocument, AutomationProperties.GetName(close));
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(close)));
        Assert.NotEmpty(ControlHarness.Descendants(close).OfType<StrokeIcon>());
    }

    [AvaloniaFact]
    public void LongTitleIsTrimmedInTheMiddleAndTabIsAtMost240()
    {
        const string title = "Quarterly-planning-for-the-regional-offices-FINAL-reviewed-v12.docx";
        var (_, tabs) = Show([new DocumentTab(title), new DocumentTab("A.docx")]);
        var view = Views(tabs).First();
        var text = ControlHarness.Part<MiddleTrimTextBlock>(view, "PART_Title");

        Assert.Equal(240, view.Bounds.Width, 0.01);
        Assert.NotEqual(title, text.DisplayText);
        Assert.Contains('…', text.DisplayText);
        Assert.StartsWith("Quar", text.DisplayText);
        Assert.EndsWith("12.docx", text.DisplayText);
        Assert.True(text.Bounds.Width <= 240 - 24 - 8, "the title must leave room for the close button");

        var shortView = Views(tabs).Last();
        Assert.True(shortView.Bounds.Width < 240);
        Assert.True(shortView.Bounds.Width >= 96);
        Assert.Equal("A.docx", ControlHarness.Part<MiddleTrimTextBlock>(shortView, "PART_Title").DisplayText);
    }

    [AvaloniaFact]
    public void PlusIsAlwaysVisible()
    {
        var (_, empty) = Show([]);
        var plus = ControlHarness.Part<Button>(empty, "PART_New");
        Assert.True(plus.IsVisible);
        Assert.Equal(ShellStrings.NewDocument, AutomationProperties.GetName(plus));
        Assert.True(plus.Bounds.Width >= 24 && plus.Bounds.Height >= 24);

        var (window, many) = Show(Enumerable.Range(0, 40).Select(i => new DocumentTab($"Document number {i}.docx")), width: 600);
        var plus2 = ControlHarness.Part<Button>(many, "PART_New");
        var right = plus2.TranslatePoint(new Point(plus2.Bounds.Width, 0), window)!.Value.X;
        Assert.True(right <= 600.01, $"the plus button ends at {right}");
    }

    [AvaloniaFact]
    public void ThirtyTabsKeepPlusSearchAndCaptionButtonsInPlace()
    {
        var bar = new TitleBar { Mark = new TextBlock { Text = "R" } };
        bar.Search.Source = SampleRibbon.Create();
        for (var i = 0; i < 30; i++)
        {
            bar.Tabs.Items.Add(new DocumentTab($"Very long document name number {i} for the annual report.docx"));
        }

        var window = ControlHarness.Show(bar, ThemeMode.Light);
        window.Width = 1440;
        ControlHarness.Settle(window);

        var plus = ControlHarness.Part<Button>(bar.Tabs, "PART_New");
        var buttons = CaptionNames
            .Select(n => ControlHarness.Part<Button>(bar, n)).ToList();

        double Left(Control c) => c.TranslatePoint(default, window)!.Value.X;
        double Right(Control c) => Left(c) + c.Bounds.Width;

        Assert.True(Right(plus) <= Left(bar.Search) + 0.01, "plus ends before the search field");
        Assert.True(Left(plus) >= 0);
        Assert.InRange(bar.Search.Bounds.Width, 200, 300.01);
        Assert.Equal(28, bar.Search.Bounds.Height, 0.01);
        Assert.True(Right(bar.Search) <= Left(buttons[0]) + 0.01, "search ends before the caption buttons");
        foreach (var button in buttons)
        {
            Assert.Equal(46, button.Bounds.Width, 0.01);
            Assert.Equal(36, button.Bounds.Height, 0.01);
            Assert.True(Right(button) <= 1440.01);
        }

        Assert.Equal(1440, Right(buttons[2]), 0.01);
        Assert.Equal(28, plus.Bounds.Height, 0.01);

        // The tabs shrink to the minimum of 96 and the strip scrolls instead of pushing anything out.
        Assert.All(Views(bar.Tabs), v => Assert.True(v.Bounds.Width >= 96 - 0.01));
        var scroll = ControlHarness.Part<ScrollViewer>(bar.Tabs, "PART_Scroll");
        Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
    }

    [AvaloniaFact]
    public void CloseRaisesCloseRequested()
    {
        var first = new DocumentTab("One.docx");
        var second = new DocumentTab("Two.docx");
        var (window, tabs) = Show([first, second]);
        var requested = new List<DocumentTab>();
        tabs.CloseRequested += requested.Add;

        Click(window, ControlHarness.Part<Button>(Views(tabs).First(v => v.Model == second), "PART_Close"));

        Assert.Equal([second], requested);
        Assert.Equal(2, tabs.Items.Count); // the app decides; the control only asks
    }

    [AvaloniaFact]
    public void ClickingATabSelectsItAndPlusRaisesNewRequested()
    {
        var first = new DocumentTab("One.docx");
        var second = new DocumentTab("Two.docx");
        var (window, tabs) = Show([first, second]);
        var created = 0;
        tabs.NewRequested += () => created++;

        Click(window, Views(tabs).First(v => v.Model == second));
        Assert.Same(second, tabs.Selected);

        Click(window, ControlHarness.Part<Button>(tabs, "PART_New"));
        Assert.Equal(1, created);
    }

    [AvaloniaFact]
    public void AddingATabKeepsTheKeyboardFocus()
    {
        var first = new DocumentTab("One.docx");
        var (window, tabs) = Show([first, new DocumentTab("Two.docx")]);
        var close = ControlHarness.Part<Button>(Views(tabs).First(v => v.Model == first), "PART_Close");
        ControlHarness.KeyboardFocus(window, close);
        Assert.True(close.IsFocused);

        tabs.Items.Add(new DocumentTab("Three.docx"));
        ControlHarness.Settle(window);

        Assert.True(close.IsFocused);
        Assert.Same(close, window.FocusManager!.GetFocusedElement());
    }

    [AvaloniaFact]
    public void RemovingTheSelectedTabSelectsANeighbour()
    {
        var a = new DocumentTab("A");
        var b = new DocumentTab("B");
        var c = new DocumentTab("C");
        var (_, tabs) = Show([a, b, c]);
        tabs.Selected = b;

        tabs.Items.Remove(b);

        Assert.Same(c, tabs.Selected);
        Assert.Equal(2, Views(tabs).Count());
    }

    [AvaloniaFact]
    public void TabsAreNotRound()
    {
        var (_, tabs) = Show([new DocumentTab("One.docx"), new DocumentTab("Two.docx")]);
        tabs.Selected = tabs.Items[0];

        Assert.All(Views(tabs), v => Assert.Equal(new CornerRadius(0), v.CornerRadius));
        Assert.All(Views(tabs), v => Assert.False(ControlHarness.HasShadow(v)));
    }
}
