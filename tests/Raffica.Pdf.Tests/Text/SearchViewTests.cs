using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Core.Text;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.Text;
using Xunit;

namespace Raffica.Pdf.Tests.Text;

/// <summary>The search in the page view: the highlights drawn in the one pass, the scroll to the current hit, the bar and its keys.</summary>
public sealed class SearchViewTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    private static (byte B, byte G, byte R) PixelAt(byte[] pixels, int stride, int x, int y)
    {
        var i = (y * stride) + (x * 4);
        return (pixels[i], pixels[i + 1], pixels[i + 2]);
    }

    private static double Brightness(byte[] pixels, int stride, Rect box)
    {
        double sum = 0;
        var count = 0;
        for (var y = (int)box.Top; y < (int)box.Bottom; y++)
        {
            for (var x = (int)box.Left; x < (int)box.Right; x++)
            {
                var (b, g, r) = PixelAt(pixels, stride, x, y);
                sum += b + g + r;
                count++;
            }
        }

        return sum / Math.Max(1, count);
    }

    // The text box raises TextChanged after the call that set the text; the run starts there.
    private static async Task TypeAsync(SearchBar bar, SearchSession session, string text)
    {
        bar.Query = text;
        ViewFixture.Pump();
        await session.Completion.WaitAsync(Limit);
        ViewFixture.Pump();
    }

    [AvaloniaFact]
    public async Task HitsAreDrawnOverTheirRectanglesAndTheCurrentOneStronger()
    {
        using var fixture = await ViewFixture.OpenAsync("text-rich.pdf");
        await fixture.SettleAsync();
        using var session = new SearchSession(fixture.Tab, () => 0);
        fixture.View.Search = session;
        var (plain, stride) = fixture.Capture();

        session.Start("Zeile", default);
        await session.Completion.WaitAsync(Limit);
        ViewFixture.Pump();
        var hit = session.Hits[0];
        var box = fixture.View.ToView(hit.PageIndex, hit.Rects[0])!.Value;
        var (marked, _) = fixture.Capture();

        // A pixel at the edge of the rectangle changed colour.
        var x = (int)(box.Left + 1);
        var y = (int)(box.Top + 1);
        Assert.NotEqual(PixelAt(plain, stride, x, y), PixelAt(marked, stride, x, y));

        // A hit that is not the current one is lighter than the current one.
        session.Start("e", default);
        await session.Completion.WaitAsync(Limit);
        ViewFixture.Pump();
        Assert.True(session.Hits.Count > 3);
        var current = session.CurrentHit!;
        var other = session.Hits.First(h => h.PageIndex == current.PageIndex && !ReferenceEquals(h, current));
        var (both, _) = fixture.Capture();
        var currentBox = fixture.View.ToView(current.PageIndex, current.Rects[0])!.Value;
        var otherBox = fixture.View.ToView(other.PageIndex, other.Rects[0])!.Value;
        var strong = Brightness(both, stride, currentBox);
        var light = Brightness(both, stride, otherBox);
        var untouched = Brightness(plain, stride, otherBox);
        Assert.True(light < untouched, $"{light} vs {untouched}");
        Assert.True(strong < light, $"{strong} vs {light}");
    }

    [AvaloniaFact]
    public async Task TheViewScrollsToTheCurrentHit()
    {
        using var fixture = await ViewFixture.OpenAsync("text-dense.pdf");
        await fixture.SettleAsync();
        using var session = new SearchSession(fixture.Tab, () => fixture.View.CurrentPage);
        fixture.View.Search = session;
        Assert.Equal(0, fixture.View.CurrentPage);

        // The first "Needle" lies on page 10; the view jumps there and the hit is inside the view.
        session.Start("Needle", default);
        await session.Completion.WaitAsync(Limit);
        ViewFixture.Pump();
        var hit = session.CurrentHit!;
        Assert.Equal(9, hit.PageIndex);
        var box = fixture.View.ToView(hit.PageIndex, hit.Rects[0])!.Value;
        Assert.True(box.Top >= 0 && box.Bottom <= fixture.View.Bounds.Height, box.ToString());

        // Next goes to the following hit, which lies on page 20.
        session.Next();
        ViewFixture.Pump();
        Assert.Equal(19, session.CurrentHit!.PageIndex);
        var next = fixture.View.ToView(19, session.CurrentHit.Rects[0])!.Value;
        Assert.True(next.Top >= 0 && next.Bottom <= fixture.View.Bounds.Height, next.ToString());
    }

    [AvaloniaFact]
    public async Task ARevealedRectangleThatIsAlreadyVisibleDoesNotMoveTheView()
    {
        using var fixture = await ViewFixture.OpenAsync("text-rich.pdf", 800, 900);
        await fixture.SettleAsync();
        var before = fixture.View.Offset;
        fixture.View.Reveal(0, new PageRect(40, 40, 80, 60));
        Assert.Equal(before, fixture.View.Offset);
    }

    [AvaloniaFact]
    public async Task RevealCentresARectangleThatIsOffScreen()
    {
        using var fixture = await ViewFixture.OpenAsync("text-dense.pdf");
        await fixture.SettleAsync();
        var rect = new PageRect(100, 300, 160, 316);
        fixture.View.Reveal(50, rect);
        var box = fixture.View.ToView(50, rect)!.Value;
        var middle = (box.Top + box.Bottom) / 2;
        Assert.InRange(middle, (fixture.View.Bounds.Height / 2) - 2, (fixture.View.Bounds.Height / 2) + 2);
    }

    [AvaloniaFact]
    public async Task GoToDestinationScrollsToThePointOfThePage()
    {
        using var fixture = await ViewFixture.OpenAsync("text-dense.pdf");
        await fixture.SettleAsync();
        fixture.View.GoToDestination(3, null);
        var top = fixture.View.PageBounds(3)!.Value.Top;
        fixture.View.GoToDestination(3, 400);
        var lower = fixture.View.PageBounds(3)!.Value.Top;
        Assert.True(lower < top - 100, $"{lower} vs {top}");
        Assert.Equal(3, fixture.View.CurrentPage);
        fixture.View.GoToDestination(99, 0);
        Assert.Equal(99, fixture.View.CurrentPage);
        fixture.View.GoToDestination(1_000_000, double.NaN);
        Assert.Equal(199, fixture.View.CurrentPage);
    }

    [AvaloniaFact]
    public async Task PagePointsAndViewPointsMapBothWays()
    {
        using var fixture = await ViewFixture.OpenAsync("text-rich.pdf", 800, 900);
        await fixture.SettleAsync();
        var point = fixture.View.ToView(0, 100, 200)!.Value;
        var spot = fixture.View.PageAt(point)!.Value;
        Assert.Equal(0, spot.Page);
        Assert.Equal(100, spot.X, 0.01);
        Assert.Equal(200, spot.Y, 0.01);
        Assert.Null(fixture.View.PageAt(new Point(1, 1)));
        var outside = fixture.View.NearestSpot(new Point(1, 1))!.Value;
        Assert.Equal(0, outside.Page);
        Assert.True(outside.X < 0 && outside.Y < 0);
    }

    [AvaloniaFact]
    public async Task TheBarSearchesAsYouTypeAndTheKeysMoveThroughTheHits()
    {
        using var fixture = WindowFixture.Create();
        var tab = await fixture.OpenAsync("text-rich.pdf");
        var window = fixture.Window;
        window.ShowSearchBar();
        ViewFixture.Pump();
        var bar = Assert.IsType<SearchBar>(window.ShownSearchBar);
        var session = window.SearchOf(tab)!;

        await TypeAsync(bar, session, "seite");
        Assert.Equal(2, session.Hits.Count);
        Assert.Equal("2 hits", bar.SummaryText);
        Assert.Equal(0, session.Current);

        // Enter goes to the next hit, Shift+Enter to the previous one.
        bar.Input.Focus();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.Equal(1, session.Current);
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Shift);
        Assert.Equal(0, session.Current);

        // Match case with a lower case query finds nothing; the bar says so.
        bar.MatchCaseToggle.IsChecked = true;
        ViewFixture.Pump();
        await session.Completion.WaitAsync(Limit);
        ViewFixture.Pump();
        Assert.Empty(session.Hits);
        Assert.Equal("No hits", bar.SummaryText);
    }

    [AvaloniaFact]
    public async Task EscapeClosesTheBarAndClearsTheHighlights()
    {
        using var fixture = WindowFixture.Create();
        var tab = await fixture.OpenAsync("text-rich.pdf");
        var window = fixture.Window;
        window.ShowSearchBar();
        ViewFixture.Pump();
        var bar = window.ShownSearchBar!;
        var session = window.SearchOf(tab)!;
        await TypeAsync(bar, session, "seite");
        Assert.NotEmpty(session.Hits);

        bar.Input.Focus();
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        ViewFixture.Pump();

        Assert.Null(window.ShownSearchBar);
        Assert.Empty(session.Hits);
        Assert.Null(window.Frame.Notification);
    }

    [AvaloniaFact]
    public async Task TheBarFollowsTheActiveTabAndCloseLeavesNoHighlightBehind()
    {
        using var fixture = WindowFixture.Create();
        var first = await fixture.OpenAsync("text-rich.pdf");
        var window = fixture.Window;
        window.ShowSearchBar();
        var bar = window.ShownSearchBar!;
        await TypeAsync(bar, window.SearchOf(first)!, "seite");

        var second = await fixture.OpenAsync("text-dense.pdf");
        ViewFixture.Pump();
        Assert.Equal(string.Empty, bar.Query);
        Assert.Equal(string.Empty, bar.SummaryText);

        window.Workspace.Select(first);
        ViewFixture.Pump();
        Assert.Equal("seite", bar.Query);
        Assert.Equal("2 hits", bar.SummaryText);

        window.CloseSearchBar();
        Assert.Empty(window.SearchOf(first)!.Hits);
        Assert.Empty(window.SearchOf(second)!.Hits);
    }

    [AvaloniaFact]
    public async Task SwitchingBackToATabDoesNotRestartItsSearch()
    {
        using var fixture = WindowFixture.Create();
        var first = await fixture.OpenAsync("text-rich.pdf");
        var window = fixture.Window;
        window.ShowSearchBar();
        var bar = window.ShownSearchBar!;
        var session = window.SearchOf(first)!;
        await TypeAsync(bar, session, "seite");
        session.Next();
        Assert.Equal(1, session.Current);
        var completion = session.Completion;

        await fixture.OpenAsync("text-dense.pdf");
        ViewFixture.Pump();
        window.Workspace.Select(first);

        // The text box raises TextChanged after the setter that gave it the session's query; that must not count as the user typing.
        for (var i = 0; i < 20; i++)
        {
            ViewFixture.Pump();
            await Task.Delay(10);
        }

        Assert.Same(completion, session.Completion);
        Assert.False(session.IsRunning);
        Assert.Equal(1, session.Current);
        Assert.Equal("2 hits", bar.SummaryText);
    }

    [AvaloniaFact]
    public async Task SearchNextOpensTheBarWhenThereIsNoSearchYet()
    {
        using var fixture = WindowFixture.Create();
        await fixture.OpenAsync("text-rich.pdf");
        Assert.Null(fixture.Window.ShownSearchBar);
        fixture.Window.SearchNext();
        Assert.NotNull(fixture.Window.ShownSearchBar);
    }

    [AvaloniaFact]
    public async Task EveryControlOfTheBarHasAnAutomationName()
    {
        using var fixture = WindowFixture.Create();
        await fixture.OpenAsync("text-rich.pdf");
        fixture.Window.ShowSearchBar();
        ViewFixture.Pump();
        var bar = fixture.Window.ShownSearchBar!;
        foreach (var control in bar.GetVisualDescendants().OfType<Button>())
        {
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)), control.Name);
        }

        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(bar.Input)));
        Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(bar.MatchCaseToggle)));
    }
}
