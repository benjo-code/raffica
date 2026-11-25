using System.Diagnostics;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using SkiaSharp;

namespace WriteLayoutBench;

/// <summary>
/// The objects part of <c>edit</c>: the cases of <see cref="ObjectCases.Names"/> (a table inserted, a row added, two cells merged, borders set, a picture inserted, pasted, resized and moved, a footnote inserted and
/// deleted) through the editor and the layout, without a window: the operations of <see cref="TextEditor"/> and <see cref="DocumentLayout.ApplyEdit"/> for every block change through the page after the first one.
/// Every run is undone outside the timing and the layout completed, so each run starts from the state of the file. The rows are for the record: the limit of the objects cases is judged on the app
/// (<c>--measure-objects</c>). A picture the run inserts has no bytes in the package the bench's layout reads, so the layout draws its placeholder box (the size of the picture is the same). <see cref="ObjectCases"/>
/// is a near copy of the app's <c>ObjectPlaces</c> and the choice of the places a simplification of the app's (the bench cannot reference the app): change them together.
/// </summary>
internal static partial class EditCommand
{
    private const int ObjectRuns = 20;

    // A small picture (64 by 48 pixels, one colour), made the way the paste tests of the app make theirs.
    private static byte[] ObjectPng()
    {
        using var bitmap = new SKBitmap(64, 48);
        bitmap.Erase(new SKColor(0x33, 0x66, 0x99));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>The rows of the objects measurement (one per case that the document allows).</summary>
    internal static List<EditRow> MeasureObjects(string file, int count, TextWriter error)
    {
        var bytes = File.ReadAllBytes(file);
        using var docx = DocxFile.Open(bytes);
        var model = docx.Model;
        var layout = LayOut(model, FontCatalog.CreateShipped(), new PackageImages(bytes), LayoutMode.Pages);
        var format = new RefreshingFormat(model);
        var editor = new TextEditor(model, (story, paragraph) => layout.ProjectionHost(story, paragraph), format: format);
        var blocks = model.Body.Blocks;
        var rows = new List<EditRow>();
        var picks = EditCases.Pick(model, layout);
        var png = ObjectPng();

        void Apply(EditResult? result)
        {
            if (result is null)
            {
                throw new InvalidOperationException("an operation of the objects measurement was refused");
            }

            var through = Math.Min(Math.Max(0, layout.LaidOutPages - 1), 1);
            if (result.Style is not null)
            {
                format.Invalidate();
                layout.Restyle(through);
            }
            else
            {
                foreach (var change in result.Changes)
                {
                    layout.ApplyEdit(change, through);
                }
            }
        }

        // One case: the preparation (null when there is none), the timed operation, then every change undone. Both read their place again for every run, because an undo may replace paragraph objects.
        double[] Run(Func<EditResult?> prepare, Func<EditResult?> timed)
        {
            var millis = new double[count];
            for (var i = 0; i < count; i++)
            {
                Complete(layout);
                var undos = 0;
                if (prepare() is { } before)
                {
                    Apply(before);
                    Complete(layout);
                    undos++;
                }

                var start = Stopwatch.GetTimestamp();
                Apply(timed());
                millis[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                undos++;
                for (; undos > 0; undos--)
                {
                    Complete(layout);
                    Apply(editor.History.Undo());
                }

                Complete(layout);
            }

            return millis;
        }

        EditResult? Nothing() => null;
        EditRow Row(string name, double[] millis) => new(name, "page", string.Empty, millis, 0, 0, null);

        void Add(string name, bool possible, Func<EditResult?> prepare, Func<EditResult?> timed)
        {
            if (possible)
            {
                rows.Add(Row(name, Run(prepare, timed)));
            }
            else
            {
                error.WriteLine($"write-layout-bench: the document has no place for the case '{name}'");
            }
        }

        var middle = picks.FirstOrDefault(c => c.Name == "middle") is { } pick && blocks[pick.HintBlock] is Paragraph ? pick.HintBlock : (int?)null;
        TextPosition At(int block, int offset = 0) => new(StoryRef.Body, (Paragraph)blocks[block], offset);

        // The table near the middle of the body whose first two cells merge (read by trying the merge once and undoing it).
        var tableBlock = Enumerable.Range(0, blocks.Count).Where(i => blocks[i] is Table).OrderBy(i => Math.Abs(i - (blocks.Count / 2))).FirstOrDefault(i =>
        {
            var range = new CellRange(new TableRef(StoryRef.Body, i), 0, 0, 0, 1);
            if (editor.MergeCells(range) is null)
            {
                return false;
            }

            editor.History.Undo();
            return true;
        }, -1);

        Add(ObjectCases.TableInsert, middle is not null, Nothing, () => editor.InsertTable(At(middle!.Value), 5, 5));
        Add(ObjectCases.TableAddRow, tableBlock >= 0, Nothing, () => editor.AddRow(new TableRef(StoryRef.Body, tableBlock), 0, after: true));
        Add(ObjectCases.TableMerge, tableBlock >= 0, Nothing, () => editor.MergeCells(new CellRange(new TableRef(StoryRef.Body, tableBlock), 0, 0, 0, 1)));
        Add(ObjectCases.TableBorderSet, tableBlock >= 0, Nothing, () => editor.SetBorders(new CellRange(new TableRef(StoryRef.Body, tableBlock), 0, 0, 0, 1), BorderSpec.All()));
        Add(ObjectCases.PictureInsert, middle is not null, Nothing, () => editor.InsertPicture(At(middle!.Value), png, "measure"));
        Add(ObjectCases.PicturePaste, middle is not null, Nothing, () =>
        {
            var position = At(middle!.Value);
            return editor.Paste(new TextRange(position, position), new ClipboardContent(string.Empty, null, Image: png), _ => true);
        });

        // The picture of the resize and move cases is inserted by the preparation (untimed) and found again from its place.
        ObjectId? PictureOf(int block, int offset) => editor.PictureAt(At(block, offset));
        Add(ObjectCases.PictureResize, middle is not null, () => editor.InsertPicture(At(middle!.Value), png, "measure"), () =>
            PictureOf(middle!.Value, 0) is { } id ? editor.ResizePicture(id, 1905000, 1428750) : null);
        Add(ObjectCases.PictureFloatMove, middle is not null && middle.Value + 2 < blocks.Count && blocks[middle.Value + 2] is Paragraph, () =>
        {
            var inserted = editor.InsertPicture(At(middle!.Value), png, "measure");
            if (inserted is null)
            {
                return null;
            }

            Apply(inserted);
            Complete(layout);
            // Floating (square wrap) is its own history step: both are undone after the timed move.
            return PictureOf(middle.Value, 0) is { } id ? editor.SetPictureWrap(id, PictureWrap.Square) : null;
        }, () => PictureOf(middle!.Value, 0) is { } id ? editor.MovePicture(id, At(middle.Value + 2), 127000, 127000) : null);

        // A footnote: inserted at the start of a paragraph near the middle, and the reference of an existing note deleted.
        Add(ObjectCases.FootnoteInsert, middle is not null, Nothing, () => editor.InsertNote(At(middle!.Value), NoteType.Footnote));
        var cited = Enumerable.Range(0, blocks.Count).Where(i => blocks[i] is Paragraph).OrderBy(i => Math.Abs(i - (blocks.Count / 2)))
            .Select(i => (Block: i, Piece: ParagraphProjection.Build((Paragraph)blocks[i], layout.ProjectionHost(StoryRef.Body, (Paragraph)blocks[i])).Pieces.OfType<AtomicPiece>().FirstOrDefault(p => p.Atomic == AtomicKind.NoteReference)))
            .FirstOrDefault(c => c.Piece is not null);
        Add(ObjectCases.FootnoteDelete, cited.Piece is not null, Nothing, () =>
        {
            var start = At(cited.Block, cited.Piece!.Start);
            return editor.Delete(new TextRange(start, start with { Offset = cited.Piece.Start + cited.Piece.Length }));
        });

        return rows;
    }
}

/// <summary>The cases of the objects measurement (<c>ObjectPlaces.Names</c> of the Write app and <c>WriteRun.ObjectsNames</c> of measure.cs, near copies: change the three together).</summary>
internal static class ObjectCases
{
    public const string TableInsert = "table-insert";
    public const string TableAddRow = "table-add-row";
    public const string TableMerge = "table-merge";
    public const string TableBorderSet = "table-border-set";
    public const string PictureInsert = "picture-insert";
    public const string PicturePaste = "picture-paste";
    public const string PictureResize = "picture-resize";
    public const string PictureFloatMove = "picture-float-move";
    public const string FootnoteInsert = "footnote-insert";
    public const string FootnoteDelete = "footnote-delete";

    /// <summary>The cases in the order of the rows.</summary>
    public static readonly string[] Names = [TableInsert, TableAddRow, TableMerge, TableBorderSet, PictureInsert, PicturePaste, PictureResize, PictureFloatMove, FootnoteInsert, FootnoteDelete];
}
