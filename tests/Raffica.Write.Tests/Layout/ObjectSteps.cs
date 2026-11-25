using System.Globalization;
using System.Xml.Linq;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>The kinds of object step of the oracle gates.</summary>
internal enum ObjectKind
{
    InsertFootnote,
    InsertEndnote,
    DeleteReference,
    InsertTable,
    AddRow,
    AddColumn,
    DeleteRows,
    DeleteColumns,
    Merge,
    Split,
    MoveRow,
    DeleteTable,
    SetBorders,
    SetShading,
    SetHeaderRow,
    SetColumnWidths,
    SetTableWidth,
    SetRowHeight,
    DistributeColumns,
    InsertPicture,
    Resize,
    Wrap,
    Move,
    Nudge,
    DeletePicture,
    Undo,
    Redo,
}

/// <summary>
/// The object steps of the oracle gates (milestone 4.5a): a footnote or an endnote inserted at a random place, the deletion of a reference (with its note), the table steps (see <c>ObjectSteps.Tables.cs</c>), and undo and redo, each made by the real
/// <see cref="TextEditor"/>, brought into the layout as the session does (a result that made styles starts the layout over with <see cref="DocumentLayout.Restyle"/>, any other is
/// <see cref="DocumentLayout.ApplyEdit"/>) and compared with a full layout, exactly, and the note numbers drawn on the pages are compared with an independent count of the references in document
/// order. A step the editor refuses (null, or no change) is counted as a refusal and compared nowhere, except an undo and a redo with something on the stack, which must always change something.
/// </summary>
internal sealed partial class ObjectSteps
{
    /// <summary>The number of kinds.</summary>
    public static readonly int Kinds = Enum.GetValues<ObjectKind>().Length;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly ObjectKind[] s_forcedOrder =
    [
        ObjectKind.InsertFootnote,
        ObjectKind.InsertEndnote,
        ObjectKind.InsertFootnote,
        ObjectKind.InsertFootnote,
        ObjectKind.InsertEndnote,
        ObjectKind.DeleteReference,
        ObjectKind.Undo,
        ObjectKind.Redo,
        ObjectKind.Undo,
        ObjectKind.Undo,
        ObjectKind.InsertTable,
        ObjectKind.AddRow,
        ObjectKind.AddColumn,
        ObjectKind.Merge,
        ObjectKind.Split,
        ObjectKind.MoveRow,
        ObjectKind.SetBorders,
        ObjectKind.SetShading,
        ObjectKind.SetHeaderRow,
        ObjectKind.SetColumnWidths,
        ObjectKind.SetTableWidth,
        ObjectKind.SetRowHeight,
        ObjectKind.DistributeColumns,
        ObjectKind.DeleteRows,
        ObjectKind.DeleteColumns,
        ObjectKind.DeleteTable,
        ObjectKind.InsertPicture,
        ObjectKind.Wrap,
        ObjectKind.Resize,
        ObjectKind.Nudge,
        ObjectKind.Move,
        ObjectKind.Wrap,
        ObjectKind.DeletePicture,
        ObjectKind.Undo,
        ObjectKind.Undo,
        ObjectKind.Redo,
    ];

    private readonly WordDocument _model;
    private readonly DocumentLayout _layout;
    private readonly LayoutOptions _options;
    private ObjectKind _kind;

    public ObjectSteps(WordDocument model, DocumentLayout layout, LayoutOptions options)
    {
        _model = model;
        _layout = layout;
        _options = options;
        Editor = new TextEditor(model, (s, p) => layout.ProjectionHost(s, p));
    }

    public TextEditor Editor { get; }

    /// <summary>For each kind, how many of its steps changed something and were compared.</summary>
    public int[] RanPerKind { get; } = new int[Kinds];

    /// <summary>For each kind, how many attempts the editor refused (null or no change) or the document had nothing for.</summary>
    public int[] RefusedPerKind { get; } = new int[Kinds];

    /// <summary>How many steps were compared.</summary>
    public int Compared { get; private set; }

    /// <summary>How many steps started the layout over because they made styles.</summary>
    public int Restyles { get; private set; }

    /// <summary>How many note numbers were compared with the reference, summed over the steps.</summary>
    public long NumbersCompared { get; private set; }

    /// <summary>The labels of the steps that were compared, in order (a failure message carries them).</summary>
    public List<string> Trace { get; } = [];

    /// <summary>One of every kind in a fixed order, then <paramref name="steps"/> random steps, all from <paramref name="random"/> (with a <paramref name="pool"/> no fixed order is run and the random steps draw only from the pool). Returns how many steps were compared.</summary>
    public async Task<int> RunAsync(Random random, int steps, string label, IReadOnlyList<ObjectKind>? pool = null)
    {
        var before = Compared;
        foreach (var kind in pool is null ? s_forcedOrder : Array.Empty<ObjectKind>())
        {
            await RunKindAsync(kind, random, $"{label} forced {kind}");
        }

        for (var i = 0; i < steps; i++)
        {
            var kind = pool is null ? (ObjectKind)random.Next(Kinds) : pool[random.Next(pool.Count)];
            await RunKindAsync(kind, random, $"{label} step {i} {kind}");
        }

        return Compared - before;
    }

