using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Core.Text;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.Text;
using Xunit;

namespace Raffica.Pdf.Tests.Text;

public sealed class SelectionTests
{
    [AvaloniaFact]
    public async Task DragSelectsTheTextBetweenTheCarets()
    {
        using var f = await TextFixture.OpenAsync("text-rich.pdf");
        var text = await f.TextOfAsync(0);
        var start = text.GetText(0, text.Chars.Count).IndexOf("Handbuch", StringComparison.Ordinal);
        var end = start + "Handbuch".Length;
        var selection = f.Text.Selection;

        selection.Press(f.CharPoint(text, start, right: false), 1);
        selection.Drag(f.CharPoint(text, end - 1, right: true));
        selection.Release();

        Assert.Equal(new TextSelection(new TextPosition(0, start), new TextPosition(0, end)), selection.Selection);
        Assert.True(selection.HasSelection);
        Assert.Equal("Handbuch", (await selection.GetTextAsync())!.Plain);
        Assert.NotEmpty(selection.RectsOn(0));
        Assert.Empty(selection.RectsOn(1));
    }

    [AvaloniaFact]
    public async Task DraggingBackwardsSelectsTheSameText()
    {
        using var f = await TextFixture.OpenAsync("text-rich.pdf");
        var text = await f.TextOfAsync(0);
        var start = text.GetText(0, text.Chars.Count).IndexOf("Handbuch", StringComparison.Ordinal);
        var end = start + "Handbuch".Length;

        f.Text.Selection.Press(f.CharPoint(text, end - 1, right: true), 1);
        f.Text.Selection.Drag(f.CharPoint(text, start, right: false));
        f.Text.Selection.Release();

        Assert.Equal(new TextPosition(0, start), f.Text.Selection.Selection!.Value.Start);
        Assert.Equal(new TextPosition(0, end), f.Text.Selection.Selection!.Value.End);
        Assert.Equal("Handbuch", (await f.Text.Selection.GetTextAsync())!.Plain);
    }

    [AvaloniaFact]
    public async Task AClickWithoutADragClearsTheSelection()
    {
        using var f = await TextFixture.OpenAsync("text-rich.pdf");
        var text = await f.TextOfAsync(0);
        var start = text.GetText(0, text.Chars.Count).IndexOf("Handbuch", StringComparison.Ordinal);
        f.Text.Selection.Press(f.CharPoint(text, start, right: false), 1);
        f.Text.Selection.Drag(f.CharPoint(text, start + 4, right: true));
        f.Text.Selection.Release();
        Assert.True(f.Text.Selection.HasSelection);

        f.Text.Selection.Press(f.CharPoint(text, start + 2, right: false), 1);
        f.Text.Selection.Release();

        Assert.False(f.Text.Selection.HasSelection);
        Assert.Empty(f.Text.Selection.RectsOn(0));
        Assert.Null(await f.Text.Selection.GetTextAsync());
    }

    [AvaloniaFact]
    public async Task DoubleClickSelectsAWord()
    {
        using var f = await TextFixture.OpenAsync("text-rich.pdf");
        var text = await f.TextOfAsync(0);
        var start = text.GetText(0, text.Chars.Count).IndexOf("Handbuch", StringComparison.Ordinal);
        var point = f.CharPoint(text, start + 3, right: false);

        // Two real clicks in a row at one place: the second one reports a click count of 2.
        f.Window.MouseDown(point, MouseButton.Left);
        f.Window.MouseUp(point, MouseButton.Left);
        f.Window.MouseDown(point, MouseButton.Left);
        f.Window.MouseUp(point, MouseButton.Left);
        ViewFixture.Pump();

        Assert.Equal(new TextSelection(new TextPosition(0, start), new TextPosition(0, start + "Handbuch".Length)), f.Text.Selection.Selection);
        Assert.Equal("Handbuch", (await f.Text.Selection.GetTextAsync())!.Plain);
    }

