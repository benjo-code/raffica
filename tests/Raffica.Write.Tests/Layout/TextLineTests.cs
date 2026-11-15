using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>The text lines a laid-out page keeps: the way back from what is drawn to the paragraphs of the model.</summary>
public sealed class TextLineTests
{
    private static string Root => Path.Combine(TestData.Root, "testdata", "write-corpus");

    private static string Thesis => Path.Combine(Root, "thesis", "thesis.docx");

    private static async Task<DocumentLayout> LayOut(DocxFile docx, LayoutMode mode)
    {
        var layout = DocumentLayout.Create(docx.Model, PageTestKit.Fonts, new LayoutOptions(mode));
        await layout.LayOutRestAsync(null, CancellationToken.None);
        Assert.True(layout.IsComplete);
        return layout;
    }

    private static string TextOf(TextLine line) => line.Projection.Text.AsSpan(line.Start, line.End - line.Start).TrimEnd(" \n\r\f").ToString();

    [Fact]
    public async Task EveryRegionHasRecords()
    {
        using var docx = DocxFile.Open(Thesis);
        var layout = await LayOut(docx, LayoutMode.Pages);
        var topLevel = new HashSet<Paragraph>(docx.Model.Body.Blocks.OfType<Paragraph>(), ReferenceEqualityComparer.Instance);
        var kinds = new HashSet<StoryKind>();
        var cell = false;
        var body = false;
        for (var i = 0; i < layout.LaidOutPages; i++)
        {
            foreach (var line in layout.Page(i).TextLines)
            {
                kinds.Add(line.Story.Kind);
                if (line.Story.Kind == StoryKind.Body)
                {
                    if (topLevel.Contains(line.Paragraph))
                    {
                        body = true;
                    }
                    else
                    {
                        cell = true;
                    }
                }
            }
        }

        Assert.True(body, "no body paragraph record");
        Assert.True(cell, "no table cell paragraph record");
        Assert.Contains(StoryKind.Note, kinds);
        Assert.Contains(StoryKind.HeaderFooter, kinds);
        Assert.DoesNotContain(StoryKind.Comment, kinds);
    }

    [Fact]
    public async Task RecordsCoverThePageText()
    {
        using var docx = DocxFile.Open(Thesis);
        var layout = await LayOut(docx, LayoutMode.Pages);
        for (var i = 0; i < layout.LaidOutPages; i++)
        {
            var page = layout.Page(i);
            var lines = page.TextLines;
            var rows = page.Text.Length == 0 ? [] : page.Text.Split('\n');
            Assert.True(rows.Length == lines.Length, $"page {i}: {rows.Length} text rows, {lines.Length} records");
            for (var k = 0; k < rows.Length; k++)
            {
                var text = TextOf(lines[k]);

                // The row of a numbered paragraph starts with its label and a space; the record has the text only.
                var ok = rows[k] == text || (rows[k].Length > text.Length && rows[k].EndsWith(text, StringComparison.Ordinal) && rows[k][^(text.Length + 1)] == ' ');
                Assert.True(ok, $"page {i} row {k}: [{rows[k]}] against record [{text}]");
            }
        }
    }

    [Fact]
    public async Task ARecordHasTheGeometryOfItsLine()
    {
        using var docx = DocxFile.Open(Thesis);
        var layout = await LayOut(docx, LayoutMode.Pages);
        var page = layout.Page(2);
        Assert.NotEmpty(page.TextLines);
        foreach (var line in page.TextLines)
        {
            Assert.True(line.Height > 0f);
            Assert.InRange(line.Baseline, line.Top, line.Top + line.Height);
            Assert.InRange(line.Top, 0f, page.Height);
            Assert.InRange(line.X, 0f, page.Width);
            Assert.InRange(line.Start, 0, line.End);
            Assert.InRange(line.End, 0, line.Projection.Text.Length);
        }
    }

    [Fact]
    public void APageBreakContinuationKeepsTheRangesContiguous()
    {
        var body = "<w:p><w:r><w:t>before</w:t></w:r><w:r><w:br w:type=\"page\"/></w:r><w:r><w:t>after the break</w:t></w:r></w:p>";
        var layout = PageTestKit.Complete(body);
        Assert.Equal(2, layout.LaidOutPages);
        var first = Assert.Single(layout.Page(0).TextLines);
        var second = Assert.Single(layout.Page(1).TextLines);
        Assert.Same(first.Paragraph, second.Paragraph);
        Assert.Equal(0, first.Start);
        Assert.Equal(first.End, second.Start);
        Assert.Equal(first.Projection.Text.Length, second.End);
        Assert.Equal("after the break", TextOf(second));
    }

    [Fact]
    public void AnEmptyParagraphHasAnEmptyRange()
    {
        var layout = PageTestKit.Complete("<w:p/><w:p><w:r><w:t>x</w:t></w:r></w:p>");
        var lines = layout.Page(0).TextLines;
        Assert.Equal(2, lines.Length);
        Assert.Equal((0, 0), (lines[0].Start, lines[0].End));
        Assert.Equal((0, 1), (lines[1].Start, lines[1].End));
    }

