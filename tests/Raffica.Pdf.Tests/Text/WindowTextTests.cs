using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using Raffica.Core.Text;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.Text;
using Xunit;

namespace Raffica.Pdf.Tests.Text;

/// <summary>Selection, copy and links as the window wires them for each tab (the keys themselves belong to the command catalog).</summary>
public sealed class WindowTextTests
{
    [AvaloniaFact]
    public async Task EachTabHasItsOwnSelectionAndCopyUsesTheActiveOne()
    {
        using var fixture = WindowFixture.Create();
        var first = await fixture.OpenAsync("text-rich.pdf");
        var second = await fixture.OpenAsync("text-dense.pdf");
        var window = fixture.Window;
        var firstText = window.TextOf(first)!;
        var secondText = window.TextOf(second)!;
        Assert.NotSame(firstText, secondText);

        await ViewFixture.WaitAsync(() => window.ViewOf(second)!.FirstSharpPageShown);
        await secondText.Selection.EnsureVisibleTextAsync();
        window.SelectAll();
        Assert.True(secondText.Selection.HasSelection);
        Assert.False(firstText.Selection.HasSelection);

        var clipboard = window.Clipboard!;
        await clipboard.ClearAsync();
        Assert.True(await window.CopySelectionAsync());
        Assert.Contains("alpha", (await clipboard.TryGetTextAsync())!, StringComparison.Ordinal);

        window.Workspace.Select(first);
        ViewFixture.Pump();
        await clipboard.ClearAsync();
        Assert.False(await window.CopySelectionAsync());
        Assert.Null(await clipboard.TryGetTextAsync());
    }

    [AvaloniaFact]
    public async Task AClickOnALinkShowsTheCardInTheWindowAndOpenWaitsForTheDelay()
    {
        var launched = new List<Uri>();
        var time = new ManualTime();
        using var fixture = WindowFixture.Create(launcher: launched.Add, time: time);
        var tab = await fixture.OpenAsync("text-rich.pdf");
        var window = fixture.Window;
        var view = window.ViewOf(tab)!;
        await ViewFixture.WaitAsync(() => view.FirstSharpPageShown);
        var text = window.TextOf(tab)!;
        await text.Selection.EnsureVisibleTextAsync();
        var pageText = await tab.Session.EnsurePageTexts()!.GetAsync(0, cancellationToken: TestContext.Current.CancellationToken);
        var link = pageText.Links.First(l => l.Target is UriTarget { Url: "https://example.org/docs" });
        var rect = link.Rects[0];
        var inView = view.ToView(0, (rect.Left + rect.Right) / 2, (rect.Top + rect.Bottom) / 2);
        Assert.NotNull(inView);
        var point = view.TranslatePoint(inView.Value, window)!.Value;

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        ViewFixture.Pump();

        var card = window.Host.ActiveDialog!;
        Assert.NotNull(card);
        Assert.Empty(launched);
        var open = card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Open");
        open.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(launched);
        time.Advance(500);
        open.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        ViewFixture.Pump();
        await ViewFixture.WaitAsync(() => launched.Count == 1);
        Assert.Equal("https://example.org/docs", launched[0].OriginalString);
    }

    [AvaloniaFact]
    public async Task ClosingATabDisposesItsTextInteraction()
    {
        using var fixture = WindowFixture.Create();
        var tab = await fixture.OpenAsync("text-rich.pdf");
        var window = fixture.Window;
        Assert.NotNull(window.TextOf(tab));
        Assert.True(await window.Workspace.CloseAsync(tab));
        ViewFixture.Pump();
        Assert.Null(window.TextOf(tab));
        Assert.Null(window.SearchOf(tab));
    }
}
