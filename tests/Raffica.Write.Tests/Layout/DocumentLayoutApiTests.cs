using Raffica.Write.Layout;
using Xunit;
using static Raffica.Write.Tests.Layout.PageTestKit;

namespace Raffica.Write.Tests.Layout;

public sealed class DocumentLayoutApiTests
{
    private sealed class SyncProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value) => report(value);
    }

    private static string LongDocument(int paragraphs) => Numbered("Paragraph {0} of the document under test", paragraphs) + Sect();

    [Fact]
    public void LayOutFirstPagePublishesOnePage()
    {
        var layout = Create(LongDocument(400));
        Assert.Equal(0, layout.LaidOutPages);
        Assert.False(layout.IsComplete);
        layout.LayOutFirstPage();
        Assert.Equal(1, layout.LaidOutPages);
        Assert.False(layout.IsComplete);
        var page = layout.Page(0);
        Assert.Equal(0, page.Index);
        Assert.Equal("1", page.Number);
        Assert.False(page.IsStrip);
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.Page(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => layout.Page(-1));
    }

    [Fact]
    public void LayOutThroughStopsAtThePageAndIsRepeatable()
    {
        var layout = Create(LongDocument(400));
        layout.LayOutThrough(2);
        Assert.Equal(3, layout.LaidOutPages);
        layout.LayOutThrough(1);
        Assert.Equal(3, layout.LaidOutPages);
        layout.LayOutFirstPage();
        Assert.Equal(3, layout.LaidOutPages);

        // Asking beyond the end lays out everything and stops.
        layout.LayOutThrough(100000);
        var total = layout.LaidOutPages;
        Assert.True(total > 3 && total < 100000);
        Assert.True(layout.IsComplete);
        for (var i = 0; i < total; i++)
        {
            Assert.Equal(i, layout.Page(i).Index);
            Assert.Equal((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), layout.Page(i).Number);
        }
    }

    [Fact]
    public async Task TheRestIsLaidOutOnAnotherThreadAndReported()
    {
        var layout = Create(LongDocument(400));
        layout.LayOutFirstPage();
        var reports = new List<int>();
        await layout.LayOutRestAsync(new SyncProgress(reports.Add), CancellationToken.None);
        Assert.True(layout.IsComplete);
        Assert.NotEmpty(reports);
        Assert.Equal(layout.LaidOutPages, reports[^1]);
        Assert.Equal(reports.OrderBy(r => r), reports);
        Assert.Equal(reports.Distinct().Count(), reports.Count);
    }

    [Fact]
    public async Task ReadingWhileLayingOutIsSafe()
    {
        var layout = Create(Numbered("Paragraph {0} with a few words to give the page text some weight", 6000) + Sect());
        layout.LayOutFirstPage();
        var rest = layout.LayOutRestAsync(null, CancellationToken.None);
        var reader = Task.Run(() =>
        {
            var last = 0;
            var glyphs = 0L;
            while (true)
            {
                var done = rest.IsCompleted;
                var count = layout.LaidOutPages;
                Assert.True(count >= last, "the page count never decreases");
                last = count;
                for (var i = Math.Max(0, count - 20); i < count; i++)
                {
                    var page = layout.Page(i);
                    Assert.Equal(i, page.Index);
                    foreach (var item in page.Items)
                    {
                        if (item is GlyphRunItem run)
                        {
                            glyphs += run.Glyphs.Length + (long)run.Xs.Length;
                        }
                    }

                    Assert.NotEmpty(page.Text);
                    _ = layout.Locate(0);
                    _ = layout.BlockAt(i, 100f);
                }

                if (done)
                {
                    return (last, glyphs);
                }
            }
        });

        await rest;
        var (seen, total) = await reader;
        Assert.True(layout.IsComplete);
        Assert.Equal(layout.LaidOutPages, seen);
        Assert.True(seen > 50);
        Assert.True(total > 0);
    }

    [Fact]
    public async Task CancellationStopsTheLayout()
    {
        var body = LongDocument(6000);
        var expected = Complete(body).LaidOutPages;
        Assert.True(expected > 50);

        var layout = Create(body);
        layout.LayOutFirstPage();
        using var cts = new CancellationTokenSource();
        var task = layout.LayOutRestAsync(new SyncProgress(_ => cts.Cancel()), cts.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(task.IsCanceled);
        Assert.False(layout.IsComplete);
        var stopped = layout.LaidOutPages;
        Assert.InRange(stopped, 2, expected - 1);

        // Nothing goes on after the cancel, and a later call carries on from the same state to the same result.
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(stopped, layout.LaidOutPages);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        Assert.True(layout.IsComplete);
        Assert.Equal(expected, layout.LaidOutPages);

        // A token that is already cancelled does no work at all.
        var untouched = Create(body);
        using var done = new CancellationTokenSource();
        await done.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => untouched.LayOutRestAsync(null, done.Token));
        Assert.Equal(0, untouched.LaidOutPages);
    }

    [Fact]
    public void PageNumbersFollowTheFormatAndStartOfTheSection()
    {
        // Section 1: lower roman from 3 (two pages); section 2: decimal again from 1; section 3: upper letters from 2; section 4: lower letters from 27.
        var body =
            P("a") + PEnd("b", Sect("<w:pgNumType w:fmt=\"lowerRoman\" w:start=\"3\"/>"), "<w:pageBreakBefore/>")
            + PEnd("c", Sect("<w:pgNumType w:start=\"1\"/>"))
            + PEnd("d", Sect("<w:pgNumType w:fmt=\"upperLetter\" w:start=\"2\"/>"))
            + PEnd("e", Sect("<w:pgNumType w:fmt=\"lowerLetter\" w:start=\"27\"/>"))
            + P("f") + Sect("<w:pgNumType w:fmt=\"upperRoman\"/>");
        var numbers = Pages(Complete(body)).Select(p => p.Number).ToList();
        Assert.Equal(["iii", "iv", "1", "B", "aa", "XXVIII"], numbers);
    }

    [Fact]
    public void RepairsAreKnownBeforeAnyLayoutAndNeverChange()
    {
        var layout = Create(P("x") + "<w:sectPr><w:pgSz w:w=\"0\" w:h=\"0\"/></w:sectPr>");
        Assert.Single(layout.Notes);
        layout.LayOutFirstPage();
        Assert.Single(layout.Notes);
        Assert.Empty(Create(P("x") + Sect()).Notes);
    }

    [Fact]
    public void ABlockTheLayoutCannotDrawIsAMarkedBox()
    {
        const string Table = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"2000\"/></w:tblGrid><w:tr><w:tc><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
        var layout = Complete(P("before") + Table + P("after") + Sect());
        var page = layout.Page(0);
        Assert.Single(page.Items.OfType<PlaceholderItem>());
        Assert.Equal(["before", "after"], Lines(page));
        Assert.Equal(0, layout.Locate(1)!.Value.Page);
    }

    [Fact]
    public void TrackedChangesShowAsAccepted()
    {
        const string Body =
            "<w:p><w:r><w:t xml:space=\"preserve\">keep </w:t></w:r>"
            + "<w:ins w:id=\"1\" w:author=\"a\"><w:r><w:t xml:space=\"preserve\">added </w:t></w:r></w:ins>"
            + "<w:del w:id=\"2\" w:author=\"a\"><w:r><w:delText xml:space=\"preserve\">removed </w:delText></w:r></w:del>"
            + "<w:r><w:t>end</w:t></w:r></w:p>";
        var layout = Complete(Body + Sect());
        Assert.Equal(["keep added end"], Lines(layout.Page(0)));
    }
}
