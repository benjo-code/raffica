using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Editing;

/// <summary>
/// Makes the files of the owner's Word check for milestone 4.5a (<c>Docs/write-word-check.md</c>, section "Tables, pictures and notes (milestone 4.5a)"): every operation goes through the editor as the app calls
/// it and the file is saved through <see cref="DocxFile.SaveAsync(string, CancellationToken)"/>; <c>pwsh tools/word-open-check.ps1 &lt;files&gt;</c> then asks Word whether it opens each without repair. It covers the
/// notes (<c>footnote.docx</c>, <c>endnote.docx</c> and <c>notes-in-table.docx</c>), the tables (<c>table-*.docx</c>) and the pictures (<c>picture-*.docx</c>, in <c>WordCheckObjectFilesTests.Pictures.cs</c>). Each file has a
/// <c>.facts.txt</c> of what the model holds (the counts of tables, notes and pictures, one line per picture with its kind, size and description, and the row, cell and column counts of every table) for the comparison with
/// Word's own (<c>pwsh tools/word-object-counts.ps1 &lt;file&gt;</c>). The tests write nothing and skip unless the folder is named by <c>RAFFICA_WORD_CHECK_DIR</c>.
/// </summary>
public sealed partial class WordCheckObjectFilesTests
{
    private const string FolderVariable = "RAFFICA_WORD_CHECK_DIR";

    private static string Folder()
    {
        var folder = Environment.GetEnvironmentVariable(FolderVariable);
        Assert.SkipUnless(!string.IsNullOrWhiteSpace(folder), "Set RAFFICA_WORD_CHECK_DIR to a folder to make the Word-check files.");

        Directory.CreateDirectory(folder!);
        return folder!;
    }

    private static TextEditor EditorOf(DocxFile file) =>
        new(file.Model, (_, _) => ProjectionHost.Plain, format: new Raffica.Write.Layout.LayoutEffectiveFormat(file.Model));

    private static TextRange Caret(TextPosition at) => new(at, at);

    private static Paragraph FirstParagraph(DocxFile file) => file.Model.Body.Blocks.OfType<Paragraph>().First();

    // Types a text at the end of the paragraph and returns the caret behind it.
    private static TextPosition Type(TextEditor editor, Paragraph paragraph, string text)
    {
        var end = new TextPosition(StoryRef.Body, paragraph, ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text.Length);
        var result = editor.InsertText(Caret(end), text);
        return result.Caret;
    }

    // A note at the caret with a line of text typed into it, as the app does (the caret lands in the note).
    private static void NoteWithText(TextEditor editor, TextPosition at, NoteType kind, string text)
    {
        var result = editor.InsertNote(at, kind);
        Assert.NotNull(result);
        var inNote = result.Caret;
        Assert.Equal(StoryKind.Note, inNote.Story.Kind);
        editor.InsertText(Caret(inNote), text);
    }

