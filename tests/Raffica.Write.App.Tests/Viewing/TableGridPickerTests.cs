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
using Raffica.Write.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Viewing;

/// <summary>The grid of Insert table (milestone 4.5a, Task 8): ten by eight cells, keys and pointer, the size in words, "More…" and Esc.</summary>
public sealed class TableGridPickerTests
{
    private sealed class Shown : IDisposable
    {
        public Shown()
        {
            Picker = new TableGridPicker(Chosen.Add, () => MoreCalls++, () => CloseCalls++);
            Window = new Window { Width = 400, Height = 400, Content = Picker };
            Window.Show();
            Picker.Focus();
            ViewHarness.Pump();
        }

        public List<TableSize> Chosen { get; } = [];

        public int MoreCalls { get; private set; }

        public int CloseCalls { get; private set; }

        public TableGridPicker Picker { get; }

        public Window Window { get; }

        public void Press(Key key)
        {
            Window.KeyPressQwerty(PhysicalKeyOf(key), RawInputModifiers.None);
            Window.KeyReleaseQwerty(PhysicalKeyOf(key), RawInputModifiers.None);
            ViewHarness.Pump();
        }

        public void Dispose() => Window.Close();

        private static PhysicalKey PhysicalKeyOf(Key key) => key switch
        {
            Key.Left => PhysicalKey.ArrowLeft,
            Key.Right => PhysicalKey.ArrowRight,
            Key.Up => PhysicalKey.ArrowUp,
            Key.Down => PhysicalKey.ArrowDown,
            Key.Enter => PhysicalKey.Enter,
            _ => PhysicalKey.Escape,
        };
    }

    [AvaloniaFact]
    public void TheGridHasTenByEightCellsEachWithTheNameOfItsSize()
    {
        using var shown = new Shown();
        var cells = shown.Picker.GetVisualDescendants().OfType<Border>().Where(b => b.Name?.StartsWith("PART_Cell_", StringComparison.Ordinal) == true).ToList();
        Assert.Equal(80, cells.Count);
        Assert.Equal("3 by 4 table", AutomationProperties.GetName(cells.Single(c => c.Name == "PART_Cell_3_4")));
        Assert.Equal("10 by 8 table", AutomationProperties.GetName(cells.Single(c => c.Name == "PART_Cell_10_8")));
        Assert.Equal(WriteStrings.TableGridPopupName, AutomationProperties.GetName(shown.Picker));
        Assert.Equal("1 by 1 table", shown.Picker.SizeText);
    }

    [AvaloniaFact]
    public void TheArrowKeysMoveTheChoiceAndStayInsideTheGrid()
    {
        using var shown = new Shown();
        shown.Press(Key.Right);
        shown.Press(Key.Right);
        shown.Press(Key.Down);
        Assert.Equal((3, 2), (shown.Picker.Columns, shown.Picker.Rows));
        Assert.Equal("3 by 2 table", AutomationProperties.GetItemStatus(shown.Picker));

        for (var i = 0; i < 20; i++)
        {
            shown.Press(Key.Right);
            shown.Press(Key.Down);
        }

        Assert.Equal((10, 8), (shown.Picker.Columns, shown.Picker.Rows));
        for (var i = 0; i < 20; i++)
        {
            shown.Press(Key.Left);
            shown.Press(Key.Up);
        }

        Assert.Equal((1, 1), (shown.Picker.Columns, shown.Picker.Rows));
        Assert.Empty(shown.Chosen);
    }

    [AvaloniaFact]
    public void EnterChoosesTheLitSizeAndEscapeClosesWithoutChoosing()
    {
        using var shown = new Shown();
        shown.Press(Key.Right);
        shown.Press(Key.Right);
        shown.Press(Key.Right);
        shown.Press(Key.Down);
        shown.Press(Key.Enter);
        Assert.Equal([new TableSize(4, 2)], shown.Chosen);

        shown.Press(Key.Escape);
        Assert.Equal(1, shown.CloseCalls);
        Assert.Single(shown.Chosen);
    }

    [AvaloniaFact]
    public void AClickOnACellChoosesItAndMoreAsksTheCard()
    {
        using var shown = new Shown();
        var cell = shown.Picker.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_Cell_5_3");
        var point = cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), shown.Window)!.Value;
        shown.Window.MouseMove(point);
        Assert.Equal((5, 3), (shown.Picker.Columns, shown.Picker.Rows));
        shown.Window.MouseDown(point, MouseButton.Left);
        shown.Window.MouseUp(point, MouseButton.Left);
        ViewHarness.Pump();
        Assert.Equal([new TableSize(5, 3)], shown.Chosen);

        shown.Picker.MoreButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, shown.MoreCalls);
        Assert.Equal(WriteStrings.TableGridMore, shown.Picker.MoreButton.Content);
    }

    [AvaloniaFact]
    public void LightClampsAndResetGoesBackToTheSmallestSize()
    {
        using var shown = new Shown();
        shown.Picker.Light(99, 0);
        Assert.Equal((10, 1), (shown.Picker.Columns, shown.Picker.Rows));
        shown.Picker.Reset();
        Assert.Equal((1, 1), (shown.Picker.Columns, shown.Picker.Rows));
    }
}
