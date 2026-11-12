using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace OpenXmlAot;

public static class Xlsx
{
    sealed class Strings
    {
        public readonly List<string> Items = new();
        readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);
        public int Of(string s)
        {
            if (_index.TryGetValue(s, out int i)) return i;
            _index[s] = Items.Count;
            Items.Add(s);
            return Items.Count - 1;
        }
    }

    static string Col(int c) => ((char)('A' + c)).ToString();

    static Cell Text(string reference, string value, Strings ss, uint style = 0) =>
        new() { CellReference = reference, DataType = CellValues.SharedString, StyleIndex = style, CellValue = new CellValue(ss.Of(value).ToString()) };

    static Cell Number(string reference, double value, uint style = 0) =>
        new() { CellReference = reference, StyleIndex = style, CellValue = new CellValue(value.ToString(System.Globalization.CultureInfo.InvariantCulture)) };

    static Stylesheet MakeStyles() => new(
        new NumberingFormats(new NumberingFormat { NumberFormatId = 164, FormatCode = "#,##0.00\" EUR\"" }) { Count = 1 },
        new Fonts(
            new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" })) { Count = 2 },
        new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FFFFF2CC" }) { PatternType = PatternValues.Solid })) { Count = 3 },
        new Borders(new Border(new LeftBorder(), new RightBorder(), new TopBorder(), new BottomBorder(), new DiagonalBorder())) { Count = 1 },
        new CellFormats(
            new CellFormat(),                                                                                  // 0 default
            new CellFormat { NumberFormatId = 4, ApplyNumberFormat = true },                                   // 1 #,##0.00
            new CellFormat { NumberFormatId = 164, ApplyNumberFormat = true },                                 // 2 custom EUR
            new CellFormat { FontId = 1, FillId = 2, ApplyFont = true, ApplyFill = true }) { Count = 4 });     // 3 bold, filled

    static WorkbookPart NewWorkbook(SpreadsheetDocument doc, out Strings ss)
    {
        var wb = doc.AddWorkbookPart();
        wb.Workbook = new Workbook();
        wb.AddNewPart<WorkbookStylesPart>("rIdStyles").Stylesheet = MakeStyles();
        ss = new Strings();
        return wb;
    }

    static void AddSheet(WorkbookPart wb, string name, uint id, SheetData data, params OpenXmlElement[] after)
    {
        var part = wb.AddNewPart<WorksheetPart>("rIdSheet" + id);
        part.Worksheet = new Worksheet(data);
        foreach (var e in after) part.Worksheet!.Append(e);
        wb.Workbook!.Sheets ??= new Sheets();
        wb.Workbook.Sheets!.Append(new Sheet { Name = name, SheetId = id, Id = "rIdSheet" + id });
    }

    static void Finish(WorkbookPart wb, Strings ss)
    {
        var sst = wb.AddNewPart<SharedStringTablePart>("rIdStrings");
        sst.SharedStringTable = new SharedStringTable(ss.Items.Select(s => new SharedStringItem(new Text(s))).Cast<OpenXmlElement>().ToArray())
        { Count = (uint)ss.Items.Count, UniqueCount = (uint)ss.Items.Count };
        wb.Workbook!.Append(new CalculationProperties { CalculationId = 191029, FullCalculationOnLoad = true });
    }

    /// <summary>A plain sheet (1,000 rows by 10 columns of numbers and text) and a workbook with formulas, formats, a merged cell and conditional formatting.</summary>
    public static void Make(string plainPath, string formulasPath)
    {
        using (var doc = SpreadsheetDocument.Create(plainPath, SpreadsheetDocumentType.Workbook))
        {
            var wb = NewWorkbook(doc, out var ss);
            var data = new SheetData();
            var rng = new Random(4242);
            for (int r = 1; r <= 1000; r++)
            {
                var row = new Row { RowIndex = (uint)r };
                row.Append(Text("A" + r, "item " + r, ss));
                for (int c = 1; c < 10; c++)
                    row.Append(c % 3 == 0 ? Text(Col(c) + r, "label " + rng.Next(50), ss) : Number(Col(c) + r, Math.Round(rng.NextDouble() * 1000, 2)));
                data.Append(row);
            }
            AddSheet(wb, "Plain", 1, data);
            Finish(wb, ss);
        }

        using (var doc = SpreadsheetDocument.Create(formulasPath, SpreadsheetDocumentType.Workbook))
        {
            var wb = NewWorkbook(doc, out var ss);
            // Sheet 1 "Data": header, 20 keyed rows.
            var data = new SheetData();
            var header = new Row { RowIndex = 1 };
            header.Append(Text("A1", "key", ss, 3), Text("B1", "value", ss, 3));
            data.Append(header);
            double sum = 0, value5 = 0, b2 = 0;
            for (int i = 1; i <= 20; i++)
            {
                double v = i * 10.5;
                sum += v;
                if (i == 5) value5 = v;
                if (i == 1) b2 = v;
                var row = new Row { RowIndex = (uint)(i + 1) };
                row.Append(Text("A" + (i + 1), "key" + i, ss), Number("B" + (i + 1), v, 1));
                data.Append(row);
            }
            var scale = new ConditionalFormatting(new ConditionalFormattingRule(
                new ColorScale(
                    new ConditionalFormatValueObject { Type = ConditionalFormatValueObjectValues.Min },
                    new ConditionalFormatValueObject { Type = ConditionalFormatValueObjectValues.Max },
                    new Color { Rgb = "FFFFFFFF" }, new Color { Rgb = "FF63BE7B" }))
            { Type = ConditionalFormatValues.ColorScale, Priority = 1 })
            { SequenceOfReferences = new ListValue<StringValue> { InnerText = "B2:B21" } };
            AddSheet(wb, "Data", 1, data, scale);

            // Sheet 2 "Calc": SUM, VLOOKUP, a cross-sheet reference, a merged title.
            var calc = new SheetData();
            var r1 = new Row { RowIndex = 1 };
            r1.Append(Text("A1", "Merged title", ss, 3));
            calc.Append(r1);
            Row Formula(uint idx, string label, string f, double cached, uint style)
            {
                var row = new Row { RowIndex = idx };
                row.Append(Text("A" + idx, label, ss),
                    new Cell { CellReference = "B" + idx, StyleIndex = style, CellFormula = new CellFormula(f), CellValue = new CellValue(cached.ToString(System.Globalization.CultureInfo.InvariantCulture)) });
                return row;
            }
            calc.Append(Formula(2, "Sum", "SUM(Data!B2:B21)", sum, 2));
            calc.Append(Formula(3, "Lookup", "VLOOKUP(\"key5\",Data!A2:B21,2,FALSE)", value5, 1));
            calc.Append(Formula(4, "Cross-sheet", "Data!B2*2", b2 * 2, 2));
            AddSheet(wb, "Calc", 2, calc, new MergeCells(new MergeCell { Reference = "A1:D1" }) { Count = 1 });
            Finish(wb, ss);
        }
    }

    /// <summary>
    /// Copies the workbook and sets cell B2 of the first sheet to 42. Touched part: the first worksheet only.
    /// The first sheet is found through the workbook (which loads and so re-serialises /xl/workbook.xml) or, with
    /// <paramref name="withoutWorkbook"/>, as the first worksheet part of the package (workbook.xml is never loaded).
    /// </summary>
    public static void Edit(string input, string output, bool withoutWorkbook = false)
    {
        File.Copy(input, output, true);
        using var doc = SpreadsheetDocument.Open(output, true);
        var wb = doc.WorkbookPart!;
        WorksheetPart part;
        if (withoutWorkbook) part = wb.WorksheetParts.First();
        else
        {
            var first = wb.Workbook!.Sheets!.Elements<Sheet>().First();
            part = (WorksheetPart)wb.GetPartById(first.Id!.Value!);
        }
        var cell = part.Worksheet!.Descendants<Cell>().First(c => c.CellReference?.Value == "B2");
        cell.DataType = null;
        cell.CellValue = new CellValue("42");
    }
}
