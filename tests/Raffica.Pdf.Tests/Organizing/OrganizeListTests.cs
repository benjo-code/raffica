using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Core.Pages;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Organizing;
using Raffica.Pdf.Panes;
using Raffica.Pdf.Tests.Panes;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Organizing;

/// <summary>The list of the organize mode in a headless window next to a page view (pages-marked.pdf: six pages, the third one landscape).</summary>
internal sealed class OrganizeListFixture : IDisposable
{
    private OrganizeListFixture(PaneFixture<OrganizeList> inner) => Inner = inner;

    public PaneFixture<OrganizeList> Inner { get; }

    public OrganizeList List => Inner.Pane;

    public OrganizeSession Session => Inner.Tab.Organize;

    public PdfTab Tab => Inner.Tab;

    public Window Window => Inner.Window;

    // The window is tall enough for all six items of pages-marked.pdf, so a click on any of them lands on it.
    public static async Task<OrganizeListFixture> OpenAsync(string file = "pages-marked.pdf", bool showView = true, double height = 1800)
    {
        var inner = await PaneFixture<OrganizeList>.OpenAsync(
            file,
            (tab, view) =>
            {
                tab.Organize.Enter();
                return new OrganizeList(tab, view, tab.Organize);
            },
            showView);
        inner.Window.Height = height;
        ViewFixture.Pump();
        return new OrganizeListFixture(inner);
    }

    public int[] Order() => [.. Session.Plan.Slots.Select(s => s.SourceIndex)];

    public Control Item(int position) => List.ContainerOf(position) ?? throw new InvalidOperationException("item " + position + " is not realised");

    public void Click(int position, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var point = Center(Item(position));
        Window.MouseDown(point, MouseButton.Left, modifiers);
        Window.MouseUp(point, MouseButton.Left, modifiers);
        ViewFixture.Pump();
    }

    public Point Center(Control target) =>
        target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), Window) ?? throw new InvalidOperationException("not in the window");

    public void Key(Key key, PhysicalKey physical, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.KeyPress(key, modifiers, physical, null);
        ViewFixture.Pump();
    }

    public void Dispose() => Inner.Dispose();
}

public sealed class OrganizeListTests
{
    private static RawInputModifiers CommandKey => OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    [AvaloniaFact]
    public async Task ListShowsOneItemPerSlotInPlanOrder()
    {
        using var f = await OrganizeListFixture.OpenAsync();
        Assert.Equal(6, f.List.Items.Count);
        Assert.Equal([0, 1, 2, 3, 4, 5], f.List.Items.Select(i => i.Slot.SourceIndex));
        f.Session.Selection.Select(0);
        f.Session.MoveBy(2);
        ViewFixture.Pump();

        Assert.Equal(f.Order(), f.List.Items.Select(i => i.Slot.SourceIndex));
        Assert.Equal([1, 2, 0, 3, 4, 5], f.List.Items.Select(i => i.Slot.SourceIndex));
        Assert.Equal([0, 1, 2, 3, 4, 5], f.List.Items.Select(i => i.Position));
    }

