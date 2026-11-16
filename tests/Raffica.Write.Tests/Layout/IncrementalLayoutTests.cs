using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>An edit of the model laid out again from the page it touches must give what a layout from scratch gives (<see cref="LayoutOracle"/>).</summary>
public sealed class IncrementalLayoutTests
{
    private static string Corpus(string id) => Path.Combine(TestData.Root, "testdata", "write-corpus", id, id + ".docx");

    private static DocumentLayout Laid(WordDocument model, LayoutMode mode = LayoutMode.Pages)
    {
        var layout = DocumentLayout.Create(model, PageTestKit.Fonts, new LayoutOptions(mode));
        layout.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(layout.IsComplete);
        return layout;
    }

    private static void Settle(DocumentLayout layout) => layout.LayOutRestAsync(null, CancellationToken.None).GetAwaiter().GetResult();

    private static int PageOf(DocumentLayout layout, WordDocument model, Paragraph paragraph) => layout.Locate(TestEdit.TopLevelIndex(model, paragraph))?.Page ?? 0;

    // Body paragraphs with text, in a document order that is the same on every run.
    private static List<Paragraph> Texty(WordDocument model, bool bodyOnly = true) =>
        TestEdit.Paragraphs(model).Where(p => (!bodyOnly || model.Body.Blocks.Contains(p.Paragraph)) && p.Paragraph.SectionBreak is null && TestEdit.TextOf(p.Paragraph).Length > 12).Select(p => p.Paragraph).ToList();

    [Fact]
    public void IncrementalEqualsFullAfterTwentySeededInserts()
    {
        using var file = DocxFile.Open(Corpus("thesis"));
        var model = file.Model;
        var layout = Laid(model);
        var random = new Random(4242);
        for (var i = 0; i < 20; i++)
        {
            var candidates = Texty(model);
            var paragraph = candidates[random.Next(candidates.Count)];
            var text = i % 2 == 0 ? "x" : string.Concat(Enumerable.Repeat("lorem ipsum ", 34))[..400];
            var offset = random.Next(TestEdit.TextOf(paragraph).Length + 1);
            var page = PageOf(layout, model, paragraph);
            var change = TestEdit.InsertText(model, paragraph, offset, text, replace: i % 3 == 0);
            var refill = layout.ApplyEdit(change, page + 1);
            Assert.False(refill.Restarted, "edit " + i);
            Settle(layout);
            LayoutOracle.AssertEqualToFull(layout, model, label: "edit " + i);
        }
    }

    [Fact]
    public void RefillStopsWhereAPageStartedBefore()
    {
        using var file = DocxFile.Open(Corpus("thesis"));
        var model = file.Model;
        var layout = Laid(model);
        var candidates = Texty(model);
        var paragraph = candidates[candidates.Count / 2];
        var page = PageOf(layout, model, paragraph);
        var refill = layout.ApplyEdit(TestEdit.InsertText(model, paragraph, 3, "x"), page + 1);

        Assert.False(refill.Restarted);
        Assert.InRange(layout.PagesLaidOut, 1, 3);
        Assert.True(layout.IsComplete, "the pages after the stop were put back, so the layout is still complete");
        LayoutOracle.AssertEqualToFull(layout, model);
    }

    [Fact]
    public void PageThatMovesTheBreakRefillsFurther()
    {
        using var file = DocxFile.Open(Corpus("thesis"));
        var model = file.Model;
        var layout = Laid(model);

        // The last paragraph of a page: 600 more characters push lines onto the next page, whose start then differs.
        var target = Enumerable.Range(20, 60)
            .Select(i => layout.Page(i))
            .Select(p => p.TextLines.LastOrDefault(l => l.Story == StoryRef.Body)?.Paragraph)
            .First(p => p is not null && model.Body.Blocks.Contains(p) && TestEdit.TextOf(p).Length > 40)!;
        var page = PageOf(layout, model, target);
        var refill = layout.ApplyEdit(TestEdit.InsertText(model, target, 5, string.Concat(Enumerable.Repeat("pushing the break ", 34))), page + 1);

        Assert.False(refill.Restarted);
        Assert.True(layout.PagesLaidOut >= 2, "the next page starts differently, so it is laid out too: " + layout.PagesLaidOut);
        Settle(layout);
        LayoutOracle.AssertEqualToFull(layout, model);
    }

    [Fact]
    public void ReadersSeeConsistentPages()
    {
        using var file = DocxFile.Open(Corpus("thesis"));
        var model = file.Model;
        var layout = Laid(model);
        var random = new Random(7);
        var stop = false;
        Exception? failure = null;
        var reads = 0;
        var reader = new Thread(() =>
        {
            try
            {
                while (!Volatile.Read(ref stop))
                {
                    var count = layout.LaidOutPages;
                    for (var i = 0; i < count; i++)
                    {
                        if (layout.TryGetPage(i, out var page))
                        {
                            Assert.Equal(i, page!.Index);
                            Assert.NotNull(page.Text);
                        }

                        _ = layout.Locate(i);
                    }

                    _ = layout.Revision;
                    reads++;
                }
            }
            catch (Exception e)
            {
                failure = e;
            }
        });
        reader.Start();
        try
        {
            for (var i = 0; i < 50; i++)
            {
                var candidates = Texty(model);
                var paragraph = candidates[random.Next(candidates.Count)];
                var text = i % 4 == 0 ? string.Concat(Enumerable.Repeat("pushing the break ", 34)) : "y";
                var change = TestEdit.InsertText(model, paragraph, 2, text);
                layout.ApplyEdit(change, PageOf(layout, model, paragraph) + 1);
                if (i % 10 == 9)
                {
                    Settle(layout);
                }
            }

            Settle(layout);
        }
        finally
        {
            Volatile.Write(ref stop, true);
            reader.Join();
        }

        Assert.Null(failure);
        Assert.True(reads > 0);
        LayoutOracle.AssertEqualToFull(layout, model);
    }
}
