using System.Diagnostics;
using System.Globalization;
using System.Text;
using Raffica.TestSupport;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Editing;
using Raffica.Write.Tests.Support;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>
/// The gate of milestone 4.4a: over the whole corpus and three generated documents with numbered headings, every structure step (chapter moves, promote and demote, deleting and inserting
/// headings, the heading numbering switch, list toggles, level changes, restarts, undo and redo) is brought into the layout as the session does and then compared, exactly, with a full
/// layout, and the labels drawn on the pages are compared with the independent count of <see cref="ReferenceNumbering"/>: a number on a page is never stale.
/// </summary>
public sealed class StructureOracleTests
{
    private const int StepsPerFile = 30;
    private const int StepsOnTheThesis = 6;

    private static string Root => Path.Combine(TestData.Root, "testdata", "write-corpus");

    private static string Corpus(string id) => Path.Combine(Root, id, id + ".docx");

    /// <summary>The corpus files and the three generated documents (<c>structure-gen-1</c> to <c>structure-gen-3</c>).</summary>
    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            data.Add(entry.Id);
        }

        for (var i = 1; i <= 3; i++)
        {
            data.Add("structure-gen-" + i);
        }

        return data;
    }

    private static int StepsFor(string name) => name == "thesis" ? StepsOnTheThesis : StepsPerFile;

    // FNV-1a over the UTF-16 code units: stable across processes (string.GetHashCode is randomized per process).
    private static uint Fnv1a(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text)
        {
            hash = unchecked((hash ^ c) * 16777619u);
        }

        return hash;
    }

    private static Random RandomFor(string name) => new(unchecked((int)Fnv1a("structure " + name)));

    internal static DocxFile Open(string name, TempFolder folder)
    {
        if (!name.StartsWith("structure-gen-", StringComparison.Ordinal))
        {
            return DocxFile.Open(Corpus(name));
        }

        var path = folder.File(name + ".docx");
        var (body, styles, numbering) = Generated(int.Parse(name["structure-gen-".Length..], CultureInfo.InvariantCulture));
        DocxBuilder.WithStylesAndNumbering(path, body, styles, numbering);
        return DocxFile.Open(path);
    }

    private static async Task<DocumentLayout> LaidAsync(WordDocument model)
    {
        var layout = DocumentLayout.Create(model, PageTestKit.Fonts, new LayoutOptions());
        await layout.LayOutRestAsync(null, CancellationToken.None);
        Assert.True(layout.IsComplete);
        return layout;
    }

    private static string Filler(Random random, int words)
    {
        string[] pool = ["the", "chapter", "reads", "a", "long", "line", "of", "plain", "words", "that", "wraps", "across", "the", "page", "and", "fills", "it"];
        var text = new StringBuilder();
        for (var i = 0; i < words; i++)
        {
            text.Append(pool[random.Next(pool.Length)]).Append(' ');
        }

        return text.ToString().TrimEnd();
    }

    private static string Para(string text, string pPr = "") => $"<w:p><w:pPr>{pPr}</w:pPr><w:r><w:t xml:space=\"preserve\">{text}</w:t></w:r></w:p>";

    private static string Heading(int level, string text) => Para(text, $"<w:pStyle w:val=\"Heading{level}\"/>");

    private static string Item(int numId, int level, string text) =>
        Para(text, $"<w:pStyle w:val=\"ListParagraph\"/><w:numPr><w:ilvl w:val=\"{level}\"/><w:numId w:val=\"{numId}\"/></w:numPr>");

    /// <summary>A document of chapters, sections and lists made from a seed: the Heading styles carry no numbering (the steps switch it on).</summary>
    private static (string Body, string Styles, string Numbering) Generated(int seed)
    {
        var random = new Random(seed * 7919);
        var body = new StringBuilder();
        var chapters = 8 + (seed * 5);
        for (var c = 1; c <= chapters; c++)
        {
            body.Append(Heading(1, "Chapter " + c));
            for (var p = random.Next(2, 5); p > 0; p--)
            {
                body.Append(Para(Filler(random, random.Next(30, 90))));
            }

            for (var s = random.Next(0, 3); s > 0; s--)
            {
                body.Append(Heading(2, $"Section {c}.{s}"));
                body.Append(Para(Filler(random, random.Next(20, 60))));
                if (random.Next(2) == 0)
                {
                    body.Append(Heading(3, $"Detail {c}.{s}"));
                    body.Append(Para(Filler(random, random.Next(10, 40))));
                }

                if (random.Next(2) == 0)
                {
                    var numId = 1 + random.Next(2);
                    for (var i = random.Next(2, 5); i > 0; i--)
                    {
                        body.Append(Item(numId, random.Next(2), Filler(random, random.Next(4, 20))));
                    }
                }
            }
        }

        var styles = new StringBuilder("<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:pPr><w:spacing w:after=\"120\"/></w:pPr></w:style>");
        styles.Append("<w:style w:type=\"paragraph\" w:styleId=\"ListParagraph\"><w:name w:val=\"List Paragraph\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:ind w:left=\"720\"/></w:pPr></w:style>");
        for (var i = 1; i <= 9; i++)
        {
            styles.Append(CultureInfo.InvariantCulture, $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{i}\"><w:name w:val=\"heading {i}\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:pPr><w:keepNext/><w:keepLines/><w:spacing w:before=\"{240 - (i * 10)}\" w:after=\"80\"/><w:outlineLvl w:val=\"{i - 1}\"/></w:pPr><w:rPr><w:b/><w:sz w:val=\"{36 - (i * 2)}\"/></w:rPr></w:style>");
        }

        var numbering = new StringBuilder();
        numbering.Append("<w:abstractNum w:abstractNumId=\"0\">");
        for (var l = 0; l < 9; l++)
        {
            numbering.Append(CultureInfo.InvariantCulture, $"<w:lvl w:ilvl=\"{l}\"><w:start w:val=\"1\"/><w:numFmt w:val=\"{(l % 2 == 0 ? "decimal" : "lowerLetter")}\"/><w:lvlText w:val=\"%{l + 1}.\"/><w:pPr><w:ind w:left=\"{720 * (l + 1)}\" w:hanging=\"360\"/></w:pPr></w:lvl>");
        }

        numbering.Append("</w:abstractNum><w:abstractNum w:abstractNumId=\"1\">");
        for (var l = 0; l < 9; l++)
        {
            numbering.Append(CultureInfo.InvariantCulture, $"<w:lvl w:ilvl=\"{l}\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"•\"/><w:pPr><w:ind w:left=\"{720 * (l + 1)}\" w:hanging=\"360\"/></w:pPr></w:lvl>");
        }

        numbering.Append("</w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\"/></w:num><w:num w:numId=\"2\"><w:abstractNumId w:val=\"1\"/></w:num>");
        return (body.ToString(), LayoutModel.StylesPart(styles.ToString()), LayoutModel.NumberingPart(numbering.ToString()));
    }

    // Lines of one-line paragraphs fill page 1 up to a heading that keeps with the next block; the block after it is a long paragraph (it needs two lines to start on the page) and a one-line
    // paragraph follows it. Moving the short one in front of the long one lets the heading stay on page 1 where it had been pushed to page 2 (and the undo pushes it back): the refill must
    // start before the heading, the first block of the run of keep-with-next blocks that ends at the edit. Swept over the number of lines in front so the boundary is hit.
    [Fact]
    public async Task AMoveUnderAKeepWithNextHeadingPullsItBackToThePageBeforeAndStaysEqualToFullLayout()
    {
        var longText = string.Concat(Enumerable.Repeat("a long paragraph that needs several lines on the page ", 12));
        var pulled = 0;
        for (var fillers = 20; fillers <= 70; fillers++)
        {
            var body = string.Concat(Enumerable.Range(0, fillers).Select(i => PageTestKit.P("line " + i)))
                + PageTestKit.P("Heading", "<w:keepNext/>") + PageTestKit.P(longText) + PageTestKit.P("short");
            var model = LayoutModel.Document(body);
            var layout = DocumentLayout.Create(model, PageTestKit.Fonts, new LayoutOptions());
            await layout.LayOutRestAsync(null, CancellationToken.None);
            var steps = new StructureSteps(model, layout, new LayoutOptions());
            var heading = fillers;
            var before = layout.Locate(heading)?.Page;

            Assert.True(await steps.MoveToAsync(fillers + 2, 1, fillers + 1, false, $"keepNext {fillers} fillers"));

            Assert.Equal(3, steps.Compared);
            if (layout.Locate(heading)?.Page != before)
            {
                pulled++;
            }
        }

        Assert.True(pulled > 0, "no variant moved the heading to another page: the walk-back was not exercised");
    }

    private static string LabelsAndText(WordDocument model) =>
        string.Join("\n", ReferenceNumbering.Labels(model).Select(l => l.Label + "|" + TestEdit.TextOf(l.Paragraph)));

    [Fact]
    public async Task EveryStepKindRanOnTheCorpus()
    {
        var totals = new int[StructureSteps.Kinds];
        long labels = 0;
        using var folder = new TempFolder();
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            using var file = Open(entry.Id, folder);
            var layout = await LaidAsync(file.Model);
            var steps = new StructureSteps(file.Model, layout, new LayoutOptions());
            await steps.RunAsync(RandomFor("kinds " + entry.Id), StepsFor(entry.Id), entry.Id);
            StructureSteps.AssertReadable(entry.Id, steps.Tally);
            for (var k = 0; k < totals.Length; k++)
            {
                totals[k] += steps.RanPerKind[k];
            }

            labels += steps.LabelsCompared;
        }

        foreach (var kind in Enum.GetValues<StructureKind>())
        {
            Assert.True(totals[(int)kind] > 0, $"the step kind {kind} never ran a step that changed something in the corpus (ran per kind: {string.Join(", ", Enum.GetValues<StructureKind>().Select(k => k + " " + totals[(int)k]))})");
        }

        Assert.True(labels > 100, $"only {labels} labels were compared with the reference");
        TestContext.Current.SendDiagnosticMessage("steps per kind on the corpus: " + string.Join(", ", Enum.GetValues<StructureKind>().Select(k => k + " " + totals[(int)k])) + $"; {labels} labels compared");
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task LabelsEqualTheReferenceAfterEveryStep(string name)
    {
        using var folder = new TempFolder();
        using var file = Open(name, folder);
        var layout = await LaidAsync(file.Model);
        var steps = new StructureSteps(file.Model, layout, new LayoutOptions());
        var compared = await steps.RunAsync(RandomFor(name), StepsFor(name), name);
        StructureSteps.AssertReadable(name, steps.Tally);

        if (name.StartsWith("structure-gen-", StringComparison.Ordinal))
        {
            // A generated document has something for every kind: a pass that skipped one would prove less than it claims.
            foreach (var kind in Enum.GetValues<StructureKind>())
            {
                Assert.True(steps.RanPerKind[(int)kind] > 0, $"{name}: the step kind {kind} never ran (refused {steps.RefusedPerKind[(int)kind]} times)");
            }

            Assert.True(steps.LabelsCompared > 500, $"{name}: only {steps.LabelsCompared} labels were compared");
        }

        if (file.Model.Body.Blocks.OfType<Paragraph>().Any())
        {
            Assert.True(compared > 0, $"{name}: no step changed anything");
        }

        TestContext.Current.SendDiagnosticMessage($"{name}: {compared} steps compared, {steps.LabelsCompared} labels, refused {steps.RefusedPerKind.Sum()}, restyles {steps.Restyles}, {steps.Tally}");
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task UndoAllThenRedoAllReturnsToTheSameLabels(string name)
    {
        using var folder = new TempFolder();
        using var file = Open(name, folder);
        var model = file.Model;
        var layout = await LaidAsync(model);
        var steps = new StructureSteps(model, layout, new LayoutOptions());
        var start = LabelsAndText(model);
        var startRevision = model.Edits.Revision;
        var random = RandomFor("undo " + name);
        await steps.RunAsync(random, StepsFor(name) / 2, name);
        StructureSteps.AssertReadable(name, steps.Tally);
        while (steps.Editor.History.CanRedo)
        {
            // Random undo steps left redo steps behind: redo them first, so the final state has every step applied.
            Assert.True(await steps.RunKindAsync(StructureKind.Redo, random, name + " settle"));
        }

        var final = LabelsAndText(model);
        var finalRevision = model.Edits.Revision;
        var undoable = steps.Editor.History.UndoCount;

        var undone = 0;
        while (steps.Editor.History.CanUndo)
        {
            Assert.True(await steps.RunKindAsync(StructureKind.Undo, random, $"{name} undo all {undone}"), $"{name}: undo {undone} changed nothing");
            undone++;
        }

        Assert.Equal(undoable, undone);
        Assert.Equal(start, LabelsAndText(model));
        Assert.Equal(startRevision, model.Edits.Revision);

        var redone = 0;
        while (steps.Editor.History.CanRedo)
        {
            Assert.True(await steps.RunKindAsync(StructureKind.Redo, random, $"{name} redo all {redone}"), $"{name}: redo {redone} changed nothing");
            redone++;
        }

        Assert.Equal(undone, redone);
        Assert.Equal(final, LabelsAndText(model));
        Assert.Equal(finalRevision, model.Edits.Revision);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task TheSavedFileOfTheFinalStateReopensWithTheSameLabels(string name)
    {
        using var folder = new TempFolder();
        using var file = Open(name, folder);
        var layout = await LaidAsync(file.Model);
        var steps = new StructureSteps(file.Model, layout, new LayoutOptions());
        await steps.RunAsync(RandomFor("save " + name), StepsFor(name), name);
        StructureSteps.AssertReadable(name, steps.Tally);
        var tally = new LabelTally();
        var before = ReferenceNumbering.Labels(file.Model).Select(l => l.Label).ToList();

        var target = folder.File("saved.docx");
        await file.SaveAsync(target, TestContext.Current.CancellationToken);
        using var reopened = DocxFile.Open(target);
        var after = ReferenceNumbering.Labels(reopened.Model).Select(l => l.Label).ToList();
        Assert.Equal(before, after);

        var second = await LaidAsync(reopened.Model);
        var drawn = DrawnLabels.AssertEqualToReference(second, reopened.Model, name + " reopened", tally);
        StructureSteps.AssertReadable(name, tally);
        if (drawn.Count == after.Count)
        {
            Assert.Equal(before, drawn.Select(l => l.Label).ToList());
        }
        if (name.StartsWith("structure-gen-", StringComparison.Ordinal))
        {
            Assert.NotEmpty(before);
        }
    }
}

/// <summary>The wall-clock bound of the structure switches on the thesis; the class runs alone after every parallel class has finished.</summary>
[Collection(WallClockGroup.Name)]
public sealed class StructureWallClockTests
{
    private static string Root => Path.Combine(TestData.Root, "testdata", "write-corpus");

    // The bound stays at 5 s: select all, then every list and heading numbering switch, each one undo step (the class sits in WallClockGroup so nothing runs beside it).
    [Fact]
    public void SelectAllThenNumberingOnAndOffIsFastAndOneStepEach()
    {
        var entry = CorpusManifest.Load(Root).Files.Single(e => e.Id == "thesis");
        var path = CorpusCheck.Resolve(Root, entry.Path);
        Assert.NotNull(path);
        using var file = DocxFile.Open(path);
        var model = file.Model;
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain, format: new LayoutEffectiveFormat(model));
        var first = model.Body.Blocks.OfType<Paragraph>().First();
        var last = model.Body.Blocks.OfType<Paragraph>().Last();
        var range = new TextRange(new TextPosition(StoryRef.Body, first, 0), new TextPosition(StoryRef.Body, last, ParagraphProjection.Build(last, ProjectionHost.Plain).Text.Length));
        var labelsBefore = ReferenceNumbering.Labels(model).Select(l => l.Label).ToList();
        var revision = model.Edits.Revision;
        var caret = new TextPosition(StoryRef.Body, first, 0);
        // The thesis defines Heading1 to Heading3 only: the heading switch works on those. Whatever its state is, the first switch flips it and the second flips it back.
        var wasOn = HeadingNumbering.StateOf(model) == HeadingNumberingState.On;
        Assert.NotEqual(HeadingNumberingState.Unavailable, HeadingNumbering.StateOf(model));

        var watch = Stopwatch.StartNew();
        var firstSwitch = editor.SetHeadingNumbering(!wasOn, caret);
        var firstSwitchSteps = editor.History.UndoCount;
        var labelsAfterFirst = ReferenceNumbering.Labels(model).Select(l => l.Label).ToList();
        var secondSwitch = editor.SetHeadingNumbering(wasOn, caret);
        var secondSwitchSteps = editor.History.UndoCount;
        var labelsAfterSecond = ReferenceNumbering.Labels(model).Select(l => l.Label).ToList();
        var on = editor.ToggleList(range, ListKind.Numbered);
        var onSteps = editor.History.UndoCount;
        var off = editor.ToggleList(range, ListKind.Numbered);
        var offSteps = editor.History.UndoCount;
        watch.Stop();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "select-all numbering on and off on the thesis took " + watch.ElapsedMilliseconds + " ms");
        Assert.NotNull(firstSwitch?.Style);
        Assert.Empty(firstSwitch.Changes);
        Assert.Equal(1, firstSwitchSteps);
        Assert.NotEqual(labelsBefore, labelsAfterFirst);
        Assert.NotNull(secondSwitch?.Style);
        Assert.Equal(2, secondSwitchSteps);
        Assert.Equal(labelsBefore, labelsAfterSecond);
        Assert.NotEmpty(on.Changes);
        Assert.Equal(3, onSteps);
        Assert.NotEmpty(off.Changes);
        Assert.Equal(4, offSteps);
        // Select all, numbered list on and off again: the second toggle finds every paragraph in a numbered list and removes it, a list that comes from a style (the headings) by a direct numId 0.
        Assert.True(labelsBefore.Count > 0);
        Assert.Empty(ReferenceNumbering.Labels(model));

        while (editor.History.CanUndo)
        {
            Assert.NotNull(editor.History.Undo());
        }

        Assert.Equal(revision, model.Edits.Revision);
        Assert.Equal(labelsBefore, ReferenceNumbering.Labels(model).Select(l => l.Label).ToList());
    }

    // A generated document with all nine heading styles (a long one, 23 chapters) takes the timed switch as well.
    [Fact]
    public void TheHeadingNumberingSwitchOnAGeneratedDocumentIsFastAndOneStepEachWay()
    {
        using var folder = new TempFolder();
        using var file = StructureOracleTests.Open("structure-gen-3", folder);
        var model = file.Model;
        var editor = new TextEditor(model, (_, _) => ProjectionHost.Plain, format: new LayoutEffectiveFormat(model));
        var caret = new TextPosition(StoryRef.Body, model.Body.Blocks.OfType<Paragraph>().First(), 0);
        var revision = model.Edits.Revision;
        Assert.Equal(HeadingNumberingState.Off, HeadingNumbering.StateOf(model));

        var watch = Stopwatch.StartNew();
        var on = editor.SetHeadingNumbering(true, caret);
        var onSteps = editor.History.UndoCount;
        var off = editor.SetHeadingNumbering(false, caret);
        watch.Stop();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "the heading numbering switch took " + watch.ElapsedMilliseconds + " ms");
        Assert.NotNull(on);
        Assert.NotNull(on.Style);
        Assert.Equal(1, onSteps);
        Assert.NotNull(off);
        Assert.NotNull(off.Style);
        Assert.Equal(2, editor.History.UndoCount);
        Assert.Equal(HeadingNumberingState.Off, HeadingNumbering.StateOf(model));
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(HeadingNumberingState.On, HeadingNumbering.StateOf(model));
        Assert.NotNull(editor.History.Undo());
        Assert.Equal(revision, model.Edits.Revision);
    }
}
