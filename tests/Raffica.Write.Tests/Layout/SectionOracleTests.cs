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
/// The gate of milestone 4.4b: over the whole corpus and three generated documents with several sections, every section step (numbering the pages from a place, section breaks and their removal,
/// own and inherited footers and headers, a different first page, left and right pages, an inserted page number, the page setups, undo and redo) is brought into the layout as the session does and then
/// compared, exactly, with a full layout, and the page numbers drawn on the pages are compared with the independent count of <see cref="ReferencePageNumbers"/>: a page number is never stale.
/// </summary>
public sealed class SectionOracleTests
{
    private const int StepsPerFile = 30;
    private const int StepsOnTheThesis = 6;

    private static string Root => Path.Combine(TestData.Root, "testdata", "write-corpus");

    private static string Corpus(string id) => Path.Combine(Root, id, id + ".docx");

    /// <summary>The corpus files and the three generated documents (<c>section-gen-1</c> to <c>section-gen-3</c>).</summary>
    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            data.Add(entry.Id);
        }

        for (var i = 1; i <= 3; i++)
        {
            data.Add("section-gen-" + i);
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

    private static Random RandomFor(string name) => new(unchecked((int)Fnv1a("sections " + name)));

    internal static DocxFile Open(string name, TempFolder folder)
    {
        if (!name.StartsWith("section-gen-", StringComparison.Ordinal))
        {
            return DocxFile.Open(Corpus(name));
        }

        var path = folder.File(name + ".docx");
        DocxBuilder.WithBody(path, Generated(int.Parse(name["section-gen-".Length..], CultureInfo.InvariantCulture)));
        return DocxFile.Open(path);
    }

    internal static async Task<DocumentLayout> LaidAsync(WordDocument model)
    {
        var layout = DocumentLayout.Create(model, PageTestKit.Fonts, new LayoutOptions());
        await layout.LayOutRestAsync(null, CancellationToken.None);
        Assert.True(layout.IsComplete);
        return layout;
    }

    private static string Filler(Random random, int words)
    {
        string[] pool = ["the", "section", "reads", "a", "long", "line", "of", "plain", "words", "that", "wraps", "across", "the", "page", "and", "fills", "it"];
        var text = new StringBuilder();
        for (var i = 0; i < words; i++)
        {
            text.Append(pool[random.Next(pool.Length)]).Append(' ');
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>A document of several sections of several pages: some start at a given number or count in another format, one has a different first page, the last section is the body's own.</summary>
    private static string Generated(int seed)
    {
        var random = new Random(seed * 104729);
        var body = new StringBuilder();
        var sections = 2 + seed;
        for (var s = 0; s < sections; s++)
        {
            for (var p = random.Next(8, 20); p > 0; p--)
            {
                body.Append(PageTestKit.P($"Section {s} " + Filler(random, random.Next(40, 120))));
            }

            var extra = new StringBuilder();
            extra.Append(CultureInfo.InvariantCulture, $"<w:type w:val=\"{(s % 2 == 0 ? "nextPage" : "continuous")}\"/>");
            if (s > 0 && s % 2 == 0)
            {
                extra.Append("<w:pgNumType w:start=\"5\" w:fmt=\"lowerRoman\"/>");
            }

            if (s == sections - 1)
            {
                body.Append(PageTestKit.Sect(extra.ToString().Replace("continuous", "nextPage", StringComparison.Ordinal)));
            }
            else
            {
                body.Append(PageTestKit.PEnd($"End {s}", PageTestKit.Sect(extra.ToString())));
            }
        }

        return body.ToString();
    }

    // The drawn numbers and the section count: what a step must not change after undo and redo.
    internal static string NumbersOf(DocumentLayout layout, WordDocument model) =>
        string.Join(",", DrawnPageNumbers.Read(layout, model).Select(d => d.Number)) + "|" + SectionList.Of(model).Count;

    [Fact]
    public async Task EveryStepKindRanOnTheCorpus()
    {
        var totals = new int[SectionSteps.Kinds];
        long pages = 0;
        long skipped = 0;
        using var folder = new TempFolder();
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            using var file = Open(entry.Id, folder);
            var layout = await LaidAsync(file.Model);
            var steps = new SectionSteps(file.Model, layout, new LayoutOptions());
            await steps.RunAsync(RandomFor("kinds " + entry.Id), StepsFor(entry.Id), entry.Id);
            for (var k = 0; k < totals.Length; k++)
            {
                totals[k] += steps.RanPerKind[k];
            }

            pages += steps.PagesCompared;
            skipped += steps.PagesSkipped;
        }

        foreach (var kind in Enum.GetValues<SectionKind>())
        {
            Assert.True(totals[(int)kind] > 0, $"the step kind {kind} never ran a step that changed something in the corpus (ran per kind: {string.Join(", ", Enum.GetValues<SectionKind>().Select(k => k + " " + totals[(int)k]))})");
        }

        Assert.True(pages > 1000, $"only {pages} page numbers were compared with the reference");
        Assert.True(skipped * 10 < pages + skipped, $"{skipped} of {pages + skipped} page numbers were left out of the comparison on the corpus (more than 10 percent)");
        TestContext.Current.SendDiagnosticMessage("section steps per kind on the corpus: " + string.Join(", ", Enum.GetValues<SectionKind>().Select(k => k + " " + totals[(int)k])) + $"; {pages} page numbers compared, {skipped} left out");
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task NumbersEqualTheReferenceAfterEveryStep(string name)
    {
        using var folder = new TempFolder();
        using var file = Open(name, folder);
        var layout = await LaidAsync(file.Model);
        var steps = new SectionSteps(file.Model, layout, new LayoutOptions());
        var compared = await steps.RunAsync(RandomFor(name), StepsFor(name), name);

        if (name.StartsWith("section-gen-", StringComparison.Ordinal))
        {
            // A generated document has something for every kind: a pass that skipped one would prove less than it claims.
            foreach (var kind in Enum.GetValues<SectionKind>())
            {
                Assert.True(steps.RanPerKind[(int)kind] > 0, $"{name}: the step kind {kind} never ran (refused {steps.RefusedPerKind[(int)kind]} times)");
            }
        }

        if (file.Model.Body.Blocks.OfType<Paragraph>().Any())
        {
            Assert.True(compared > 0, $"{name}: no step changed anything");
        }

        TestContext.Current.SendDiagnosticMessage($"{name}: {compared} steps compared, {steps.PagesCompared} page numbers, refused {steps.RefusedPerKind.Sum()}, resections {steps.Resections}, {steps.PagesSkipped} of {steps.PagesCompared + steps.PagesSkipped} page numbers left out (unverified states)");
    }

    // The unverified state (a wrong-parity start on an even-page or odd-page section; a start on a continuous section is compared since the Word check of 2026-11-23) is left out of the number comparison, and only the pages it affects. On the three
    // generated documents the allowance is reached at least once (so it is exercised) and stays under 10 percent of the compared pages.
    [Fact]
    public async Task TheUnverifiedStatesAreReachedOnTheGeneratedDocumentsAndStayBounded()
    {
        long compared = 0;
        long skipped = 0;
        using var folder = new TempFolder();
        for (var i = 1; i <= 3; i++)
        {
            var name = "section-gen-" + i;
            using var file = Open(name, folder);
            var layout = await LaidAsync(file.Model);
            var steps = new SectionSteps(file.Model, layout, new LayoutOptions());
            await steps.RunAsync(RandomFor("bound " + name), 60, name);
            compared += steps.PagesCompared;
            skipped += steps.PagesSkipped;
        }

        TestContext.Current.SendDiagnosticMessage($"generated documents: {compared} page numbers compared, {skipped} left out");
        Assert.True(skipped > 0, "no step reached an unverified state: the allowance is not exercised");
        Assert.True(skipped * 10 < compared + skipped, $"{skipped} of {compared + skipped} page numbers were left out of the comparison (more than 10 percent)");
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task UndoAllThenRedoAllReturnsToTheSameNumbers(string name)
    {
        using var folder = new TempFolder();
        using var file = Open(name, folder);
        var model = file.Model;
        var layout = await LaidAsync(model);
        var steps = new SectionSteps(model, layout, new LayoutOptions());
        var start = NumbersOf(layout, model);
        var startRevision = model.Edits.Revision;
        var random = RandomFor("undo " + name);
        await steps.RunAsync(random, StepsFor(name) / 2, name);
        while (steps.Editor.History.CanRedo)
        {
            // Random undo steps left redo steps behind: redo them first, so the final state has every step applied.
            Assert.True(await steps.RunKindAsync(SectionKind.Redo, random, name + " settle"));
        }

        var final = NumbersOf(layout, model);
        var finalRevision = model.Edits.Revision;
        var undoable = steps.Editor.History.UndoCount;

        var undone = 0;
        while (steps.Editor.History.CanUndo)
        {
            Assert.True(await steps.RunKindAsync(SectionKind.Undo, random, $"{name} undo all {undone}"), $"{name}: undo {undone} changed nothing");
            undone++;
        }

        Assert.Equal(undoable, undone);
        Assert.Equal(start, NumbersOf(layout, model));
        Assert.Equal(startRevision, model.Edits.Revision);

        var redone = 0;
        while (steps.Editor.History.CanRedo)
        {
            Assert.True(await steps.RunKindAsync(SectionKind.Redo, random, $"{name} redo all {redone}"), $"{name}: redo {redone} changed nothing");
            redone++;
        }

        Assert.Equal(undone, redone);
        Assert.Equal(final, NumbersOf(layout, model));
        Assert.Equal(finalRevision, model.Edits.Revision);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task TheSavedFileOfTheFinalStateReopensWithTheSameNumbers(string name)
    {
        using var folder = new TempFolder();
        using var file = Open(name, folder);
        var layout = await LaidAsync(file.Model);
        var steps = new SectionSteps(file.Model, layout, new LayoutOptions());
        await steps.RunAsync(RandomFor("save " + name), StepsFor(name), name);
        var before = NumbersOf(layout, file.Model);

        var target = folder.File("saved.docx");
        await file.SaveAsync(target, TestContext.Current.CancellationToken);
        using var reopened = DocxFile.Open(target);
        var second = await LaidAsync(reopened.Model);
        // The reopened file is compared with the reference as every step is (only the pages of the unverified states are left out).
        var reopenedCheck = SectionSteps.CheckNumbers(second, reopened.Model, name + " reopened");
        Assert.Equal(second.LaidOutPages, reopenedCheck.Compared + reopenedCheck.Skipped);

        // A section break that sits in the carrier paragraph survives the file; the numbers of the reopened document are those of the edited one.
        Assert.Equal(before, NumbersOf(second, reopened.Model));
    }

    // The section steps again with every step followed by the bytes check of the recovery copy's cache: the cached serialisation of the main part equals a full one.
    [Fact]
    public async Task EverySectionStepGivesTheBytesOfAFullWrite()
    {
        using var folder = new TempFolder();
        using var file = Open("poi-page-specific-head-foot", folder);
        var layout = await LaidAsync(file.Model);
        var steps = new SectionSteps(file.Model, layout, new LayoutOptions());
        file.SerialiseMainForTest(cached: true);
        steps.AfterStep = label =>
        {
            var cached = file.SerialiseMainForTest(cached: true);
            Assert.True(cached.AsSpan().SequenceEqual(file.SerialiseMainForTest(cached: false)), label + ": main part");
        };

        await steps.RunAsync(RandomFor("bytes"), 24, "bytes");
        foreach (var kind in Enum.GetValues<SectionKind>())
        {
            Assert.True(steps.RanPerKind[(int)kind] > 0, $"the step kind {kind} never ran");
        }
    }
}

/// <summary>The wall-clock bounds of the section commands; the class runs alone after every parallel class has finished.</summary>
[Collection(WallClockGroup.Name)]
public sealed class SectionWallClockTests
{
    private static string Root => Path.Combine(TestData.Root, "testdata", "write-corpus");

    // A thousand next-page sections of one paragraph each: one command (a page setup for the whole document), the layout started over and laid out again, within 5 s.
    [Fact]
    public async Task AThousandSectionsStayWithinTheBound()
    {
        var body = new StringBuilder();
        for (var i = 0; i < 1000; i++)
        {
            body.Append(PageTestKit.PEnd("section " + i, PageTestKit.Sect()));
        }

        body.Append(PageTestKit.P("last")).Append(PageTestKit.Sect());
        var model = LayoutModel.Document(body.ToString());
        var options = new LayoutOptions();
        var layout = DocumentLayout.Create(model, PageTestKit.Fonts, options);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        Assert.Equal(1001, SectionList.Of(model).Count);
        var editor = new TextEditor(model, (s, p) => layout.ProjectionHost(s, p));
        var at = new TextPosition(StoryRef.Body, model.Body.Blocks.OfType<Paragraph>().ElementAt(500), 0);
        var change = new PageSetupChange(SectionPageSetup.Read(null) with { WidthTwips = 8391, HeightTwips = 11906 }, PageSetupParts.Size);

        var watch = Stopwatch.StartNew();
        var result = editor.ApplyPageSetup(at, change, ApplyScope.WholeDocument);
        Assert.NotNull(result?.Sections);
        layout.Resection(2);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        watch.Stop();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "a page setup of a thousand sections took " + watch.ElapsedMilliseconds + " ms");
        Assert.Equal(1, editor.History.UndoCount);
        SectionSteps.AssertNumbersEqualTheReference(layout, model, "a thousand sections");
        Assert.NotNull(editor.History.Undo());
        Assert.True(editor.History.CanRedo);
    }

    // Number the pages from page three of the thesis (a new section, or the section that starts there), then remove what that made: one history step each, and the numbers equal the reference.
    [Fact]
    public async Task NumberFromPageThreeThenRemoveIsFastAndOneStepEach()
    {
        var entry = CorpusManifest.Load(Root).Files.Single(e => e.Id == "thesis");
        var path = CorpusCheck.Resolve(Root, entry.Path);
        Assert.NotNull(path);
        using var file = DocxFile.Open(path);
        var model = file.Model;
        var layout = await SectionOracleTests.LaidAsync(model);
        var editor = new TextEditor(model, (s, p) => layout.ProjectionHost(s, p));
        var blocks = model.Body.Blocks;
        var starts = SectionList.Of(model).Select(s => s.FirstBlock).ToHashSet();
        var block = Enumerable.Range(0, blocks.Count).First(i => blocks[i] is Paragraph && layout.Locate(i)?.Page == 2 && !starts.Contains(i));
        var at = new TextPosition(StoryRef.Body, (Paragraph)blocks[block], 0);
        var sectionsBefore = SectionList.Of(model).Count;
        var revision = model.Edits.Revision;
        var numbersBefore = SectionOracleTests.NumbersOf(layout, model);

        var watch = Stopwatch.StartNew();
        var numbered = editor.NumberPagesFrom(at, new PageNumbering(7, PageNumberFormat.UpperRoman, string.Empty));
        Assert.NotNull(numbered?.Sections);
        layout.Resection(2);
        watch.Stop();
        var numberedMs = watch.ElapsedMilliseconds;
        await layout.LayOutRestAsync(null, CancellationToken.None);
        Assert.Equal(1, editor.History.UndoCount);
        SectionSteps.AssertNumbersEqualTheReference(layout, model, "the thesis numbered from page three");

        var sectionsAfter = SectionList.Of(model).Count;
        // A paragraph in the middle of a section: the command splits it, so the removal below is always exercised.
        Assert.Equal(sectionsBefore + 1, sectionsAfter);
        long removedMs;
        {
            var section = SectionList.Of(model).Select((info, i) => (info, i)).First(x => x.info.FirstBlock >= block).i;
            watch.Restart();
            var removed = editor.RemoveSectionBreak(section);
            Assert.NotNull(removed?.Sections);
            layout.Resection(2);
            watch.Stop();
            await layout.LayOutRestAsync(null, CancellationToken.None);
            Assert.Equal(2, editor.History.UndoCount);
            SectionSteps.AssertNumbersEqualTheReference(layout, model, "the thesis after the break was removed");
            Assert.Equal(sectionsBefore, SectionList.Of(model).Count);
            removedMs = watch.ElapsedMilliseconds;
        }

        Assert.True(removedMs < 5000, "removing the section break of the thesis took " + removedMs + " ms");
        Assert.True(numberedMs < 5000, "numbering the thesis from page three took " + numberedMs + " ms");
        while (editor.History.CanUndo)
        {
            Assert.NotNull(editor.History.Undo());
        }

        layout.Resection(0);
        await layout.LayOutRestAsync(null, CancellationToken.None);
        Assert.Equal(revision, model.Edits.Revision);
        Assert.Equal(numbersBefore, SectionOracleTests.NumbersOf(layout, model));
    }
}
