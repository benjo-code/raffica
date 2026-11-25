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

    // A guard that only the per-seed runs carry would stay green if a kind never changed anything on the generated tables.
    [Fact]
    public async Task EveryTableStepKindChangedSomethingOnTheGeneratedTables()
    {
        var ran = new int[ObjectSteps.Kinds];
        var refused = new int[ObjectSteps.Kinds];
        long rowSpans = 0;
        for (var seed = 1; seed <= 6; seed++)
        {
            using var folder = new TempFolder();
            var path = folder.File("tables.docx");
            DocxBuilder.WithBody(path, TableCorpusStructureTests.GeneratedBody());
            using var file = DocxFile.Open(path);
            var layout = await SectionOracleTests.LaidAsync(file.Model);
            var steps = new ObjectSteps(file.Model, layout, new LayoutOptions());
            await steps.RunAsync(new Random(unchecked(seed * 7919)), 45, "generated " + seed, s_tableKinds);
            for (var k = 0; k < ran.Length; k++)
            {
                ran[k] += steps.RanPerKind[k];
                refused[k] += steps.RefusedPerKind[k];
            }

            rowSpans += steps.RowSpansCompared;
        }

        var report = string.Join(", ", Enum.GetValues<ObjectKind>().Select(k => $"{k} ran {ran[(int)k]} refused {refused[(int)k]}"));
        TestContext.Current.SendDiagnosticMessage("generated table steps: " + report + $"; {rowSpans} row spans compared");
        foreach (var kind in s_tableKinds.Distinct().Where(k => k is not (ObjectKind.Undo or ObjectKind.Redo)))
        {
            Assert.True(ran[(int)kind] > 0, $"the step kind {kind} never changed anything on the generated tables ({report})");
        }

        // Merge, Split and Move row are refused for good reasons now and then (a merge that cannot be cut, a split of a plain cell), but not nearly always.
        foreach (var kind in new[] { ObjectKind.Merge, ObjectKind.Split, ObjectKind.MoveRow })
        {
            Assert.True(refused[(int)kind] <= ran[(int)kind] * 3, $"{kind} was refused {refused[(int)kind]} times and changed something {ran[(int)kind]} times ({report})");
        }

        Assert.True(rowSpans > 500, $"only {rowSpans} cell rectangles had their row span compared with the model");
    }

    private static readonly ObjectKind[] s_formatKinds =
    [
        ObjectKind.SetBorders, ObjectKind.SetShading, ObjectKind.SetHeaderRow, ObjectKind.SetColumnWidths, ObjectKind.SetTableWidth, ObjectKind.SetRowHeight, ObjectKind.DistributeColumns,
        ObjectKind.AddRow, ObjectKind.AddColumn, ObjectKind.Merge, ObjectKind.Split, ObjectKind.DeleteRows, ObjectKind.MoveRow, ObjectKind.DeleteColumns,
        ObjectKind.SetBorders, ObjectKind.SetShading, ObjectKind.SetColumnWidths, ObjectKind.Undo, ObjectKind.Redo,
    ];

    // The formatting steps (borders, shading, header row, widths, row height, distribute) mixed with structure steps on tables with merges in both directions and the three width modes: each step is compared with a full
    // layout and with the model grid, and the widths after a width write agree in the grid, the table width and every cell.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task TableFormattingStepsEqualAFullLayoutOnGeneratedTables(int seed)
    {
        using var folder = new TempFolder();
        var path = folder.File("tables.docx");
        DocxBuilder.WithBody(path, TableCorpusStructureTests.GeneratedBody());
        using var file = DocxFile.Open(path);
        var layout = await SectionOracleTests.LaidAsync(file.Model);
        var steps = new ObjectSteps(file.Model, layout, new LayoutOptions());

        var compared = await steps.RunAsync(new Random(unchecked(seed * 104729)), 60, "formatting " + seed, s_formatKinds);

        Assert.True(compared > 30, $"only {compared} of 60 steps changed something");
        TestContext.Current.SendDiagnosticMessage($"formatting {seed}: {compared} steps compared, {steps.GridsCompared} table pieces, refused {steps.RefusedPerKind.Sum()}");
    }

    // A guard that only the per-seed runs carry would stay green if a kind never changed anything.
    [Fact]
    public async Task EveryTableFormattingKindChangedSomethingOnTheGeneratedTables()
    {
        var ran = new int[ObjectSteps.Kinds];
        var refused = new int[ObjectSteps.Kinds];
        long widths = 0;
        for (var seed = 1; seed <= 6; seed++)
        {
            using var folder = new TempFolder();
            var path = folder.File("tables.docx");
            DocxBuilder.WithBody(path, TableCorpusStructureTests.GeneratedBody());
            using var file = DocxFile.Open(path);
            var layout = await SectionOracleTests.LaidAsync(file.Model);
            var steps = new ObjectSteps(file.Model, layout, new LayoutOptions());
            await steps.RunAsync(new Random(unchecked(seed * 104729)), 60, "formatting " + seed, s_formatKinds);
            for (var k = 0; k < ran.Length; k++)
            {
                ran[k] += steps.RanPerKind[k];
                refused[k] += steps.RefusedPerKind[k];
            }
            widths += steps.WidthsCompared;
        }

        var report = string.Join(", ", Enum.GetValues<ObjectKind>().Select(k => $"{k} ran {ran[(int)k]} refused {refused[(int)k]}"));
        TestContext.Current.SendDiagnosticMessage("generated formatting steps: " + report);
        Assert.True(widths > 100, $"only {widths} changed tables had their widths checked for agreement ({report})");
        foreach (var kind in s_formatKinds.Distinct().Where(k => k is not (ObjectKind.Undo or ObjectKind.Redo)))
        {
            Assert.True(ran[(int)kind] > 0, $"the step kind {kind} never changed anything on the generated tables ({report})");
        }

        // A formatting step is refused only for an irregular or inconsistent table (a merge in the pool can leave none of those), not nearly always.
        foreach (var kind in new[] { ObjectKind.SetBorders, ObjectKind.SetShading, ObjectKind.SetColumnWidths, ObjectKind.SetTableWidth, ObjectKind.SetRowHeight, ObjectKind.DistributeColumns })
        {
            Assert.True(refused[(int)kind] <= ran[(int)kind], $"{kind} was refused {refused[(int)kind]} times and changed something {ran[(int)kind]} times ({report})");
        }
    }


    private const string DrawingNamespaces = "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"";

    private static string DrawingGraphic(string crop, string rotation) =>
        "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic><pic:nvPicPr><pic:cNvPr id=\"0\" name=\"x\"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed=\"rId9\"/>" + crop + "</pic:blipFill>"
        + $"<pic:spPr><a:xfrm{rotation}><a:off x=\"0\" y=\"0\"/><a:ext cx=\"1905000\" cy=\"1270000\"/></a:xfrm><a:prstGeom prst=\"rect\"/></pic:spPr></pic:pic></a:graphicData></a:graphic>";

    private static string InlineDrawing(int id, string crop = "", string rotation = "") =>
        $"<w:r><w:drawing {DrawingNamespaces}><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"1905000\" cy=\"1270000\"/><wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/><wp:docPr id=\"{id}\" name=\"Picture {id}\"/><wp:cNvGraphicFramePr/>{DrawingGraphic(crop, rotation)}</wp:inline></w:drawing></w:r>";

    private static string AnchoredDrawing(int id, int height, string crop = "") =>
        $"<w:r><w:drawing {DrawingNamespaces}><wp:anchor distT=\"0\" distB=\"0\" distL=\"114300\" distR=\"114300\" simplePos=\"0\" relativeHeight=\"{height}\" behindDoc=\"0\" locked=\"0\" layoutInCell=\"1\" allowOverlap=\"1\"><wp:simplePos x=\"0\" y=\"0\"/><wp:positionH relativeFrom=\"column\"><wp:posOffset>300000</wp:posOffset></wp:positionH><wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>100000</wp:posOffset></wp:positionV><wp:extent cx=\"1905000\" cy=\"1270000\"/><wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/><wp:wrapSquare wrapText=\"bothSides\"/><wp:docPr id=\"{id}\" name=\"Picture {id}\"/><wp:cNvGraphicFramePr/>{DrawingGraphic(crop, string.Empty)}</wp:anchor></w:drawing></w:r>";

    // Text paragraphs, cropped inline and anchored pictures, a turned one, and a table with a picture in a cell.
    internal static string PictureBody()
    {
        const string Crop = "<a:srcRect l=\"10000\" t=\"5000\" r=\"20000\" b=\"0\"/>";
        static string Text(int n) => $"<w:p><w:r><w:t>Paragraph {n} with a few words to wrap around a picture when one is put beside it.</w:t></w:r></w:p>";
        var body = new System.Text.StringBuilder();
        for (var i = 0; i < 8; i++)
        {
            body.Append(Text(i));
        }

        body.Append(System.Globalization.CultureInfo.InvariantCulture, $"<w:p>{InlineDrawing(3, Crop)}<w:r><w:t> after a cropped picture</w:t></w:r></w:p>");
        body.Append(System.Globalization.CultureInfo.InvariantCulture, $"<w:p><w:r><w:t>An anchored one: </w:t></w:r>{AnchoredDrawing(4, 100, Crop)}<w:r><w:t>text beside it</w:t></w:r></w:p>");
        body.Append(System.Globalization.CultureInfo.InvariantCulture, $"<w:p>{InlineDrawing(5, string.Empty, " rot=\"5400000\"")}</w:p>");
        body.Append("<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid><w:gridCol w:w=\"4000\"/><w:gridCol w:w=\"4000\"/></w:tblGrid>");
        body.Append(System.Globalization.CultureInfo.InvariantCulture, $"<w:tr><w:tc><w:tcPr><w:tcW w:w=\"4000\" w:type=\"dxa\"/></w:tcPr><w:p>{InlineDrawing(6)}</w:p></w:tc><w:tc><w:tcPr><w:tcW w:w=\"4000\" w:type=\"dxa\"/></w:tcPr><w:p><w:r><w:t>cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>");
        for (var i = 8; i < 30; i++)
        {
            body.Append(Text(i));
        }

        return body.ToString();
    }

    private static readonly ObjectKind[] s_pictureKinds =
    [
        ObjectKind.InsertPicture, ObjectKind.Wrap, ObjectKind.Resize, ObjectKind.Nudge, ObjectKind.Move, ObjectKind.Wrap, ObjectKind.Nudge, ObjectKind.Resize, ObjectKind.DeletePicture,
        ObjectKind.InsertPicture, ObjectKind.Wrap, ObjectKind.Move, ObjectKind.Move, ObjectKind.Nudge, ObjectKind.Undo, ObjectKind.Redo, ObjectKind.Wrap, ObjectKind.PictureProperties, ObjectKind.PictureProperties, ObjectKind.PastePicture, ObjectKind.PastePicture,
    ];

    // Cropped, anchored, turned and tabled pictures: picture steps only, each compared with a full layout and with the object map it publishes.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task PictureStepsEqualAFullLayoutOnGeneratedDocumentsWithCroppedAnchoredAndTurnedPictures(int seed)
    {
        using var folder = new TempFolder();
        var path = folder.File("pictures.docx");
        DocxBuilder.WithBody(path, PictureBody());
        using var file = DocxFile.Open(path);
        var layout = await SectionOracleTests.LaidAsync(file.Model);
        var steps = new ObjectSteps(file.Model, layout, new LayoutOptions());

        var compared = await steps.RunAsync(new Random(unchecked(seed * 15485863)), 50, "pictures " + seed, s_pictureKinds);

        Assert.True(compared > 25, $"only {compared} of 50 steps changed something");
        TestContext.Current.SendDiagnosticMessage($"pictures {seed}: {compared} steps compared, refused {steps.RefusedPerKind.Sum()}");
    }

    // A guard that only the per-seed runs carry would stay green if a kind never changed anything.
    [Fact]
    public async Task EveryPictureStepKindChangedSomethingOnTheGeneratedDocuments()
    {
        var ran = new int[ObjectSteps.Kinds];
        var refused = new int[ObjectSteps.Kinds];
        for (var seed = 1; seed <= 6; seed++)
        {
            using var folder = new TempFolder();
            var path = folder.File("pictures.docx");
            DocxBuilder.WithBody(path, PictureBody());
            using var file = DocxFile.Open(path);
            var layout = await SectionOracleTests.LaidAsync(file.Model);
            var steps = new ObjectSteps(file.Model, layout, new LayoutOptions());
            await steps.RunAsync(new Random(unchecked(seed * 15485863)), 50, "pictures " + seed, s_pictureKinds);
            for (var k = 0; k < ran.Length; k++)
            {
                ran[k] += steps.RanPerKind[k];
                refused[k] += steps.RefusedPerKind[k];
            }
        }

        var report = string.Join(", ", Enum.GetValues<ObjectKind>().Select(k => $"{k} ran {ran[(int)k]} refused {refused[(int)k]}"));
        TestContext.Current.SendDiagnosticMessage("generated picture steps: " + report);
        foreach (var kind in s_pictureKinds.Distinct())
        {
            Assert.True(ran[(int)kind] > 0, $"the step kind {kind} never changed anything on the generated pictures ({report})");
        }

        // A picture step is refused for a good reason now and then (a picture in a cell cannot float, a turned one is not resized), but not nearly always.
        foreach (var kind in new[] { ObjectKind.Resize, ObjectKind.Wrap, ObjectKind.Move, ObjectKind.Nudge, ObjectKind.PictureProperties })
        {
            Assert.True(refused[(int)kind] <= ran[(int)kind] * 2, $"{kind} was refused {refused[(int)kind]} times and changed something {ran[(int)kind]} times ({report})");
        }
    }

    // The history drops the undone insert when a different edit follows: its entry leaves the store and the layout still equals a full layout at every step.
    [Fact]
    public async Task AnInsertedPictureThatIsUndoneAndForgottenLeavesNoEntryInTheStore()
    {
        using var folder = new TempFolder();
        var path = folder.File("pictures.docx");
        DocxBuilder.WithBody(path, PictureBody());
        using var file = DocxFile.Open(path);
        var layout = await SectionOracleTests.LaidAsync(file.Model);
        var steps = new ObjectSteps(file.Model, layout, new LayoutOptions());
        var random = new Random(7);

        Assert.True(await steps.RunKindAsync(ObjectKind.InsertPicture, random, "prune insert"));
        Assert.Single(file.Model.Media.Entries);
        Assert.True(await steps.RunKindAsync(ObjectKind.Undo, random, "prune undo"));
        Assert.Single(file.Model.Media.Entries);
        Assert.True(await steps.RunKindAsync(ObjectKind.InsertFootnote, random, "prune other edit"));

        Assert.Empty(file.Model.Media.Entries);
        Assert.Equal(0, file.Model.Media.TotalBytes);
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