    /// <summary>Runs one step of a kind. Returns true when it changed something and was compared.</summary>
    public async Task<bool> RunKindAsync(ObjectKind kind, Random random, string label)
    {
        _kind = kind;
        try
        {
            return kind switch
            {
                ObjectKind.InsertFootnote => await InsertAsync(NoteType.Footnote, random, label),
                ObjectKind.InsertEndnote => await InsertAsync(NoteType.Endnote, random, label),
                ObjectKind.DeleteReference => await DeleteAsync(random, label),
                ObjectKind.InsertTable => await InsertTableAsync(random, label),
                ObjectKind.AddRow => await AddRowAsync(random, label),
                ObjectKind.AddColumn => await AddColumnAsync(random, label),
                ObjectKind.DeleteRows => await DeleteRowsAsync(random, label),
                ObjectKind.DeleteColumns => await DeleteColumnsAsync(random, label),
                ObjectKind.Merge => await MergeAsync(random, label),
                ObjectKind.Split => await SplitAsync(random, label),
                ObjectKind.MoveRow => await MoveRowAsync(random, label),
                ObjectKind.DeleteTable => await DeleteTableAsync(random, label),
                ObjectKind.SetBorders => await SetBordersAsync(random, label),
                ObjectKind.SetShading => await SetShadingAsync(random, label),
                ObjectKind.SetHeaderRow => await SetHeaderRowAsync(random, label),
                ObjectKind.SetColumnWidths => await SetColumnWidthsAsync(random, label),
                ObjectKind.SetTableWidth => await SetTableWidthAsync(random, label),
                ObjectKind.SetRowHeight => await SetRowHeightAsync(random, label),
                ObjectKind.DistributeColumns => await DistributeColumnsAsync(random, label),
                ObjectKind.InsertPicture => await InsertPictureAsync(random, label),
                ObjectKind.Resize => await ResizePictureAsync(random, label),
                ObjectKind.Wrap => await WrapPictureAsync(random, label),
                ObjectKind.Move => await MovePictureAsync(random, label),
                ObjectKind.Nudge => await NudgePictureAsync(random, label),
                ObjectKind.DeletePicture => await DeletePictureAsync(random, label),
                ObjectKind.Undo => Editor.History.CanUndo ? await CheckAsync(Editor.History.Undo(), label, required: true) : Refuse(),
                _ => Editor.History.CanRedo ? await CheckAsync(Editor.History.Redo(), label, required: true) : Refuse(),
            };
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException($"{label}: {ex.Message} (steps so far: {string.Join(", ", Trace)})", ex);
        }
    }

    private List<Paragraph> Paragraphs() => [.. ModelWalk.Blocks(_model.Body.Blocks).OfType<Paragraph>()];

    private int TextLength(Paragraph paragraph) => ParagraphProjection.Build(paragraph, _layout.ProjectionHost(StoryRef.Body, paragraph)).Text.Length;

    private async Task<bool> InsertAsync(NoteType type, Random random, string label)
    {
        var candidates = Paragraphs().Where(p => Editor.RefusalForNote(new TextPosition(StoryRef.Body, p, 0)) is null).ToList();
        if (candidates.Count == 0)
        {
            return Refuse();
        }

        var paragraph = candidates[random.Next(candidates.Count)];
        var offset = random.Next(TextLength(paragraph) + 1);
        return await CheckAsync(Editor.InsertNote(new TextPosition(StoryRef.Body, paragraph, offset), type), label, required: false);
    }

    private async Task<bool> DeleteAsync(Random random, string label)
    {
        var holders = Paragraphs().Where(p => References(p).Count > 0).ToList();
        if (holders.Count == 0)
        {
            return Refuse();
        }

        var paragraph = holders[random.Next(holders.Count)];
        var references = References(paragraph);
        var reference = references[random.Next(references.Count)];
        var projection = ParagraphProjection.Build(paragraph, _layout.ProjectionHost(StoryRef.Body, paragraph));
        var piece = projection.Pieces.OfType<AtomicPiece>().FirstOrDefault(a => ReferenceEquals(a.Content, reference));
        if (piece is null || piece.End <= piece.Start)
        {
            return Refuse();
        }

        // A random, wider or narrower range around the reference: the reference is inside it, so its note goes with it.
        var start = Math.Max(0, piece.Start - random.Next(0, 3));
        var end = Math.Min(projection.Text.Length, piece.End + random.Next(0, 3));
        return await CheckAsync(Editor.Delete(new TextRange(new TextPosition(StoryRef.Body, paragraph, start), new TextPosition(StoryRef.Body, paragraph, end))), label, required: false);
    }