    [AvaloniaFact]
    public async Task TripleClickSelectsALine()
    {
        using var f = await TextFixture.OpenAsync("text-rich.pdf");
        var text = await f.TextOfAsync(0);
        var all = text.GetText(0, text.Chars.Count);
        var inLine = all.IndexOf("Zweite Zeile", StringComparison.Ordinal);
        var (lineStart, lineEnd) = text.LineAt(inLine);
        var point = f.CharPoint(text, inLine + 2, right: false);

        for (var i = 0; i < 3; i++)
        {
            f.Window.MouseDown(point, MouseButton.Left);
            f.Window.MouseUp(point, MouseButton.Left);
        }

        ViewFixture.Pump();

        Assert.Equal(new TextSelection(new TextPosition(0, lineStart), new TextPosition(0, lineEnd)), f.Text.Selection.Selection);
        Assert.Contains("Zweite Zeile", (await f.Text.Selection.GetTextAsync())!.Plain, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task SelectionAcrossTwoPagesFollowsReadingOrder()
    {
        using var f = await TextFixture.OpenAsync("text-rich.pdf", 800, 900, zoom: 0.5);
        var first = await f.TextOfAsync(0);
        var second = await f.TextOfAsync(1);
        var from = first.GetText(0, first.Chars.Count).IndexOf("Weiter", StringComparison.Ordinal);
        var toText = second.GetText(0, second.Chars.Count);
        var to = toText.IndexOf("Seite zwei", StringComparison.Ordinal) + "Seite zwei".Length;
        Assert.NotNull(f.View.PageBounds(1));

        f.Text.Selection.Press(f.CharPoint(first, from, right: false), 1);
        f.Text.Selection.Drag(f.CharPoint(second, to - 1, right: true));
        f.Text.Selection.Release();

        var forward = f.Text.Selection.Selection!.Value;
        Assert.Equal(new TextPosition(0, from), forward.Start);
        Assert.Equal(new TextPosition(1, to), forward.End);
        var plain = (await f.Text.Selection.GetTextAsync())!.Plain;
        Assert.StartsWith("Weiter zu Seite zwei", plain, StringComparison.Ordinal);
        Assert.EndsWith("Seite zwei", plain, StringComparison.Ordinal);
        Assert.NotEmpty(f.Text.Selection.RectsOn(0));
        Assert.NotEmpty(f.Text.Selection.RectsOn(1));

        // The same drag from the bottom to the top gives the same text.
        f.Text.Selection.Press(f.CharPoint(second, to - 1, right: true), 1);
        f.Text.Selection.Drag(f.CharPoint(first, from, right: false));
        f.Text.Selection.Release();
        Assert.Equal(plain, (await f.Text.Selection.GetTextAsync())!.Plain);
        Assert.Equal(new TextPosition(1, to), f.Text.Selection.Selection!.Value.Anchor);
    }

    [AvaloniaFact]
    public async Task SelectAllTakesTheTextOfTheShownPages()
    {
        using var f = await TextFixture.OpenAsync("text-rich.pdf", 800, 900, zoom: 0.5);
        f.Text.Selection.SelectAll();
        var plain = (await f.Text.Selection.GetTextAsync())!.Plain;
        Assert.Contains("Handbuch", plain, StringComparison.Ordinal);
        Assert.Contains("Ziel:", plain, StringComparison.Ordinal);
        Assert.NotEmpty(f.Text.Selection.RectsOn(0));
        Assert.NotEmpty(f.Text.Selection.RectsOn(1));
    }

    [AvaloniaFact]
    public async Task SelectAllOnlyTakesPagesThatAreShown()
    {
        using var f = await TextFixture.OpenAsync("text-dense.pdf", 800, 600);
        f.Text.Selection.SelectAll();
        var selection = f.Text.Selection.Selection!.Value;
        var (first, last) = f.View.VisiblePages;
        Assert.Equal(first, selection.Start.Page);
        Assert.Equal(last, selection.End.Page);
        Assert.True(last < 10);
    }

    [AvaloniaFact]
    public async Task OnAPageWhoseTextIsNotLoadedNothingHappensAndNothingThrows()
    {
        // The view is off screen: no sharp page was drawn, so nothing was fetched yet.
        using var inner = await ViewFixture.OpenAsync("text-rich.pdf", show: false);
        using var selection = new SelectionController(inner.Tab, inner.View);
        var point = inner.View.ToView(0, 60, 60)!.Value;

        selection.Press(point, 1);
        selection.Drag(inner.View.ToView(0, 300, 60)!.Value);
        selection.Release();
        selection.SelectAll();

        Assert.False(selection.HasSelection);
        Assert.Empty(selection.RectsOn(0));
        Assert.Null(await selection.GetTextAsync());
        Assert.False(selection.IsOverText(point));
    }

    [AvaloniaFact]
    public async Task PointsOutsideEveryPageDoNothing()
    {
        using var f = await TextFixture.OpenAsync("text-rich.pdf");
        f.Text.Selection.Press(new Point(2, 2), 1);
        f.Text.Selection.Drag(new Point(300, 300));
        f.Text.Selection.Release();
        Assert.False(f.Text.Selection.IsOverText(new Point(2, 2)));
        Assert.False(f.Text.Selection.HasSelection);
    }

    [AvaloniaFact]
    public async Task TheSelectionIsDrawn()
    {
        using var f = await TextFixture.OpenAsync("text-rich.pdf");
        var text = await f.TextOfAsync(0);
        var start = text.GetText(0, text.Chars.Count).IndexOf("Handbuch", StringComparison.Ordinal);
        var (before, stride) = f.CaptureView();
        f.Text.Selection.Press(f.CharPoint(text, start, right: false), 1);
        f.Text.Selection.Drag(f.CharPoint(text, start + 7, right: true));
        f.Text.Selection.Release();
        ViewFixture.Pump();
        var (after, _) = f.CaptureView();

        var rect = f.View.ToView(0, f.Text.Selection.RectsOn(0)[0])!.Value;
        var x = (int)(rect.Left + 1);
        var y = (int)(rect.Top + 1);
        var i = (y * stride) + (x * 4);
        Assert.NotEqual((before[i], before[i + 1], before[i + 2]), (after[i], after[i + 1], after[i + 2]));
    }
}
