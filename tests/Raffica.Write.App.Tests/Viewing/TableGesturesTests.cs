using Raffica.Write.Editing;
using Raffica.Write.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Viewing;

/// <summary>The arithmetic of the table gestures (milestone 4.5a, Task 9): pure, no view.</summary>
public sealed class TableGesturesTests
{
    private static readonly int[] s_three = [2400, 2400, 2400];

    [Fact]
    public void TheRightEdgeStopsAtTheRoomToThePageEdge()
    {
        var (widths, applied) = TableGestures.MoveEdge(s_three, 5000, maxTotalTwips: 8000)!.Value;

        Assert.Equal(800, applied);
        Assert.Equal([2400, 2400, 3200], widths);
        Assert.Equal(8000, widths.Sum());
    }

    [Fact]
    public void TheRightEdgeNeverGrowsPastTheLimitOfTheFormat()
    {
        var (_, applied) = TableGestures.MoveEdge(s_three, 5_000_000, maxTotalTwips: int.MaxValue)!.Value;

        Assert.Equal(TableRules.MaxTwips - 7200, applied);
    }

    [Fact]
    public void ATableWiderThanTheRoomCanStillBeMadeNarrowerButNotWider()
    {
        // 9600 twips drawn, 8000 of room: a drag to the right changes nothing, a drag to the left shrinks the last column as asked.
        int[] wide = [3200, 3200, 3200];

        Assert.Equal(0, TableGestures.MoveEdge(wide, 100, maxTotalTwips: 8000)!.Value.Applied);
        var (widths, applied) = TableGestures.MoveEdge(wide, -100, maxTotalTwips: 8000)!.Value;
        Assert.Equal(-100, applied);
        Assert.Equal(3100, widths[2]);
    }

    [Fact]
    public void TheRightEdgeKeepsTheMinimumOfTheLastColumn()
    {
        Assert.Equal(TableGestures.MinColumnTwips - 2400, TableGestures.MoveEdge(s_three, -100_000, 20_000)!.Value.Applied);

        // A last column that is narrower than the minimum already is neither made narrower nor forced wider.
        int[] narrow = [2400, 100];
        Assert.Equal(0, TableGestures.MoveEdge(narrow, -50, 20_000)!.Value.Applied);
        Assert.Equal(50, TableGestures.MoveEdge(narrow, 50, 20_000)!.Value.Applied);
    }

    [Fact]
    public void ABorderBetweenColumnsNarrowerThanTheMinimumDoesNotShrinkThem()
    {
        int[] narrow = [100, 2000, 2400];

        // Left of the border the column is 100 wide: it cannot shrink, it may grow while the right one keeps the minimum.
        Assert.Equal(0, TableGestures.MoveBorder(narrow, 1, -50)!.Value.Applied);
        Assert.Equal(50, TableGestures.MoveBorder(narrow, 1, 50)!.Value.Applied);
        Assert.Equal(2000 - TableGestures.MinColumnTwips, TableGestures.MoveBorder(narrow, 1, 100_000)!.Value.Applied);

        // Both neighbours narrower than the minimum: the border stays where it is when it would narrow either.
        int[] both = [100, 120];
        Assert.Equal(0, TableGestures.MoveBorder(both, 1, 10)!.Value.Applied);
        Assert.Equal(0, TableGestures.MoveBorder(both, 1, -10)!.Value.Applied);
    }

    [Fact]
    public void OnlyAnInnerBorderMoves()
    {
        Assert.Null(TableGestures.MoveBorder(s_three, 0, 10));
        Assert.Null(TableGestures.MoveBorder(s_three, 3, 10));
    }
}
