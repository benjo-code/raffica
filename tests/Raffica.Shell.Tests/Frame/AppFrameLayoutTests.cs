using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Raffica.Shell.Tests.Controls;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

public sealed class AppFrameLayoutTests
{
    private static Border Block(double height = 10) => new() { Height = height };

    private static (Window Window, AppFrame Frame, Border Title, Border Tabs, Border Band, Border Doc, Border Status) Show(bool compact = false)
    {
        var title = Block();
        var tabs = Block();
        var band = Block();
        var doc = new Border();
        var status = Block();
        var frame = new AppFrame
        {
            TitleBar = title,
            TabRow = tabs,
            Band = band,
            Document = doc,
            StatusBar = status,
            IsCompact = compact,
        };
        var window = ControlHarness.Show(frame, ThemeMode.Light);
        return (window, frame, title, tabs, band, doc, status);
    }

    [AvaloniaFact]
    public void RowsHaveTheSpecHeights()
    {
        var (_, frame, title, tabs, band, doc, status) = Show();

        Assert.Equal(36, Row(title).Height, 0.01);
        Assert.Equal(32, Row(tabs).Height, 0.01);
        Assert.Equal(82, Row(band).Height, 0.01);
        Assert.Equal(30, Row(status).Height, 0.01);
        Assert.Equal(320 - 36 - 32 - 82 - 30, doc.Bounds.Height, 0.01);
        Assert.Equal(320, frame.Bounds.Height, 0.01);

        frame.IsCompact = true;
        ControlHarness.Settle((Window)TopLevel.GetTopLevel(frame)!);
        Assert.Equal(40, Row(band).Height, 0.01);
    }

    [AvaloniaFact]
    public void NotificationAndStatusCollapseWhenNull()
    {
        var (window, frame, _, _, _, doc, status) = Show();
        Assert.Equal(320 - 36 - 32 - 82 - 30, doc.Bounds.Height, 0.01);

        frame.StatusBar = null;
        ControlHarness.Settle(window);
        Assert.Equal(320 - 36 - 32 - 82, doc.Bounds.Height, 0.01);

        var note = Block(40);
        frame.Notification = note;
        ControlHarness.Settle(window);
        Assert.Equal(320 - 36 - 32 - 82 - 40, doc.Bounds.Height, 0.01);

        frame.Notification = null;
        frame.StatusBar = status;
        ControlHarness.Settle(window);
        Assert.Equal(320 - 36 - 32 - 82 - 30, doc.Bounds.Height, 0.01);
    }

    [AvaloniaFact]
    public void TabRowAndBandCollapseWhenNullAndTheTitleBarKeepsItsRow()
    {
        var (window, frame, _, tabs, band, doc, _) = Show();

        frame.TabRow = null;
        frame.Band = null;
        ControlHarness.Settle(window);

        // The start view of an app has no tab row and no band: the document row starts right under the 36 px title bar.
        Assert.Equal(36, doc.TranslatePoint(new Point(0, 0), window)!.Value.Y, 0.01);
        Assert.Equal(320 - 36 - 30, doc.Bounds.Height, 0.01);

        frame.TabRow = tabs;
        frame.Band = band;
        ControlHarness.Settle(window);
        Assert.Equal(36 + 32 + 82, doc.TranslatePoint(new Point(0, 0), window)!.Value.Y, 0.01);
    }

    [AvaloniaFact]
    public void AClosedPaneCollapsesItsSlotAndItsRule()
    {
        var left = new Pane { Title = "Navigation", Width = 100, Content = new Border() };
        var right = new Pane { Title = "Comments", Width = 120, Content = new Border() };
        var frame = new AppFrame { TitleBar = Block(), Document = new Border(), LeftPane = left, RightPane = right };
        var window = ControlHarness.Show(frame, ThemeMode.Light);
        var document = ControlHarness.Part<Control>(frame, "PART_Document");
        var leftSlot = ControlHarness.Part<Control>(frame, "PART_LeftPaneRow");
        var rightSlot = ControlHarness.Part<Control>(frame, "PART_RightPaneRow");
        Assert.True(leftSlot.IsVisible && rightSlot.IsVisible);
        Assert.Equal(480 - leftSlot.Bounds.Width - rightSlot.Bounds.Width, document.Bounds.Width, 0.01);

        left.IsOpen = false;
        right.IsOpen = false;
        ControlHarness.Settle(window);

        Assert.False(leftSlot.IsVisible);
        Assert.False(rightSlot.IsVisible);
        Assert.Equal(0, document.Bounds.X, 0.01);
        Assert.Equal(480, document.Bounds.Width, 0.01);

        left.IsOpen = true;
        ControlHarness.Settle(window);
        Assert.True(leftSlot.IsVisible);
        Assert.Equal(leftSlot.Bounds.Width, document.Bounds.X, 0.01);

        // Taking a pane out of the slot, and putting a new one in, follows the same rule.
        frame.LeftPane = null;
        ControlHarness.Settle(window);
        Assert.False(leftSlot.IsVisible);
        var other = new Pane { Title = "Other", Width = 80, Content = new Border(), IsOpen = false };
        frame.LeftPane = other;
        ControlHarness.Settle(window);
        Assert.False(leftSlot.IsVisible);
        other.IsOpen = true;
        ControlHarness.Settle(window);
        Assert.True(leftSlot.IsVisible);
    }

    // The height of the grid row that holds the control (the row, not the control: a control may be smaller than its row).
    private static Rect Row(Control c) => ((Control)c.Parent!).Bounds;
}