    [Fact]
    public async Task AFootnoteIsSavedForWord()
    {
        var folder = Folder();
        using var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4));
        var editor = EditorOf(file);
        var paragraph = FirstParagraph(file);
        var at = Type(editor, paragraph, "A sentence that carries a footnote.");
        NoteWithText(editor, at with { Offset = at.Offset - 1 }, NoteType.Footnote, "The footnote says where the sentence comes from.");

        await SaveWithFactsAsync(file, folder, "footnote.docx");
    }

    [Fact]
    public async Task AnEndnoteIsSavedForWord()
    {
        var folder = Folder();
        using var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4));
        var editor = EditorOf(file);
        var paragraph = FirstParagraph(file);
        var at = Type(editor, paragraph, "A sentence that carries an endnote.");
        NoteWithText(editor, at with { Offset = at.Offset - 1 }, NoteType.Endnote, "The endnote collects its sources at the end.");

        await SaveWithFactsAsync(file, folder, "endnote.docx");
    }

    [Fact]
    public async Task NotesInATableAndInTheBodyAreSavedForWord()
    {
        var folder = Folder();
        using var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4));
        var editor = EditorOf(file);
        var body = file.Model.Body.Blocks;
        var closing = FirstParagraph(file);

        // A table of one row and two cells before the closing paragraph (a body must not end in a table), each cell with a paragraph.
        var table = new Table();
        table.GridColumns.Add(4500);
        table.GridColumns.Add(4500);
        var row = new TableRow();
        var cells = new[] { new TableCell(), new TableCell() };
        var paragraphs = new[] { new Paragraph(), new Paragraph() };
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i].Blocks.Add(paragraphs[i]);
            row.Items.Add(cells[i]);
        }

        table.Rows.Add(row);
        body.Insert(body.IndexOf(closing), table);

        var first = editor.InsertText(Caret(new TextPosition(StoryRef.Body, paragraphs[0], 0)), "Cell with a footnote.");
        var second = editor.InsertText(Caret(new TextPosition(StoryRef.Body, paragraphs[1], 0)), "Cell with an endnote.");
        NoteWithText(editor, first.Caret with { Offset = first.Caret.Offset - 1 }, NoteType.Footnote, "A footnote of a cell.");
        NoteWithText(editor, second.Caret with { Offset = second.Caret.Offset - 1 }, NoteType.Endnote, "An endnote of a cell.");

        var tail = Type(editor, closing, "Text behind the table with a footnote.");
        NoteWithText(editor, tail with { Offset = tail.Offset - 1 }, NoteType.Footnote, "A footnote of the body.");

        await SaveWithFactsAsync(file, folder, "notes-in-table.docx");
    }

    // ---- tables ---------------------------------------------------------------------------------------------------------------------

    private static readonly uint[] s_colors = [0xFFC00000, 0xFF00B050, 0xFF0070C0, 0xFFFFC000, 0xFF7030A0, 0xFF92D050];

    // The operation must have changed something: a refusal is a finding of this check, not a skipped step.
    private static EditResult Did(EditResult? result, string what)
    {
        Assert.True(result is not null, "the editor refused: " + what);
        return result!;
    }

    private static Table TableOf(DocxFile file, TableRef reference) => Assert.IsType<Table>(reference.Find(file.Model.Body.Blocks));

    private static TableGridMap Map(DocxFile file, TableRef reference) => TableGridMap.Of(TableOf(file, reference));

    // The first paragraph of the cell that starts at a row and a grid column, as a caret position (the app passes the caret of the selection).
    private static TextPosition In(DocxFile file, TableRef reference, int row, int column)
    {
        var cell = Map(file, reference).At(row, column)!.Cell;
        return new TextPosition(StoryRef.Body, cell.Blocks.OfType<Paragraph>().First(), 0);
    }

    private static void Fill(TextEditor editor, DocxFile file, TableRef reference, int row, int column, string text) =>
        Did(editor.InsertText(Caret(In(file, reference, row, column)), text), $"typing into cell {row},{column}");

    private static void FillAll(TextEditor editor, DocxFile file, TableRef reference, string header = "Head")
    {
        var map = Map(file, reference);
        for (var r = 0; r < map.RowCount; r++)
        {
            for (var c = 0; c < map.Columns; c++)
            {
                Fill(editor, file, reference, r, c, r == 0 ? $"{header} {c + 1}" : $"Cell {r},{c}");
            }
        }
    }

    // The table the editor made at a caret, found as the app finds it (the caret of the result lies in its first cell).
    private static TableRef Inserted(TextEditor editor, EditResult result) => Assert.IsType<TableCellPlace>(editor.TableOf(result.Caret)).Table;

    private static CellRange Cells(TableRef table, int row, int column, int lastRow, int lastColumn) => CellRange.Between(table, row, column, lastRow, lastColumn);

    private static CellRange Whole(DocxFile file, TableRef table)
    {
        var map = Map(file, table);
        return Cells(table, 0, 0, map.Cells.Max(c => c.RowIndex), map.Columns - 1);
    }

    // The widths the layout draws for the columns of a table now, in twips (the app reads the same column borders of the object map).
    private static async Task<int[]> DrawnAsync(DocxFile file, TableRef table)
    {
        var layout = await Layout.SectionOracleTests.LaidAsync(file.Model);
        var drawn = DrawnTableWidths.Of(layout, table, Map(file, table).Columns);
        Assert.NotNull(drawn);
        return drawn!;
    }

    private static string FactsLine(TableGridMap map) => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"rows={map.RowCount} cells={map.Cells.Count(c => !c.IsContinuation)} tcs={map.Cells.Count} columns={map.Columns}");

    // Saves the file and writes what the model holds next to it, for the comparison with Word's counts: the object facts (counts of tables, notes and pictures, one line per picture) and, for every top-level table (or the lines the caller
    // names), grid rows, cells (a merged cell counts once, as in Word's own count; <c>tcs</c> counts every <c>w:tc</c>, a continuation of a vertical merge included) and grid columns. A table the editor made must be a regular and consistent grid.
    private static async Task SaveWithFactsAsync(DocxFile file, string folder, string name, IReadOnlyList<string>? tableLines = null)
    {
        var lines = ObjectFacts(file);
        if (tableLines is null)
        {
            foreach (var table in file.Model.Body.Blocks.OfType<Table>())
            {
                var map = TableGridMap.Of(table);
                Assert.True(map.IsRegular && map.IsConsistent, $"{name}: a table the editor made is not a regular and consistent grid");
                lines.Add(FactsLine(map));
            }
        }
        else
        {
            lines.AddRange(tableLines);
        }

        await file.SaveAsync(Path.Combine(folder, name), TestContext.Current.CancellationToken);
        await File.WriteAllLinesAsync(Path.Combine(folder, name + ".facts.txt"), lines, TestContext.Current.CancellationToken);
    }

    private static DocxFile BlankWithTable(out TextEditor editor, out TableRef table, int columns, int rows)
    {
        var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4));
        editor = EditorOf(file);
        var at = Type(editor, FirstParagraph(file), "Text before the table.");
        var result = Did(editor.InsertTable(at, columns, rows), $"InsertTable {columns}x{rows}");
        table = Inserted(editor, result);
        return file;
    }

    [Fact]
    public async Task ABasicTableIsSavedForWord()
    {
        var folder = Folder();
        using var file = BlankWithTable(out var editor, out var table, 4, 3);
        FillAll(editor, file, table);
        Type(editor, file.Model.Body.Blocks.OfType<Paragraph>().Last(), "A paragraph kept behind the table.");

        await SaveWithFactsAsync(file, folder, "table-basic.docx");
    }

    [Fact]
    public async Task MergedCellsAreSavedForWord()
    {
        var folder = Folder();
        using var file = BlankWithTable(out var editor, out var table, 5, 6);
        FillAll(editor, file, table);

        // A horizontal merge, a vertical merge and a merge in both directions that share no cell.
        Did(editor.MergeCells(Cells(table, 0, 1, 0, 3)), "merge across");
        Did(editor.MergeCells(Cells(table, 1, 0, 3, 0)), "merge down");
        Did(editor.MergeCells(Cells(table, 2, 2, 3, 3)), "merge both ways");

        Did(editor.AddRow(table, 2, after: false), "add a row inside the vertical merge");
        Did(editor.AddColumn(table, 2, after: true), "add a column inside the horizontal span");
        var map = Map(file, table);
        Did(editor.DeleteRows(Cells(table, 4, 0, 5, map.Columns - 1)), "delete rows across merges");
        var lastRow = Map(file, table).Cells.Max(c => c.RowIndex);
        Did(editor.DeleteColumns(Cells(table, 0, 3, lastRow, 3)), "delete a column through a span");

        var heads = Map(file, table).Cells.Where(c => !c.IsContinuation).ToList();
        var head = heads.FirstOrDefault(c => c.RowSpan > 1 && c.ColumnSpan > 1) ?? heads.First(c => c.RowSpan > 1 || c.ColumnSpan > 1);
        Did(editor.SplitCell(table, head.RowIndex, head.Column), "split a merged cell");

        await SaveWithFactsAsync(file, folder, "table-merged.docx");
    }

    [Fact]
    public async Task BordersShadingHeaderRowAndRowHeightsAreSavedForWord()
    {
        var folder = Folder();
        using var file = BlankWithTable(out var editor, out var table, 4, 5);
        FillAll(editor, file, table);

        var caret = In(file, table, 0, 0);
        Did(editor.SetBorders(Whole(file, table), BorderSpec.All(), caret), "borders all");
        Did(editor.SetBorders(Cells(table, 0, 0, 1, 1), BorderSpec.Box(), caret), "borders box");
        Did(editor.SetBorders(Cells(table, 2, 2, 3, 3), BorderSpec.Outside(TableBorderStyle.Solid, 12, s_colors[0]), caret), "borders outside");
        Did(editor.SetBorders(Cells(table, 0, 2, 0, 3), BorderSpec.None(), caret), "borders none");

        Did(editor.SetBorders(Cells(table, 1, 0, 1, 3), BorderSpec.Sides(BorderSides.Top | BorderSides.Bottom, TableBorderStyle.DoubleLine, 12, s_colors[2]), caret), "double");
        Did(editor.SetBorders(Cells(table, 2, 0, 2, 1), BorderSpec.Sides(BorderSides.Left | BorderSides.Right, TableBorderStyle.Dashed, 8, s_colors[1]), caret), "dashed");
        Did(editor.SetBorders(Cells(table, 3, 0, 4, 1), BorderSpec.Sides(BorderSides.InsideHorizontal | BorderSides.Bottom, TableBorderStyle.Dotted, 16, s_colors[4]), caret), "dotted");
        Did(editor.SetBorders(Cells(table, 4, 2, 4, 3), BorderSpec.Off(BorderSides.Top | BorderSides.Left), caret), "sides off");

        for (var i = 0; i < s_colors.Length; i++)
        {
            Did(editor.SetShading(Cells(table, i % 5, i % 4, i % 5, i % 4), s_colors[i], caret), $"shading {i}");
        }

        Did(editor.SetShading(Cells(table, 0, 0, 0, 3), s_colors[3], caret), "shading the header row");
        Did(editor.SetShading(Cells(table, 0, 0, 0, 0), null, caret), "shading removed");
        Did(editor.SetHeaderRow(table, true, caret), "header row");
        Did(editor.SetRowHeight(table, 1, 800, exact: false, caret), "row height at least");
        Did(editor.SetRowHeight(table, 2, 600, exact: true, caret), "row height exactly");

        await SaveWithFactsAsync(file, folder, "table-borders.docx");
    }

    [Fact]
    public async Task ColumnAndTableWidthsAreSavedForWord()
    {
        var folder = Folder();
        using var temp = new TempFolder();

        // A percentage table and an autofit table, the two kinds the first width write turns into fixed ones.
        static string Cell(string text, string width) => $"<w:tc><w:tcPr>{width}</w:tcPr><w:p><w:r><w:t>{text}</w:t></w:r></w:p></w:tc>";
        static string Grid(params int[] w) => "<w:tblGrid>" + string.Concat(w.Select(x => $"<w:gridCol w:w=\"{x}\"/>")) + "</w:tblGrid>";
        var percentage = $"<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"pct\"/></w:tblPr>{Grid(2800, 2800, 3600)}"
            + string.Concat(Enumerable.Range(0, 3).Select(r => "<w:tr>" + Cell($"p{r}0", "<w:tcW w:w=\"1500\" w:type=\"pct\"/>") + Cell($"p{r}1", "<w:tcW w:w=\"1500\" w:type=\"pct\"/>") + Cell($"p{r}2", "<w:tcW w:w=\"2000\" w:type=\"pct\"/>") + "</w:tr>"))
            + "</w:tbl>";
        var autofit = $"<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr>{Grid(1500, 4500, 3000)}"
            + string.Concat(Enumerable.Range(0, 4).Select(r => "<w:tr>" + Cell($"a{r}0", "<w:tcW w:w=\"1500\" w:type=\"dxa\"/>") + Cell($"a{r}1 with a longer text", "<w:tcW w:w=\"4500\" w:type=\"dxa\"/>") + Cell($"a{r}2", "<w:tcW w:w=\"3000\" w:type=\"dxa\"/>") + "</w:tr>"))
            + "</w:tbl>";
        const string Between = "<w:p><w:r><w:t>Between the tables.</w:t></w:r></w:p>";
        var styles = "<w:styles xmlns:w=\"" + XmlSnippets.W + "\"><w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/></w:style></w:styles>";
        var numbering = "<w:numbering xmlns:w=\"" + XmlSnippets.W + "\"/>";
        var path = temp.File("source.docx");
        DocxBuilder.WithStylesAndNumbering(path, percentage + Between + autofit + Between, styles, numbering);
        using var file = DocxFile.Open(path);
        var editor = EditorOf(file);
        var first = new TableRef(StoryRef.Body, 0);
        var second = new TableRef(StoryRef.Body, 2);

        // The first table: the columns written from the drawn widths with two of them changed; then its second row moves up.
        var drawn = await DrawnAsync(file, first);
        Did(editor.SetColumnWidths(first, [drawn[0] + 400, drawn[1] - 400, drawn[2]], In(file, first, 0, 0)), "SetColumnWidths");
        Did(editor.MoveRow(first, 1, -1), "MoveRow up");

        // The second table: its width set with the drawn widths, the columns distributed from the widths drawn now, a row moved down and a width written again.
        var drawnSecond = await DrawnAsync(file, second);
        Did(editor.SetTableWidth(second, 7000, drawnSecond, In(file, second, 0, 0)), "SetTableWidth");
        var drawnAfter = await DrawnAsync(file, second);
        Did(editor.DistributeColumns(second, drawnAfter, In(file, second, 0, 0)), "DistributeColumns");
        Did(editor.MoveRow(second, 1, 1), "MoveRow down");
        var drawnLast = await DrawnAsync(file, second);
        Did(editor.SetColumnWidths(second, [drawnLast[0], drawnLast[1] + 300, drawnLast[2] - 300], In(file, second, 0, 0)), "SetColumnWidths again");

        await SaveWithFactsAsync(file, folder, "table-widths.docx");
    }

    [Fact]
    public async Task ALongTableWithARepeatedHeaderRowIsSavedForWord()
    {
        var folder = Folder();
        using var file = BlankWithTable(out var editor, out var table, 3, 120);
        FillAll(editor, file, table, header: "Repeated heading");
        Did(editor.SetHeaderRow(table, true, In(file, table, 0, 0)), "header row");
        Type(editor, file.Model.Body.Blocks.OfType<Paragraph>().Last(), "The paragraph behind the long table.");

        // The header must repeat: the second page of the layout shows the heading text again.
        var layout = await Layout.SectionOracleTests.LaidAsync(file.Model);
        Assert.True(layout.LaidOutPages >= 3, $"the long table should run over several pages, it has {layout.LaidOutPages}");
        Assert.Contains("Repeated heading 1", layout.Page(1).Text, StringComparison.Ordinal);

        await SaveWithFactsAsync(file, folder, "table-header-multipage.docx");
    }

    [Fact]
    public async Task ATableInABlockContentControlIsSavedForWord()
    {
        var folder = Folder();
        using var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4));
        var editor = EditorOf(file);
        var body = file.Model.Body.Blocks;
        var closing = FirstParagraph(file);
        var one = new Paragraph();
        var two = new Paragraph();
        var control = new ContentControlBlock();
        control.Content.Add(one);
        control.Content.Add(two);
        body.Insert(body.IndexOf(closing), control);

        var typed = Did(editor.InsertText(Caret(new TextPosition(StoryRef.Body, one, 0)), "First paragraph of the control."), "typing in the control");
        Did(editor.InsertText(Caret(new TextPosition(StoryRef.Body, two, 0)), "Second paragraph of the control."), "typing in the control");
        var table = Inserted(editor, Did(editor.InsertTable(typed.Caret, 3, 3), "InsertTable in a block content control"));
        FillAll(editor, file, table);
        Type(editor, closing, "Text behind the control.");

        // The table is inside the control, so the top-level facts hold no table: the facts of the one table are named by hand.
        await SaveWithFactsAsync(file, folder, "table-in-content-control.docx", [FactsLine(Map(file, table))]);
    }

    [Fact]
    public async Task ATableAfterANoteAndNotesInsideATableAreSavedForWord()
    {
        var folder = Folder();
        using var file = DocxFile.Open(BlankDocument.Create(PaperSize.A4));
        var editor = EditorOf(file);
        var lead = FirstParagraph(file);
        var at = Type(editor, lead, "A sentence with a footnote before the table.");
        NoteWithText(editor, at with { Offset = at.Offset - 1 }, NoteType.Footnote, "A footnote of the paragraph before the table.");

        // The table goes behind the paragraph that carries the note (the caret at its end).
        var end = new TextPosition(StoryRef.Body, lead, ParagraphProjection.Build(lead, ProjectionHost.Plain).Text.Length);
        var table = Inserted(editor, Did(editor.InsertTable(end, 3, 3), "InsertTable behind a footnote paragraph"));
        FillAll(editor, file, table);

        // A footnote and an endnote inside cells of the table.
        var typed = Did(editor.InsertText(Caret(In(file, table, 1, 1)), " with a note"), "typing in a cell");
        NoteWithText(editor, typed.Caret with { Offset = typed.Caret.Offset - 1 }, NoteType.Footnote, "A footnote of a cell of the table.");
        var typedOther = Did(editor.InsertText(Caret(In(file, table, 2, 2)), " with an endnote"), "typing in a second cell");
        NoteWithText(editor, typedOther.Caret with { Offset = typedOther.Caret.Offset - 1 }, NoteType.Endnote, "An endnote of a cell of the table.");

        Type(editor, file.Model.Body.Blocks.OfType<Paragraph>().Last(), "Closing paragraph behind the table.");

        await SaveWithFactsAsync(file, folder, "table-after-notes.docx");
    }
}
