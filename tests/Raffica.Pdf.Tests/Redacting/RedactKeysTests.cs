using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Core.Text;
using Raffica.Pdf.Redacting;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Redacting;

public sealed class RedactKeysTests
{
    private static PageRect AreaOf(RedactFixture f, Guid id) => f.Session.Plan.Marks.Single(m => m.Id == id).Areas[0];

    [AvaloniaFact]
    public async Task ArrowsMoveByOnePointAndShiftResizes()
    {
        using var f = await RedactFixture.OpenAsync();
        var id = f.Session.AddArea(0, new PageRect(100, 100, 160, 140)).MarkId!.Value;

        // Arrows move the selected area by one point; a run of them within 400 ms is one step.
        Assert.True(f.Press(Key.Right));
        f.Time.Advance(300);
        Assert.True(f.Press(Key.Right));
        f.Time.Advance(300);
        Assert.True(f.Press(Key.Down));
        Assert.Equal(new PageRect(102, 101, 162, 141), AreaOf(f, id));

        // The pause ends the run: the next arrow is a step of its own.
        f.Time.Advance(500);
        Assert.True(f.Press(Key.Left));
        Assert.Equal(new PageRect(101, 101, 161, 141), AreaOf(f, id));
        f.Session.Undo();
        Assert.Equal(new PageRect(102, 101, 162, 141), AreaOf(f, id));
        f.Session.Undo();
        Assert.Equal(new PageRect(100, 100, 160, 140), AreaOf(f, id));
        f.Session.Undo();
        Assert.Empty(f.Session.Plan.Marks);

        // Shift with an arrow grows or shrinks the area by one point on its right or bottom edge.
        var second = f.Session.AddArea(0, new PageRect(100, 100, 160, 140)).MarkId!.Value;
        f.Time.Advance(1000);
        Assert.True(f.Press(Key.Right, KeyModifiers.Shift));
        Assert.True(f.Press(Key.Down, KeyModifiers.Shift));
        Assert.Equal(new PageRect(100, 100, 161, 141), AreaOf(f, second));
        f.Time.Advance(1000);
        Assert.True(f.Press(Key.Left, KeyModifiers.Shift));
        Assert.True(f.Press(Key.Up, KeyModifiers.Shift));
        Assert.Equal(new PageRect(100, 100, 160, 140), AreaOf(f, second));
    }

    [AvaloniaFact]
    public async Task ArrowsAreLeftAloneWithoutAnAreaToMove()
    {
        using var f = await RedactFixture.OpenAsync();
        Assert.False(f.Press(Key.Right));
        Assert.False(f.Press(Key.Delete));

        // A text mark does not move: the arrow goes on to scroll the page.
        f.Session.Tool = RedactTool.Text;
        var text = await f.Text.TextOfAsync(0);
        var (start, end) = text.LineAt(0);
        await f.SelectAsync(0, start, end - 1);
        Assert.True((await f.Session.MarkSelectionAsync()).IsDone);
        Assert.False(f.Press(Key.Right));
    }

    [AvaloniaFact]
    public async Task TabWalksMarksInReadingOrderAndNeverTraps()
    {
        using var f = await RedactFixture.OpenAsync();
        Assert.False(f.Press(Key.Tab), "no marks: the focus moves on");

        var low = f.Session.AddArea(0, new PageRect(50, 300, 120, 330)).MarkId!.Value;
        var right = f.Session.AddArea(0, new PageRect(200, 100, 260, 130)).MarkId!.Value;
        var left = f.Session.AddArea(0, new PageRect(50, 100, 110, 130)).MarkId!.Value;
        f.Session.Selection.Clear();

        Assert.True(f.Press(Key.Tab));
        Assert.Equal(left, f.Session.Selection.Single);
        Assert.True(f.Press(Key.Tab));
        Assert.Equal(right, f.Session.Selection.Single);
        Assert.True(f.Press(Key.Tab));
        Assert.Equal(low, f.Session.Selection.Single);

        // Past the last mark the key is not used, so the focus leaves the page instead of being trapped.
        Assert.False(f.Press(Key.Tab));
        Assert.Equal(low, f.Session.Selection.Single);

        Assert.True(f.Press(Key.Tab, KeyModifiers.Shift));
        Assert.Equal(right, f.Session.Selection.Single);
        Assert.True(f.Press(Key.Tab, KeyModifiers.Shift));
        Assert.Equal(left, f.Session.Selection.Single);
        Assert.False(f.Press(Key.Tab, KeyModifiers.Shift));
        Assert.Equal(left, f.Session.Selection.Single);
    }

    [AvaloniaFact]
    public async Task EscUnwindsDragThenSelectionThenMode()
    {
        using var f = await RedactFixture.OpenAsync();
        var ends = 0;
        f.Interaction.EndRequested += () => ends++;

        // 1. A drag in progress: Esc drops the drag and its mark.
        f.Down(f.At(0, 60, 120));
        f.MoveTo(f.At(0, 140, 150));
        f.MoveTo(f.At(0, 200, 170));
        Assert.True(f.Interaction.IsDragging);
        Assert.Single(f.Session.Plan.Marks);
        Assert.True(f.Press(Key.Escape));
        Assert.False(f.Interaction.IsDragging);
        Assert.Empty(f.Session.Plan.Marks);
        f.Up(f.At(0, 200, 170));
        Assert.Empty(f.Session.Plan.Marks);
        Assert.Equal(0, ends);

        // 2. A selected mark: Esc drops the selection and keeps the mark.
        f.Session.AddArea(0, new PageRect(50, 100, 150, 140));
        Assert.Equal(1, f.Session.Selection.Count);
        Assert.True(f.Press(Key.Escape));
        Assert.Equal(0, f.Session.Selection.Count);
        Assert.Single(f.Session.Plan.Marks);
        Assert.Equal(0, ends);

        // 3. Nothing left to drop: Esc asks to leave the mode (the window asks about the marks).
        Assert.True(f.Press(Key.Escape));
        Assert.Equal(1, ends);
    }

    [AvaloniaFact]
    public async Task DeleteRemovesTheSelectedMarksInOneStep()
    {
        using var f = await RedactFixture.OpenAsync();
        var a = f.Session.AddArea(0, new PageRect(50, 100, 150, 140)).MarkId!.Value;
        var b = f.Session.AddArea(0, new PageRect(50, 200, 150, 240)).MarkId!.Value;
        f.Session.Selection.Set([a, b]);

        Assert.True(f.Press(Key.Delete));

        Assert.Empty(f.Session.Plan.Marks);
        f.Session.Undo();
        Assert.Equal(2, f.Session.Plan.Marks.Count);
    }

    [AvaloniaFact]
    public async Task CtrlEnterMarksTheTextSelectionWhileTheModeIsOn()
    {
        using var f = await RedactFixture.OpenAsync();
        f.Session.Tool = RedactTool.Text;
        var text = await f.Text.TextOfAsync(0);
        var (start, end) = text.LineAt(0);
        await f.SelectAsync(0, start, end - 1);

        Assert.True(f.Press(Key.Enter, KeyModifiers.Control));
        await f.Interaction.Completion;
        ViewFixture.Pump();

        var mark = Assert.Single(f.Session.Plan.Marks);
        Assert.Equal(Raffica.Core.Redaction.RedactionKind.Text, mark.Kind);
    }
}
