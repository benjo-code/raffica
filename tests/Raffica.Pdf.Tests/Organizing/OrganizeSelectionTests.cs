using Raffica.Pdf.Organizing;
using Xunit;

namespace Raffica.Pdf.Tests.Organizing;

public sealed class OrganizeSelectionTests
{
    private static OrganizeSelection Make(int pages)
    {
        var selection = new OrganizeSelection();
        selection.Set(pages, []);
        return selection;
    }

    [Fact]
    public void SelectReplacesAndSetsTheAnchor()
    {
        var selection = Make(6);
        selection.Select(2);
        selection.Select(4);

        Assert.Equal([4], selection.Positions);
        Assert.Equal(4, selection.Anchor);
    }

    [Fact]
    public void ExtendSelectsFromTheAnchorInBothDirections()
    {
        var selection = Make(8);
        selection.Select(3);
        selection.Extend(5);
        Assert.Equal([3, 4, 5], selection.Positions);

        selection.Extend(1);
        Assert.Equal([1, 2, 3], selection.Positions);
        Assert.Equal(3, selection.Anchor);
    }

    [Fact]
    public void ExtendWithoutAnAnchorSelectsThePage()
    {
        var selection = Make(4);
        selection.Extend(2);

        Assert.Equal([2], selection.Positions);
    }

    [Fact]
    public void ToggleAddsRemovesAndMovesTheAnchor()
    {
        var selection = Make(6);
        selection.Select(1);
        selection.Toggle(4);
        Assert.Equal([1, 4], selection.Positions);
        Assert.Equal(4, selection.Anchor);

        selection.Toggle(1);
        selection.Toggle(4);
        Assert.Empty(selection.Positions);
    }

    [Fact]
    public void SelectAllAndClear()
    {
        var selection = Make(5);
        selection.SelectAll();
        Assert.Equal([0, 1, 2, 3, 4], selection.Positions);

        selection.Clear();
        Assert.Empty(selection.Positions);
        Assert.Equal(-1, selection.Anchor);
    }

    [Fact]
    public void PositionsAreASetAndAscending()
    {
        var selection = Make(6);
        selection.Set(6, [4, 1, 4, 2, 1, 99, -3]);

        Assert.Equal([1, 2, 4], selection.Positions);
        Assert.Equal(3, selection.Count);
        Assert.True(selection.Contains(2));
        Assert.False(selection.Contains(3));
    }

    [Fact]
    public void PositionsOutsideThePlanAreRefused()
    {
        var selection = Make(3);

        Assert.Throws<ArgumentOutOfRangeException>(() => selection.Select(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => selection.Toggle(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => selection.Extend(9));
    }

    [Fact]
    public void EveryChangeRaisesChangedOnce()
    {
        var selection = Make(4);
        var raised = 0;
        selection.Changed += () => raised++;

        selection.Select(1);
        selection.Extend(3);
        selection.Toggle(0);
        selection.SelectAll();
        selection.Clear();

        Assert.Equal(5, raised);
    }
}
