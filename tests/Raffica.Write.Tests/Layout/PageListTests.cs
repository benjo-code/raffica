using Raffica.Write.Layout;
using Xunit;

namespace Raffica.Write.Tests.Layout;

public sealed class PageListTests
{
    private static LaidOutPage Page(int index, string text = "") => new(index, (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), 612f, 792f, false, [], text, []);

    private static PageList Filled(int count)
    {
        var list = new PageList();
        for (var i = 0; i < count; i++)
        {
            list.Add(Page(i, "old"));
        }

        return list;
    }

    [Fact]
    public void AddAndIndexAcrossAGrowingArray()
    {
        var list = Filled(100);

        Assert.Equal(100, list.Count);
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(i, list[i].Index);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => list[100]);
        Assert.False(list.TryGet(100, out _));
        Assert.False(list.TryGet(-1, out _));
        Assert.True(list.TryGet(99, out var last));
        Assert.Equal(99, last.Index);
    }

    [Fact]
    public void SpliceReplacesTheMiddleAndKeepsThePagesFromTheStop()
    {
        var list = Filled(10);
        list.Splice(3, [Page(3, "new"), Page(4, "new")], keepFrom: 5);

        Assert.Equal(10, list.Count);
        Assert.Equal(["old", "old", "old", "new", "new", "old", "old", "old", "old", "old"], Enumerable.Range(0, 10).Select(i => list[i].Text));

        // A longer replacement grows the list; the kept pages must already have their new indexes (the caller made them).
        list.Splice(8, [Page(8, "new"), Page(9, "new"), Page(10, "new")], keepFrom: 10);
        Assert.Equal(11, list.Count);
        Assert.Equal("new", list[10].Text);
    }

    [Fact]
    public void SpliceWithAnAdjustmentRewritesOnlyTheKeptPages()
    {
        var list = Filled(6);
        list.Splice(1, [Page(1, "new")], keepFrom: 2, adjustKept: p => Page(p.Index, "kept"));

        Assert.Equal(["old", "new", "kept", "kept", "kept", "kept"], Enumerable.Range(0, 6).Select(i => list[i].Text));
        Assert.Equal(6, list.Count);
    }

    [Fact]
    public void TruncateAndReplaceRangeAreSingleSteps()
    {
        var list = Filled(10);
        list.ReplaceRange(2, [Page(2, "new"), Page(3, "new")]);
        Assert.Equal(10, list.Count);
        Assert.Equal("new", list[3].Text);
        Assert.Equal("old", list[4].Text);

        list.Truncate(4);
        Assert.Equal(4, list.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => list[4]);
        list.Truncate(9);
        Assert.Equal(4, list.Count);

        // After a cut a page is added into the new array: the old one a reader may hold is never written.
        list.Add(Page(4, "later"));
        Assert.Equal("later", list[4].Text);
    }

    [Fact]
    public void ReadersNeverSeeAPageWhoseIndexIsNotItsPositionWhileTheWriterSplicesAndCuts()
    {
        var list = Filled(40);
        var stop = false;
        Exception? failure = null;
        var readers = Enumerable.Range(0, 3).Select(_ => new Thread(() =>
        {
            try
            {
                while (!Volatile.Read(ref stop))
                {
                    var count = list.Count;
                    for (var i = 0; i < count; i++)
                    {
                        if (list.TryGet(i, out var page))
                        {
                            Assert.Equal(i, page.Index);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                failure = e;
            }
        })).ToList();
        readers.ForEach(t => t.Start());
        var random = new Random(1);
        for (var round = 0; round < 3000; round++)
        {
            var count = list.Count;
            switch (random.Next(4))
            {
                case 0:
                    list.Add(Page(list.Count, "add"));
                    break;
                case 1 when count > 5:
                    var first = random.Next(count - 3);
                    list.Splice(first, [Page(first, "s"), Page(first + 1, "s")], first + 2);
                    break;
                case 2 when count > 10:
                    list.Truncate(count - random.Next(1, 6));
                    break;
                default:
                    if (count > 0)
                    {
                        list.Replace(count - 1, Page(count - 1, "r"));
                    }

                    break;
            }
        }

        Volatile.Write(ref stop, true);
        readers.ForEach(t => t.Join());
        Assert.Null(failure);
    }
}
