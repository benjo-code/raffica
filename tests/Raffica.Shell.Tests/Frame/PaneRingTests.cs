using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

public sealed class PaneRingTests
{
    private sealed record Item(string Name, bool IsAvailable = true) : IPaneItem;

    private static readonly Item[] Three = [new("navigation"), new("document"), new("comments")];

    [Fact]
    public void NextCyclesThroughThePanes()
    {
        Assert.Equal(1, PaneRing.Next(Three, 0));
        Assert.Equal(2, PaneRing.Next(Three, 1));
        Assert.Equal(0, PaneRing.Next(Three, 2));
    }

    [Fact]
    public void PreviousCyclesBackwards()
    {
        Assert.Equal(1, PaneRing.Previous(Three, 2));
        Assert.Equal(0, PaneRing.Previous(Three, 1));
        Assert.Equal(2, PaneRing.Previous(Three, 0));
    }

    [Fact]
    public void ClosedPanesAreSkipped()
    {
        Item[] panes = [new("navigation"), new("document"), new("comments", false)];
        Assert.Equal(0, PaneRing.Next(panes, 1));
        Assert.Equal(1, PaneRing.Previous(panes, 0));

        Item[] middleClosed = [new("navigation"), new("document", false), new("comments")];
        Assert.Equal(2, PaneRing.Next(middleClosed, 0));
        Assert.Equal(0, PaneRing.Previous(middleClosed, 2));
    }

    [Fact]
    public void NoAvailablePaneGivesNull()
    {
        Item[] none = [new("a", false), new("b", false)];
        Assert.Null(PaneRing.Next(none, null));
        Assert.Null(PaneRing.Previous(none, 0));
        Assert.Null(PaneRing.Next([], null));
    }

    [Fact]
    public void StartsAtTheFirstAvailablePaneWhenNothingIsFocused()
    {
        Item[] panes = [new("a", false), new("b"), new("c")];
        Assert.Equal(1, PaneRing.Next(panes, null));
        Assert.Equal(2, PaneRing.Previous(panes, null));
    }

    [Fact]
    public void ASingleAvailablePaneStaysPut() => Assert.Equal(0, PaneRing.Next([new Item("a")], 0));
}