    private static List<RunContent> References(Paragraph paragraph) =>
        [.. ModelWalk.Inlines(paragraph.Inlines).OfType<Run>().SelectMany(r => r.Content).Where(c => c is FootnoteReference or EndnoteReference)];

    private async Task<bool> CheckAsync(EditResult? result, string label, bool required)
    {
        if (result is null || (result.Changes.Count == 0 && result.Style is null && result.Sections is null))
        {
            if (required)
            {
                Assert.Fail(label + ": the step returned no change");
            }

            return Refuse();
        }

        if (result.Style is not null)
        {
            // Styles were made (the first note of a document): the engine read the styles once, so the layout starts over and the block change is not applied into it either.
            _layout.Restyle(2);
            Restyles++;
        }
        else
        {
            foreach (var change in result.Changes)
            {
                _layout.ApplyEdit(change, change.Container == _model.Body.Blocks ? Math.Max(1, (_layout.Locate(Math.Min(change.First, Math.Max(0, _model.Body.Blocks.Count - 1)))?.Page ?? 0) + 1) : 2);
            }
        }

        await _layout.LayOutRestAsync(null, CancellationToken.None);
        LayoutOracle.AssertEqualToFull(_layout, _model, _options, label);
        NumbersCompared += AssertNumbers(label);
        GridsCompared += AssertTableGrids(label);
        AssertChangedTables(result, label);
        Compared++;
        RanPerKind[(int)_kind]++;
        Trace.Add(label);
        return true;
    }

    private bool Refuse()
    {
        RefusedPerKind[(int)_kind]++;
        return false;
    }

    /// <summary>
    /// Fails when the note numbers drawn on the pages differ from the independent count: the n-th reference of a kind in document order shows n (decimal, or lower or upper roman by the settings of the
    /// part; other formats are compared by count only), and a note is drawn once for every reference to it whose note exists. Returns how many labels were compared.
    /// </summary>
    private long AssertNumbers(string label)
    {
        long compared = 0;
        foreach (var type in new[] { NoteType.Footnote, NoteType.Endnote })
        {
            var collection = type == NoteType.Footnote ? _model.Footnotes : _model.Endnotes;
            var references = ModelWalk.RunContents(_model.Body).Where(c => type == NoteType.Footnote ? c is FootnoteReference : c is EndnoteReference).ToList();
            var format = (string?)_model.Settings?.Properties.Get(W + (type == NoteType.Footnote ? "footnotePr" : "endnotePr"))?.Element(W + "numFmt")?.Attribute(W + "val")
                ?? (type == NoteType.Footnote ? "decimal" : "lowerRoman");
            var expected = new List<string>();
            for (var i = 0; i < references.Count; i++)
            {
                var id = references[i] is FootnoteReference f ? f.Id : ((EndnoteReference)references[i]).Id;
                if (collection?.Find(id) is { Kind: NoteKind.Normal } note && note.Content.Blocks.FirstOrDefault() is Paragraph)
                {
                    expected.Add(Format(i + 1, format) ?? "?");
                }
            }

            var drawn = new List<string>();
            for (var p = 0; p < _layout.LaidOutPages; p++)
            {
                foreach (var line in _layout.Page(p).TextLines)
                {
                    var endnote = line.Story.Key?.StartsWith("endnote:", StringComparison.Ordinal) == true;
                    if (line.Story.Kind != StoryKind.Note || endnote != (type == NoteType.Endnote) || line.Start != 0)
                    {
                        continue;
                    }

                    var id = int.Parse(line.Story.Key!.Replace("endnote:", string.Empty, StringComparison.Ordinal), CultureInfo.InvariantCulture);
                    if (collection?.Find(id)?.Content.Blocks.FirstOrDefault() is Paragraph first && ReferenceEquals(first, line.Paragraph))
                    {
                        drawn.Add(line.Projection.Text.Split(' ')[0]);
                    }
                }
            }

            Assert.True(drawn.Count == expected.Count, $"{label}: {drawn.Count} {type} notes drawn against {expected.Count} references with a note ({string.Join(",", drawn)} against {string.Join(",", expected)})");
            if (expected.All(e => e != "?"))
            {
                Assert.True(drawn.SequenceEqual(expected, StringComparer.Ordinal), $"{label}: the {type} numbers drawn are {string.Join(",", drawn)}, the references in document order say {string.Join(",", expected)}");
                compared += drawn.Count;
            }
        }

        return compared;
    }

    private static string? Format(int number, string format) => format switch
    {
        "decimal" => number.ToString(CultureInfo.InvariantCulture),
        "lowerRoman" => Roman(number).ToLowerInvariant(),
        "upperRoman" => Roman(number),
        _ => null,
    };

    private static string Roman(int number)
    {
        (int Value, string Text)[] table = [(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")];
        var text = new System.Text.StringBuilder();
        foreach (var (value, symbol) in table)
        {
            while (number >= value)
            {
                text.Append(symbol);
                number -= value;
            }
        }

        return text.ToString();
    }
}
