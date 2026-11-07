using Avalonia;
using Avalonia.Headless.XUnit;
using Raffica.Core.Text;
using Raffica.Pdf.Redacting;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Redacting;

public sealed class RedactInteractionTests
{
    private static PageRect AreaOf(RedactFixture f, Guid id) => f.Session.Plan.Marks.Single(m => m.Id == id).Areas[0];

    [AvaloniaFact]
    public async Task ADragInsideAnAreaMovesItAsOneStep()
    {
        using var f = await RedactFixture.OpenAsync();
        var id = f.Session.AddArea(0, new PageRect(100, 100, 200, 160)).MarkId!.Value;
        f.Session.Selection.Clear();

        f.Drag(f.At(0, 150, 130), f.At(0, 180, 150));

        var moved = AreaOf(f, id);
        Assert.InRange(moved.Left, 128, 132);
        Assert.InRange(moved.Top, 118, 122);
        Assert.InRange(moved.Width, 99.9, 100.1);
        Assert.Single(f.Session.Plan.Marks);
        Assert.Equal(id, f.Session.Selection.Single);

        // One undo puts it back; a second removes the mark.
        f.Session.Undo();
        Assert.Equal(new PageRect(100, 100, 200, 160), AreaOf(f, id));
        f.Session.Undo();
        Assert.Empty(f.Session.Plan.Marks);
    }

    [AvaloniaFact]
    public async Task ADragOnACornerHandleResizes()
    {
        using var f = await RedactFixture.OpenAsync();
        var id = f.Session.AddArea(0, new PageRect(100, 100, 200, 160)).MarkId!.Value;

        f.Drag(f.At(0, 200, 160), f.At(0, 240, 190));

        var resized = AreaOf(f, id);
        Assert.InRange(resized.Left, 99.9, 100.1);
        Assert.InRange(resized.Top, 99.9, 100.1);
        Assert.InRange(resized.Right, 238, 242);
        Assert.InRange(resized.Bottom, 188, 192);
        Assert.Single(f.Session.Plan.Marks);
    }

    [AvaloniaFact]
    public async Task AClickOnATextMarkSelectsItAndADragDoesNotMoveIt()
    {
        using var f = await RedactFixture.OpenAsync();
        f.Session.Tool = RedactTool.Text;
        var text = await f.Text.TextOfAsync(0);
        var (start, end) = text.LineAt(0);
        await f.SelectAsync(0, start, end - 1);
        Assert.True((await f.Session.MarkSelectionAsync()).IsDone);
        var mark = f.Session.Plan.Marks[0];
        f.Session.Selection.Clear();
        f.Session.Tool = RedactTool.Area;

        var area = mark.Areas[0];
        var inside = f.At(0, (area.Left + area.Right) / 2, (area.Top + area.Bottom) / 2);
        f.Drag(inside, new Point(inside.X + 40, inside.Y + 30));

        Assert.Equal(mark.Areas[0], f.Session.Plan.Marks.Single(m => m.Id == mark.Id).Areas[0]);
        Assert.Single(f.Session.Plan.Marks);
        Assert.Equal(mark.Id, f.Session.Selection.Single);
    }

    [AvaloniaFact]
    public async Task TheAreaToolYieldsTheTextSelectionAndTheTextToolDoesNot()
    {
        using var f = await RedactFixture.OpenAsync();
        var text = await f.Text.TextOfAsync(0);
        var (start, end) = text.LineAt(0);

        // Area tool: a drag across a line makes an area and no text selection.
        var from = f.Text.CharPoint(text, start, right: false);
        var to = f.Text.CharPoint(text, end - 1, right: true);
        f.Drag(from, new Point(to.X, to.Y + 40));
        Assert.Single(f.Session.Plan.Marks);
        Assert.False(f.Text.Text.Selection.HasSelection);

        // Text tool: the same drag selects text and makes no mark.
        f.Session.Tool = RedactTool.Text;
        f.Session.Selection.Clear();
        f.Drag(f.Text.CharPoint(text, start, right: false), f.Text.CharPoint(text, end - 1, right: true));
        Assert.Single(f.Session.Plan.Marks);
        Assert.True(f.Text.Text.Selection.HasSelection);
    }

    [AvaloniaFact]
    public async Task NothingHappensWhileTheModeIsOff()
    {
        using var f = await RedactFixture.OpenAsync(enter: false);
        f.Drag(f.At(0, 60, 120), f.At(0, 200, 170));
        Assert.Empty(f.Session.Plan.Marks);
        Assert.False(f.Press(Avalonia.Input.Key.Escape));
        ViewFixture.Pump();
    }
}
