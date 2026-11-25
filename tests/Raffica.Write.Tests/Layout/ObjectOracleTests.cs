using Raffica.Write.Docx;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Editing;
using Raffica.Write.Tests.Support;
using WriteCorpus;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>
/// The gate of the object steps of milestone 4.5a (notes and tables now, pictures later): over the whole corpus, every note and table step (a footnote or an endnote inserted, a reference deleted with its note, undo and
/// redo) is brought into the layout as the session does and compared, exactly, with a full layout, and the note numbers drawn on the pages are compared with the independent count of the references in
/// document order: a note number is never stale.
/// </summary>
public sealed class ObjectOracleTests
{
    private const int StepsPerFile = 14;
    private const int StepsOnTheThesis = 4;

    private static string Root => Path.Combine(Raffica.TestSupport.TestData.Root, "testdata", "write-corpus");

    /// <summary>The corpus files.</summary>
    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var entry in CorpusManifest.Load(Root).Files)
        {
            data.Add(entry.Id);
        }

        return data;
    }

    private static int StepsFor(string name) => name == "thesis" ? StepsOnTheThesis : StepsPerFile;

    // FNV-1a over the UTF-16 code units: stable across processes (string.GetHashCode is randomized per process).
    private static Random RandomFor(string name)
    {
        var hash = 2166136261u;
        foreach (var c in "objects " + name)
        {
            hash = unchecked((hash ^ c) * 16777619u);
        }

        return new Random(unchecked((int)hash));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task NoteStepsEqualAFullLayoutOnTheCorpus(string name)
    {
        using var file = DocxFile.Open(Path.Combine(Root, name, name + ".docx"));
        var layout = await SectionOracleTests.LaidAsync(file.Model);
        var steps = new ObjectSteps(file.Model, layout, new LayoutOptions());

        var compared = await steps.RunAsync(RandomFor(name), StepsFor(name), name);

        if (file.Model.Body.Blocks.OfType<Paragraph>().Any())
        {
            Assert.True(compared > 0, $"{name}: no step changed anything");
        }

        TestContext.Current.SendDiagnosticMessage($"{name}: {compared} steps compared, {steps.NumbersCompared} note numbers, refused {steps.RefusedPerKind.Sum()}, restyles {steps.Restyles}");
    }

    // A guard that only the per-file runs carry would stay green if a kind were skipped everywhere.
    [Fact]
    public async Task EveryNoteStepKindRanOnTheCorpus()
    {
        var totals = new int[ObjectSteps.Kinds];
        long numbers = 0;
        long grids = 0;
        foreach (var entry in CorpusManifest.Load(Root).Files.Where(f => f.Id != "thesis"))
        {
            using var file = DocxFile.Open(Path.Combine(Root, entry.Id, entry.Id + ".docx"));
            var layout = await SectionOracleTests.LaidAsync(file.Model);
            var steps = new ObjectSteps(file.Model, layout, new LayoutOptions());
            await steps.RunAsync(RandomFor("kinds " + entry.Id), 6, entry.Id);
            for (var k = 0; k < totals.Length; k++)
            {
                totals[k] += steps.RanPerKind[k];
            }

            numbers += steps.NumbersCompared;
            grids += steps.GridsCompared;
        }

        foreach (var kind in Enum.GetValues<ObjectKind>())
        {
            Assert.True(totals[(int)kind] > 0, $"the step kind {kind} never ran a step that changed something in the corpus (ran per kind: {string.Join(", ", Enum.GetValues<ObjectKind>().Select(k => k + " " + totals[(int)k]))})");
        }

        Assert.True(numbers > 100, $"only {numbers} note numbers were compared with the reference");
        Assert.True(grids > 50, $"only {grids} table pieces were compared with the model grid");
        TestContext.Current.SendDiagnosticMessage("note steps per kind on the corpus: " + string.Join(", ", Enum.GetValues<ObjectKind>().Select(k => k + " " + totals[(int)k])) + $"; {numbers} note numbers compared");
    }

    private static readonly ObjectKind[] s_tableKinds =
    [
        ObjectKind.InsertTable, ObjectKind.AddRow, ObjectKind.AddColumn, ObjectKind.DeleteRows, ObjectKind.DeleteColumns, ObjectKind.Merge, ObjectKind.Split, ObjectKind.MoveRow, ObjectKind.DeleteTable,
        ObjectKind.AddRow, ObjectKind.AddColumn, ObjectKind.Merge, ObjectKind.Split, ObjectKind.MoveRow, ObjectKind.Undo, ObjectKind.Redo,
    ];

    // Tables with merges in both directions, spans and the three width modes (the corpus has no vertical merge): table steps only, each compared with a full layout and with the model grid.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task TableStepsEqualAFullLayoutOnGeneratedTablesWithMerges(int seed)
    {
        using var folder = new TempFolder();
        var path = folder.File("tables.docx");
        DocxBuilder.WithBody(path, TableCorpusStructureTests.GeneratedBody());
        using var file = DocxFile.Open(path);
        var layout = await SectionOracleTests.LaidAsync(file.Model);
        var steps = new ObjectSteps(file.Model, layout, new LayoutOptions());

        var compared = await steps.RunAsync(new Random(unchecked(seed * 7919)), 45, "generated " + seed, s_tableKinds);

        Assert.True(compared > 20, $"only {compared} of 45 steps changed something");
        Assert.True(steps.GridsCompared > 20, $"only {steps.GridsCompared} table pieces were compared with the model grid");
        TestContext.Current.SendDiagnosticMessage($"generated {seed}: {compared} steps compared, {steps.GridsCompared} table pieces, refused {steps.RefusedPerKind.Sum()}");
    }

    // The notes with text: a part made by a step stays after its undo (with its separators), as a numbering part does.
    private static (int Footnotes, int Endnotes) Counted(WordDocument model) =>
        (model.Footnotes?.Notes.Count(n => n.Kind == NoteKind.Normal) ?? 0, model.Endnotes?.Notes.Count(n => n.Kind == NoteKind.Normal) ?? 0);

    [Fact]
    public async Task UndoAllThenRedoAllReturnsToTheSameNotes()
    {
        using var file = DocxFile.Open(Path.Combine(Root, "thesis", "thesis.docx"));
        var model = file.Model;
        var layout = await SectionOracleTests.LaidAsync(model);
        var steps = new ObjectSteps(model, layout, new LayoutOptions());
        var startNotes = Counted(model);
        var startRevision = model.Edits.Revision;
        var random = RandomFor("undo thesis");

        await steps.RunAsync(random, 3, "thesis");
        while (steps.Editor.History.CanRedo)
        {
            Assert.True(await steps.RunKindAsync(ObjectKind.Redo, random, "thesis settle"));
        }

        var finalNotes = Counted(model);
        var finalRevision = model.Edits.Revision;
        var undone = 0;
        while (steps.Editor.History.CanUndo)
        {
            Assert.True(await steps.RunKindAsync(ObjectKind.Undo, random, $"thesis undo all {undone}"));
            undone++;
        }

        Assert.Equal(startNotes, Counted(model));
        Assert.Equal(startRevision, model.Edits.Revision);
        while (steps.Editor.History.CanRedo)
        {
            Assert.True(await steps.RunKindAsync(ObjectKind.Redo, random, "thesis redo all"));
        }

        Assert.Equal(finalNotes, Counted(model));
        Assert.Equal(finalRevision, model.Edits.Revision);
    }
}
