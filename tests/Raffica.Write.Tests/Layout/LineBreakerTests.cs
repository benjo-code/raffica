using System.Diagnostics;
using Raffica.Write.Layout;
using Xunit;

namespace Raffica.Write.Tests.Layout;

public sealed class LineBreakerTests
{
    private static List<int> Offsets(string text) => LineBreaker.Find(text).Select(b => b.Offset).ToList();

    [Fact]
    public void MandatoryBreakAfterNewline()
    {
        var breaks = LineBreaker.Find("a\nb");
        Assert.Contains(new BreakOpportunity(2, true), breaks);
        Assert.Contains(new BreakOpportunity(3, false), breaks);
    }

    [Fact]
    public void NoBreakInsideNumber() => Assert.Equal([8], Offsets("1,000.50"));

    [Fact]
    public void BreakAfterHyphen() => Assert.Contains(5, Offsets("well-known"));

    [Fact]
    public void NoBreakBeforeClosePunctuation() => Assert.DoesNotContain(3, Offsets("end) next"));

    [Fact]
    public void SpaceAllowsABreakAfterIt() => Assert.Equal([4, 7], Offsets("one two"));

    [Fact]
    public void SurrogatePairsAreOneCharacter()
    {
        var text = "a\U0001F600b";
        Assert.DoesNotContain(2, Offsets(text));
    }

    [Fact]
    public void CjkBreaksBetweenIdeographs() => Assert.Equal([1, 2, 3], Offsets("中文字"));

    [Fact]
    public void EndOffsetIsAlwaysPresent()
    {
        Assert.Equal([0], Offsets(string.Empty));
        Assert.Equal([1], Offsets("x"));
    }

    [Fact]
    public void LoneSurrogatesDoNotThrow() => Assert.Equal(3, Offsets("a\uD800b").Last());

    [Fact]
    public void LongTextIsLinear()
    {
        var text = string.Concat(Enumerable.Repeat("word, 1,000.50 (x) 中文 ", 50000));
        Assert.True(text.Length >= 1_000_000);
        var watch = Stopwatch.StartNew();
        var breaks = LineBreaker.Find(text);
        watch.Stop();
        Assert.True(watch.ElapsedMilliseconds < 2000, $"took {watch.ElapsedMilliseconds} ms");
        Assert.Equal(text.Length, breaks[^1].Offset);
    }
}