    [AvaloniaFact]
    public async Task CounterNamesTheChangeAndIsALiveRegion()
    {
        using var f = await OrganizeListFixture.OpenAsync();
        Assert.Equal("6 pages", f.List.CounterText);
        for (var i = 0; i < 3; i++)
        {
            f.Session.Selection.Select(1);
            f.Session.Delete();
        }

        ViewFixture.Pump();
        Assert.Equal("6 → 3 pages", f.List.CounterText);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(f.List.Counter));
        Assert.Equal("6 → 3 pages", AutomationProperties.GetName(f.List.Counter));
        Assert.Contains("3", f.List.GoneText);
        f.Session.Undo();
        ViewFixture.Pump();
        Assert.Equal("6 → 4 pages", f.List.CounterText);
    }

    [AvaloniaFact]
    public async Task PendingStateIsIconPlusTextAndHasAnAutomationName()
    {
        using var f = await OrganizeListFixture.OpenAsync();
        f.Session.Selection.Select(1);
        f.Session.Rotate(90);
        f.Session.InsertBlank(0, new BlankPage(595, 842));
        var inserted = await f.Session.InsertFileAsync(TestData.Pdf("hello_world.pdf"), null, "all", 2);
        Assert.True(inserted.IsDone);
        f.Session.Selection.Select(4);
        f.Session.Duplicate();
        ViewFixture.Pump();

        // The items are read from the list, so the test does not depend on where each change ended up.
        var items = f.List.Items;
        var turned = items.First(i => i.Slot.ExtraRotation == 90);
        var blank = items.First(i => i.Slot.Blank is not null);
        var file = items.First(i => i.Slot.Source.Value != 0);
        var copy = items.First(i => i.IsCopy);
        Assert.Contains(turned.Marks, m => m.Text == "Turned 90°");
        Assert.Contains(blank.Marks, m => m.Text.StartsWith("New blank page", StringComparison.Ordinal));
        Assert.Contains(file.Marks, m => m.Text == "From hello_world.pdf, page 1");
        Assert.Contains(copy.Marks, m => m.Text == "Copy");

        foreach (var item in new[] { turned, blank, file, copy })
        {
            f.List.List.ScrollIntoView(item.Position);
            ViewFixture.Pump();
            var container = f.Item(item.Position);
            var view = container.GetVisualDescendants().OfType<OrganizeItemView>().Single();
            Assert.Equal(item.Marks.Count, view.GetVisualDescendants().OfType<StrokeIcon>().Count());
            var name = AutomationProperties.GetName(container);
            foreach (var mark in item.Marks)
            {
                Assert.Contains(mark.Text, name, StringComparison.Ordinal);
            }
        }
    }

    [AvaloniaFact]
    public async Task DeletedPagesAreNotListedAndUndoBringsThemBack()
    {
        using var f = await OrganizeListFixture.OpenAsync();
        f.Session.Selection.Select(2);
        f.Session.Delete();
        ViewFixture.Pump();
        Assert.Equal(5, f.List.Items.Count);
        Assert.DoesNotContain(f.List.Items, i => i.Slot.SourceIndex == 2);

        f.Session.Undo();
        ViewFixture.Pump();
        Assert.Equal(6, f.List.Items.Count);
        Assert.Contains(f.List.Items, i => i.Slot.SourceIndex == 2);
    }

    [AvaloniaFact]
    public async Task MultiSelectWithCtrlShiftAndSelectAll()
    {
        using var f = await OrganizeListFixture.OpenAsync();
        f.Click(0);
        Assert.Equal([0], f.Session.Selection.Positions);
        f.Click(2, CommandKey);
        Assert.Equal([0, 2], f.Session.Selection.Positions);
        f.Click(2, CommandKey);
        Assert.Equal([0], f.Session.Selection.Positions);
        f.Click(0);
        f.Click(3, RawInputModifiers.Shift);
        Assert.Equal([0, 1, 2, 3], f.Session.Selection.Positions);
        f.Click(1, CommandKey);
        Assert.Equal([0, 2, 3], f.Session.Selection.Positions);

        Assert.True(f.Item(1).Focus());
        f.Key(Key.A, PhysicalKey.A, CommandKey);
        Assert.Equal([0, 1, 2, 3, 4, 5], f.Session.Selection.Positions);
        Assert.Equal(6, f.List.List.Selection.SelectedIndexes.Count);
    }

    [AvaloniaFact]
    public async Task DragReordersAMultiSelectionKeepingItsOrder()
    {
        using var f = await OrganizeListFixture.OpenAsync();
        f.Click(0);
        f.Click(2, CommandKey);
        Assert.Equal([0, 2], f.Session.Selection.Positions);

        // Press on a selected page, move below the middle of the third item (the gap before the fourth), release.
        var from = f.Center(f.Item(0));
        var target = f.Center(f.Item(2)) + new Point(0, 20);
        f.Window.MouseDown(from, MouseButton.Left);
        f.Window.MouseMove(from + new Point(0, 12), RawInputModifiers.LeftMouseButton);
        f.Window.MouseMove(target, RawInputModifiers.LeftMouseButton);
        ViewFixture.Pump();
        Assert.Equal(3, f.List.DropGap);
        Assert.True(f.List.Marker.IsVisible);
        f.Window.MouseUp(target, MouseButton.Left);
        ViewFixture.Pump();

        Assert.Equal([1, 0, 2, 3, 4, 5], f.Order());
        Assert.Equal([1, 2], f.Session.Selection.Positions);
        Assert.Null(f.List.DropGap);
        Assert.False(f.List.Marker.IsVisible);
    }

    [AvaloniaFact]
    public async Task DragEndingWhereItStartedChangesNothing()
    {
        using var f = await OrganizeListFixture.OpenAsync();
        f.Click(1);
        var version = f.Session.Version;
        var from = f.Center(f.Item(1));
        f.Window.MouseDown(from, MouseButton.Left);
        f.Window.MouseMove(from + new Point(0, 30), RawInputModifiers.LeftMouseButton);
        f.Window.MouseMove(from + new Point(0, 4), RawInputModifiers.LeftMouseButton);
        f.Window.MouseUp(from + new Point(0, 4), MouseButton.Left);
        ViewFixture.Pump();

        Assert.Equal(version, f.Session.Version);
        Assert.Equal([0, 1, 2, 3, 4, 5], f.Order());

        // A multi-selection pressed and released without a move collapses to the pressed page, as a click does.
        f.Click(0);
        f.Click(2, CommandKey);
        f.Click(0);
        Assert.Equal([0], f.Session.Selection.Positions);
        Assert.True(OrganizeDrag.IsNoOp([2, 3], 2));
        Assert.True(OrganizeDrag.IsNoOp([2, 3], 4));
        Assert.False(OrganizeDrag.IsNoOp([2, 3], 5));
        Assert.False(OrganizeDrag.IsNoOp([2, 4], 3));
    }

    [AvaloniaFact]
    public async Task AltArrowsMoveBySteps()
    {
        using var f = await OrganizeListFixture.OpenAsync();
        f.Click(2);
        f.Key(Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.Alt);
        Assert.Equal([0, 1, 3, 2, 4, 5], f.Order());
        Assert.Equal([3], f.Session.Selection.Positions);
        f.Key(Key.Up, PhysicalKey.ArrowUp, RawInputModifiers.Alt);
        f.Key(Key.Up, PhysicalKey.ArrowUp, RawInputModifiers.Alt);
        Assert.Equal([0, 2, 1, 3, 4, 5], f.Order());
        Assert.Equal([1], f.Session.Selection.Positions);

        // A selection with a gap is gathered at the destination, in its order: the same as a drop of it.
        f.Session.Selection.Set(6, [1, 3]);
        f.Key(Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.Alt);
        Assert.Equal([0, 1, 4, 2, 3, 5], f.Order());
        Assert.Equal([3, 4], f.Session.Selection.Positions);
    }

    [AvaloniaFact]
    public async Task AltHomeEndGoToTheEnds()
    {
        using var f = await OrganizeListFixture.OpenAsync();
        f.Click(3);
        f.Key(Key.Home, PhysicalKey.Home, RawInputModifiers.Alt);
        Assert.Equal([3, 0, 1, 2, 4, 5], f.Order());
        Assert.Equal([0], f.Session.Selection.Positions);
        f.Key(Key.End, PhysicalKey.End, RawInputModifiers.Alt);
        Assert.Equal([0, 1, 2, 4, 5, 3], f.Order());
        Assert.Equal([5], f.Session.Selection.Positions);
    }

    [AvaloniaFact]
    public async Task DeleteKeyDeletesAndTheLastPageIsRefusedWithAReason()
    {
        using var f = await OrganizeListFixture.OpenAsync();
        f.Click(1);
        f.Key(Key.Delete, PhysicalKey.Delete);
        Assert.Equal([0, 2, 3, 4, 5], f.Order());
        Assert.Null(f.List.NoticeText);

        // One page at a time down to the last one: the last one is refused with a reason, and nothing changes.
        for (var i = 0; i < 4; i++)
        {
            f.Session.Selection.Select(0);
            f.Key(Key.Delete, PhysicalKey.Delete);
        }

        Assert.Single(f.Order());
        f.Session.Selection.Select(0);
        f.Key(Key.Delete, PhysicalKey.Delete);
        Assert.Single(f.Order());
        Assert.Equal("The last page cannot be deleted. Nothing was changed.", f.List.NoticeText);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(f.List.Notice));

        // The next change that works takes the notice away.
        f.Session.Undo();
        f.Session.Selection.Select(0);
        f.Key(Key.Delete, PhysicalKey.Delete);
        Assert.Null(f.List.NoticeText);
    }

    [AvaloniaFact]
    public async Task BracketKeysRotate()
    {
        using var f = await OrganizeListFixture.OpenAsync();
        f.Click(1);
        f.Key(Key.OemCloseBrackets, PhysicalKey.BracketRight);
        Assert.Equal(90, f.Session.Plan.Slots[1].ExtraRotation);
        f.Key(Key.OemOpenBrackets, PhysicalKey.BracketLeft);
        f.Key(Key.OemOpenBrackets, PhysicalKey.BracketLeft);
        Assert.Equal(270, f.Session.Plan.Slots[1].ExtraRotation);
        f.Key(Key.D, PhysicalKey.D, CommandKey);
        Assert.Equal(7, f.Session.Plan.Slots.Count);
    }

    [AvaloniaFact]
    public async Task CommandDDuplicatesWhenTheCommandModifierIsMeta()
    {
        using var f = await OrganizeListFixture.OpenAsync();
        f.List.CommandModifier = KeyModifiers.Meta;
        f.Click(1);
        f.Key(Key.D, PhysicalKey.D, RawInputModifiers.Control);
        Assert.Equal(6, f.Session.Plan.Slots.Count);
        f.Key(Key.D, PhysicalKey.D, RawInputModifiers.Meta);
        Assert.Equal(7, f.Session.Plan.Slots.Count);
    }

    [AvaloniaFact]
    public async Task CommandADoesSelectAllWhenTheCommandModifierIsMeta()
    {
        using var f = await OrganizeListFixture.OpenAsync();
        f.List.CommandModifier = KeyModifiers.Meta;
        f.Click(1);
        f.Key(Key.A, PhysicalKey.A, RawInputModifiers.Meta);
        Assert.Equal(f.Session.Plan.Slots.Count, f.Session.Selection.Count);
    }

    [AvaloniaFact]
    public async Task TheDefaultCommandModifierIsMetaOnMacAndControlElsewhere()
    {
        using var f = await OrganizeListFixture.OpenAsync();
        Assert.Equal(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control, f.List.CommandModifier);
    }

    [AvaloniaFact]
    public async Task BracketKeysRotateWithAltGrOnGermanLayouts()
    {
        using var f = await OrganizeListFixture.OpenAsync();
        f.Click(1);

        // AltGr+9 and AltGr+8 reach the list as Control+Alt with the character in the key symbol.
        var altGr = RawInputModifiers.Control | RawInputModifiers.Alt;
        f.Window.KeyPress(Key.D9, altGr, PhysicalKey.Digit9, "]");
        ViewFixture.Pump();
        Assert.Equal(90, f.Session.Plan.Slots[1].ExtraRotation);
        f.Window.KeyPress(Key.D8, altGr, PhysicalKey.Digit8, "[");
        ViewFixture.Pump();
        Assert.Equal(0, f.Session.Plan.Slots[1].ExtraRotation);

        // Other keys with Control+Alt, and a bracket with Control alone, do nothing.
        var version = f.Session.Version;
        f.Window.KeyPress(Key.Up, altGr, PhysicalKey.ArrowUp, null);
        f.Window.KeyPress(Key.D, altGr, PhysicalKey.D, "d");
        f.Window.KeyPress(Key.D9, RawInputModifiers.Control, PhysicalKey.Digit9, "]");
        ViewFixture.Pump();
        Assert.Equal(version, f.Session.Version);

        // The keys that already worked still do (Alt+Down moves, a plain bracket with its character turns).
        f.Key(Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.Alt);
        Assert.Equal([0, 2, 1, 3, 4, 5], f.Order());
        f.Window.KeyPress(Key.OemCloseBrackets, RawInputModifiers.None, PhysicalKey.BracketRight, "]");
        ViewFixture.Pump();
        Assert.Equal(90, f.Session.Plan.Slots[2].ExtraRotation);
    }

    [AvaloniaFact]
    public async Task FocusStaysOnItsRowAfterSelectAllAndMoves()
    {
        using var f = await OrganizeListFixture.OpenAsync("thousand-pages.pdf");
        var list = f.List.List;
        list.ScrollIntoView(500);
        ViewFixture.Pump();
        f.Click(500);
        Assert.Equal(500, NavList.FocusedIndex(list));

        f.Key(Key.A, PhysicalKey.A, CommandKey);
        Assert.Equal(1000, f.Session.Selection.Count);
        Assert.Equal(500, NavList.FocusedIndex(list));
        Assert.Contains(500, f.List.RealizedPositions);
        Assert.DoesNotContain(0, f.List.RealizedPositions);

        // A block with the focus on its middle page: the move keeps the focus inside the block, on the same row.
        f.Session.Selection.Set(1000, [499, 500, 501, 502]);
        Assert.True(f.Item(501).Focus());
        f.Key(Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.Alt);
        Assert.Equal([500, 501, 502, 503], f.Session.Selection.Positions);
        Assert.Equal(501, NavList.FocusedIndex(list));
        Assert.Contains(501, f.List.RealizedPositions);

        // A single page: the focus follows it.
        list.ScrollIntoView(300);
        ViewFixture.Pump();
        f.Click(300);
        f.Key(Key.Down, PhysicalKey.ArrowDown, RawInputModifiers.Alt);
        Assert.Equal([301], f.Session.Selection.Positions);
        Assert.Equal(301, NavList.FocusedIndex(list));
    }

    [AvaloniaFact]
    public async Task EscapeCollapsesTheSelectionToTheFocusAndNeverLeavesTheMode()
    {
        using var f = await OrganizeListFixture.OpenAsync();
        f.Click(0);
        f.Click(3, RawInputModifiers.Shift);
        Assert.Equal(4, f.Session.Selection.Count);
        Assert.True(f.Item(2).Focus());
        f.Key(Key.Escape, PhysicalKey.Escape);
        Assert.Equal([2], f.Session.Selection.Positions);
        Assert.True(f.Session.IsActive);
    }

    [AvaloniaFact]
    public async Task TabLeavesTheListInsteadOfTrappingTheFocus()
    {
        using var f = await OrganizeListFixture.OpenAsync();
        var after = new Button { Content = "after", Focusable = true };
        f.Inner.Grid.Children.Add(after);
        Grid.SetColumn(after, 1);
        ViewFixture.Pump();
        Assert.True(f.Item(1).Focus());
        f.Key(Key.Tab, PhysicalKey.Tab);

        var focused = TopLevel.GetTopLevel(f.List)!.FocusManager!.GetFocusedElement() as Visual;
        Assert.NotNull(focused);
        Assert.False(f.List.IsVisualAncestorOf(focused));
    }

    [AvaloniaFact]
    public async Task ThousandPageListRealisesOnlyTheVisibleItems()
    {
        using var f = await OrganizeListFixture.OpenAsync("thousand-pages.pdf");
        Assert.Equal(1000, f.List.Items.Count);
        Assert.Equal(1000, f.List.List.ItemCount);
        Assert.InRange(f.List.RealizedPositions.Count, 1, 40);
        f.List.List.ScrollIntoView(700);
        ViewFixture.Pump();
        Assert.Contains(700, f.List.RealizedPositions);
        Assert.InRange(f.List.RealizedPositions.Count, 1, 40);
    }

    [AvaloniaFact]
    public async Task GermanTextsFitTheItems()
    {
        PdfStrings.Culture = System.Globalization.CultureInfo.GetCultureInfo("de");
        using var f = await OrganizeListFixture.OpenAsync();
        f.Session.Selection.Select(1);
        f.Session.Rotate(90);
        f.Session.InsertBlank(0, new BlankPage(595, 842));
        var inserted = await f.Session.InsertFileAsync(TestData.Pdf("hello_world.pdf"), null, "all", 3);
        Assert.True(inserted.IsDone);
        f.Session.Selection.Select(5);
        f.Session.Duplicate();
        ViewFixture.Pump();

        Assert.Contains(f.List.Items.SelectMany(i => i.Marks), m => m.Text == "Um 90° gedreht");
        Assert.Contains(f.List.Items.SelectMany(i => i.Marks), m => m.Text == "Kopie");
        var viewport = f.List.List.Bounds.Width;
        Assert.InRange(viewport, 100, 400);
        foreach (var position in f.List.RealizedPositions)
        {
            var view = f.Item(position).GetVisualDescendants().OfType<OrganizeItemView>().Single();
            view.Measure(new Size(viewport - 24, double.PositiveInfinity));
            Assert.True(view.DesiredSize.Width <= viewport - 24 + 0.5, "item " + position + " is " + view.DesiredSize.Width + " wide in " + viewport);
        }

        Assert.Equal("6 → 9 Seiten", f.List.CounterText);
    }
}