    [Fact]
    public void LineRangesOfAWrappedParagraphTileTheText()
    {
        var text = string.Join(' ', Enumerable.Repeat("lorem ipsum dolor", 80));
        var layout = PageTestKit.Complete(PageTestKit.P(text));
        var lines = layout.Page(0).TextLines;
        Assert.True(lines.Length > 5);
        Assert.Equal(0, lines[0].Start);
        for (var i = 1; i < lines.Length; i++)
        {
            Assert.Equal(lines[i - 1].End, lines[i].Start);
        }

        Assert.Equal(text.Length, lines[^1].End);
        Assert.Equal(text, string.Concat(lines.Select(l => l.Projection.Text[l.Start..l.End])));
    }

    // The layout text differs from the projection text where a float has no room (a leading box), where a break is hidden and where a
    // float is placed by the page; the map must make the ranges tile the projection text in every one of those cases.
    [Fact]
    public void OffsetMapTranslatesLeadingBoxesSkippedCharactersAndHiddenBreaks()
    {
        // projection "ab" + a placed float (1 char, no layout text) + "cd" + hidden break (no projection text, one layout char) + "ef"
        var builder = new OffsetMap.Builder();
        builder.Add(0, 2, 2);
        builder.Add(2, 1, 0);
        builder.Add(3, 2, 2);
        builder.Add(5, 0, 1);
        builder.Add(5, 2, 2);
        var map = builder.Build();

        Assert.Equal(0, map.Map(0));
        Assert.Equal(1, map.Map(1));
        Assert.Equal(3, map.Map(2));
        Assert.Equal(4, map.Map(3));
        Assert.Equal(5, map.Map(4));
        Assert.Equal(5, map.Map(5));
        Assert.Equal(6, map.Map(6));
        Assert.Equal(7, map.Map(7));

        var lead = new OffsetMap.Builder();
        lead.Add(0, 3, 3);
        lead.Lead(2);
        var led = lead.Build();
        Assert.Equal(0, led.Map(0));
        Assert.Equal(0, led.Map(1));
        Assert.Equal(0, led.Map(2));
        Assert.Equal(3, led.Map(5));

        var tail = map.Slice(4);
        Assert.Equal(5, tail.Map(0));
        Assert.Equal(5, tail.Map(1));
        Assert.Equal(6, tail.Map(2));
        Assert.Same(OffsetMap.Identity, new OffsetMap.Builder().Build());
    }

    [Fact]
    public async Task EveryCorpusParagraphTilesItsProjectionText()
    {
        var problems = new List<string>();
        var paragraphs = 0;
        var lines = 0;
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            using var docx = DocxFile.Open(Path.Combine(Root, entry.Id, entry.Id + ".docx"));
            foreach (var mode in new[] { LayoutMode.Pages, LayoutMode.Flow })
            {
                var layout = await LayOut(docx, mode);
                foreach (var group in Groups(layout))
                {
                    paragraphs++;
                    lines += group.Count;
                    var where = $"{entry.Id}/{mode} page {group[0].Page}";
                    if (group[0].Line.Start != 0)
                    {
                        problems.Add($"{where}: starts at {group[0].Line.Start}");
                    }

                    for (var k = 0; k < group.Count; k++)
                    {
                        var line = group[k].Line;
                        if (line.Start > line.End || line.End > line.Projection.Text.Length)
                        {
                            problems.Add($"{where}: range {line.Start}..{line.End} of {line.Projection.Text.Length}");
                        }

                        if (k > 0 && group[k - 1].Line.End != line.Start)
                        {
                            problems.Add($"{where}: gap {group[k - 1].Line.End} to {line.Start}");
                        }
                    }

                    var last = group[^1].Line;
                    if (last.End != last.Projection.Text.Length)
                    {
                        problems.Add($"{where}: ends at {last.End} of {last.Projection.Text.Length}");
                    }
                }
            }
        }

        Assert.True(paragraphs > 1000 && lines > paragraphs, $"only {paragraphs} paragraphs, {lines} lines");
        Assert.True(problems.Count == 0, $"{problems.Count} problems, first: {string.Join(" | ", problems.Take(20))}");
    }

    // The lines of every paragraph in reading order: a line joins the open group of its paragraph (in its story) when that paragraph went on
    // and the line starts where it ended; a header that repeats on every page starts a group each time because its next line starts at 0.
    private static List<List<(int Page, TextLine Line)>> Groups(DocumentLayout layout)
    {
        var groups = new List<List<(int Page, TextLine Line)>>();
        var open = new Dictionary<(Paragraph, StoryRef), List<(int Page, TextLine Line)>>(new KeyComparer());
        for (var i = 0; i < layout.LaidOutPages; i++)
        {
            foreach (var line in layout.Page(i).TextLines)
            {
                var key = (line.Paragraph, line.Story);
                if (open.TryGetValue(key, out var group) && group[^1].Line.End == line.Start)
                {
                    group.Add((i, line));
                }
                else
                {
                    group = [(i, line)];
                    groups.Add(group);
                    open[key] = group;
                }
            }
        }

        return groups;
    }

    private sealed class KeyComparer : IEqualityComparer<(Paragraph Paragraph, StoryRef Story)>
    {
        public bool Equals((Paragraph Paragraph, StoryRef Story) x, (Paragraph Paragraph, StoryRef Story) y) => ReferenceEquals(x.Paragraph, y.Paragraph) && x.Story == y.Story;

        public int GetHashCode((Paragraph Paragraph, StoryRef Story) obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj.Paragraph);
    }
}
