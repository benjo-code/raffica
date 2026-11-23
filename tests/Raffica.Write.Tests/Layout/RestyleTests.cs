using System.Xml.Linq;
using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>A style edit changes the style sheet, which an engine reads once: <see cref="DocumentLayout.Restyle"/> starts over on a fresh engine.</summary>
public sealed class RestyleTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static string Thesis => Path.Combine(TestData.Root, "testdata", "write-corpus", "thesis", "thesis.docx");

    private static void SetHeadingSize(WordDocument model, string halfPoints)
    {
        var style = model.Styles!.Find("Heading1") ?? throw new InvalidOperationException("no Heading1");
        style.RunProperties.Set(W + "sz", new XElement(W + "sz", new XAttribute(W + "val", halfPoints)));
    }

    private static async Task<DocumentLayout> LaidAsync(WordDocument model)
    {
        var layout = DocumentLayout.Create(model, PageTestKit.Fonts, new LayoutOptions());
        await layout.LayOutRestAsync(null, CancellationToken.None);
        return layout;
    }

    [Fact]
    public async Task ChangingAStyleAndRestylingEqualsAFullLayout()
    {
        using var file = DocxFile.Open(Thesis);
        var layout = await LaidAsync(file.Model);
        SetHeadingSize(file.Model, "72");

        var refill = layout.Restyle(2);

        Assert.True(refill.Restarted);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(layout, file.Model, label: "restyle heading size");
    }

    [Fact]
    public async Task WithoutRestyleTheLayoutDoesNotSeeAStyleChange()
    {
        using var file = DocxFile.Open(Thesis);
        var layout = await LaidAsync(file.Model);
        var before = layout.Page(1);
        SetHeadingSize(file.Model, "72");
        var full = await LaidAsync(file.Model);

        // The engine's resolver snapshotted the styles: the old layout still has the old pages, the fresh one has bigger headings.
        Assert.NotEqual(full.LaidOutPages, layout.LaidOutPages);
        var changed = Enumerable.Range(0, Math.Min(full.LaidOutPages, layout.LaidOutPages))
            .First(i => full.Page(i).Text != layout.Page(i).Text || full.Page(i).Items.Count != layout.Page(i).Items.Count);
        Assert.Same(before, layout.Page(1));
        Assert.True(changed < 40, "a heading of the first chapters differs: " + changed);

        layout.Restyle(changed);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        Assert.Equal(full.Page(changed).Text, layout.Page(changed).Text);
        Assert.Equal(full.Page(changed).Items.Count, layout.Page(changed).Items.Count);
    }

    [Fact]
    public async Task RestyleCountsARestart()
    {
        using var file = DocxFile.Open(Thesis);
        var layout = await LaidAsync(file.Model);
        SetHeadingSize(file.Model, "72");

        layout.Restyle(0);

        Assert.Equal(new EditStats(1, 1), layout.Stats);
    }

    [Fact]
    public void RestyleAfterAFaultThrows()
    {
        using var file = DocxFile.Open(Thesis);
        var layout = DocumentLayout.Create(file.Model, PageTestKit.Fonts, new LayoutOptions());
        layout.BeforePage = published =>
        {
            if (published == 1)
            {
                throw new InvalidOperationException("injected");
            }
        };
        Assert.Throws<InvalidOperationException>(() => layout.LayOutThrough(3));

        // LayOutThrough and Restyle share the one remembered fault.
        Assert.Throws<InvalidOperationException>(() => layout.Restyle(1));
    }

    private static string HeadingStyles(int count = 9)
    {
        var text = "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style>";
        for (var i = 1; i <= count; i++)
        {
            text += $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{i}\"><w:name w:val=\"heading {i}\"/><w:pPr><w:outlineLvl w:val=\"{i - 1}\"/></w:pPr></w:style>";
        }

        return LayoutModel.StylesPart(text);
    }

    private static WordDocument HeadingDocument(string? numbering, int count = 9)
    {
        string P(int level, string text) => $"<w:p><w:pPr><w:pStyle w:val=\"Heading{level}\"/></w:pPr><w:r><w:t>{text}</w:t></w:r></w:p>";
        return LayoutModel.Document(P(1, "A") + P(2, "B") + P(1, "C") + P(2, "D") + P(2, "E") + P(3, "F"), HeadingStyles(count), numbering);
    }

    // The labels the layout drew in line order: the first word of every body line that has a label run.
    private static List<string> DrawnLabels(DocumentLayout layout)
    {
        var labels = new List<string>();
        for (var i = 0; i < layout.LaidOutPages; i++)
        {
            var page = layout.Page(i);
            var lines = page.Text.Length == 0 ? [] : page.Text.Split('\n');
            Assert.Equal(lines.Length, page.TextLines.Length);
            for (var l = 0; l < lines.Length; l++)
            {
                if (page.TextLines[l].Runs.Any(r => r.IsLabel))
                {
                    var cut = lines[l].IndexOf(' ', StringComparison.Ordinal);
                    labels.Add(cut < 0 ? lines[l] : lines[l][..cut]);
                }
            }
        }

        return labels;
    }

    private static async Task AssertLabelsAsync(DocumentLayout layout, WordDocument model, string label)
    {
        await layout.LayOutRestAsync(null, CancellationToken.None);
        var expected = ReferenceNumbering.Labels(model).Select(l => l.Label).ToList();
        Assert.Equal(expected, DrawnLabels(layout));
        LayoutOracle.AssertEqualToFull(layout, model, label: label);
    }

    private static async Task SwitchingHeadingNumbersAsync(string? numbering, int count = 9)
    {
        var model = HeadingDocument(numbering, count);
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain);
        var layout = await LaidAsync(model);
        Assert.Empty(DrawnLabels(layout));

        Assert.NotNull(editor.SetHeadingNumbering(true));
        layout.Restyle(0);
        await AssertLabelsAsync(layout, model, "numbers on");
        Assert.Equal(["1.", "1.1.", "2.", "2.1.", "2.2.", "2.2.1."], DrawnLabels(layout));

        Assert.NotNull(editor.History.Undo());
        layout.Restyle(0);
        await AssertLabelsAsync(layout, model, "numbers undone");
        Assert.Empty(DrawnLabels(layout));

        Assert.NotNull(editor.History.Redo());
        layout.Restyle(0);
        await AssertLabelsAsync(layout, model, "numbers redone");
        Assert.Equal(["1.", "1.1.", "2.", "2.1.", "2.2.", "2.2.1."], DrawnLabels(layout));
    }

    [Fact]
    public Task SwitchingHeadingNumbersOnGivesTheLayoutNumbers() => SwitchingHeadingNumbersAsync(
        LayoutModel.NumberingPart(string.Empty));

    [Fact]
    public Task SwitchingHeadingNumbersOnInADocumentWithoutANumberingPartGivesTheLayoutNumbers() => SwitchingHeadingNumbersAsync(null);

    [Fact]
    public Task SwitchingHeadingNumbersOnADocumentWithOnlyHeading1To3GivesTheLayoutNumbers() => SwitchingHeadingNumbersAsync(
        LayoutModel.NumberingPart(string.Empty), 3);

    [Fact]
    public Task SwitchingHeadingNumbersOnADocumentWithOnlyHeading1To3AndNoNumberingPartGivesTheLayoutNumbers() => SwitchingHeadingNumbersAsync(null, 3);

    private sealed class StillClock : TimeProvider
    {
        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => 0;
    }

    [Fact]
    public async Task AResectionEqualsAFullLayoutAfterAPropertySwap()
    {
        // Three sections of some pages each; the page size of the middle one and the margins of the last one change, then the step is undone and redone.
        string Text(string label) => string.Concat(Enumerable.Range(0, 60).Select(i => PageTestKit.P($"{label} paragraph {i} " + new string('x', 200))));
        var body = Text("one") + PageTestKit.PEnd("end one", PageTestKit.Sect()) + Text("two") + PageTestKit.PEnd("end two", PageTestKit.Sect(width: 500f, height: 700f))
            + Text("three") + PageTestKit.Sect();
        var model = LayoutModel.Document(body);
        var layout = await LaidAsync(model);
        var history = new EditHistory(model, new StillClock(), HistoryLimits.Default);
        var sections = SectionList.Of(model);
        var swaps = new List<SectionSwap>();
        foreach (var (live, width) in new[] { (sections[1].Properties, "4000"), (sections[2].Properties, "4500") })
        {
            var old = live.Clone();
            live.Get(W + "pgSz")!.SetAttributeValue(W + "w", width);
            live.Get(W + "pgMar")!.SetAttributeValue(W + "left", "1500");
            swaps.Add(new SectionSwap(live, old, live.Clone()));
        }

        var revision = history.BeginOperation();
        model.Edits.Raise();
        var caret = new TextPosition(StoryRef.Body, (Paragraph)model.Body.Blocks[0], 0);
        history.Record(new EditResult(caret, [], Sections: new SectionChange(swaps, null, null)), new EditHistory.Point(0, 0), revision, null);
        var pagesBefore = layout.LaidOutPages;

        var refill = layout.Resection(1);

        Assert.True(refill.Restarted);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        Assert.NotEqual(pagesBefore, layout.LaidOutPages);
        LayoutOracle.AssertEqualToFull(layout, model, label: "resection");

        Assert.NotNull(history.Undo()?.Sections);
        layout.Resection(0);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        Assert.Equal(pagesBefore, layout.LaidOutPages);
        LayoutOracle.AssertEqualToFull(layout, model, label: "resection undone");

        Assert.NotNull(history.Redo()?.Sections);
        layout.Resection(2);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(layout, model, label: "resection redone");
    }
}
