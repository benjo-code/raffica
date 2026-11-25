using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;

namespace Raffica.Write.Commands;

/// <summary>Why a table command cannot run at the caret or on the selected cells (<see cref="None"/>: it can).</summary>
internal enum TableBlock
{
    None,

    /// <summary>The writing view draws a table at another width than the page, so the two width commands need the page view.</summary>
    WritingViewWidths,

    /// <summary>Only the first blocks of the file are open: the editor cannot see that the body was cut, so every table command is refused.</summary>
    PrefixDocument,

    /// <summary>The command acts on a table and the caret is in none (or in a table nested in a cell).</summary>
    NotInTable,

    /// <summary>Moving a row needs exactly one selected row.</summary>
    NeedsOneRow,

    /// <summary>The editor refuses the command; <see cref="TableCommandFacts.Refusal"/> says why.</summary>
    Refused,
}

/// <summary>What the rules of the table commands need to know about the caret and the selected cells; <paramref name="Block"/> is the reason the asked command cannot run, <paramref name="HeaderOn"/> whether the first row is a header row.</summary>
internal sealed record TableCommandFacts(TableBlock Block, TableRefusal? Refusal = null, bool HeaderOn = false)
{
    /// <summary>The facts of a tab that has not been asked: every command is offered (the operation decides when it runs).</summary>
    public static TableCommandFacts Permissive { get; } = new(TableBlock.None);
}

/// <summary>The words for the reasons of the table commands (the rules show them as the reason of a disabled command, the window as a note).</summary>
internal static class TableWording
{
    /// <summary>The words for the facts; null when the command may run.</summary>
    public static string? ReasonText(TableCommandFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return facts.Block switch
        {
            TableBlock.WritingViewWidths => WriteStrings.ReasonTableWritingViewWidths,
            TableBlock.PrefixDocument => WriteStrings.ReasonTablePrefixDocument,
            TableBlock.NotInTable => WriteStrings.ReasonTableNotInTable,
            TableBlock.NeedsOneRow => WriteStrings.ReasonTableOneRow,
            TableBlock.Refused => facts.Refusal is { } refusal ? ReasonText(refusal) : WriteStrings.NoteTableNoChange,
            _ => null,
        };
    }

    /// <summary>The words for a refusal of the editor (<c>ReasonTable&lt;Name&gt;</c>; a guard test asks that every member has its words in both languages).</summary>
    public static string ReasonText(TableRefusal refusal) => WriteStrings.Get(KeyOf(refusal));

    /// <summary>The string key of a refusal.</summary>
    public static string KeyOf(TableRefusal refusal) => "ReasonTable" + refusal;
}

/// <summary>What a table command acts on: the table, the range of cells (the object selection, or the one cell of the caret) and the caret.</summary>
internal sealed record TableTarget(TableRef Table, CellRange Range, TextPosition Caret)
{
    /// <summary>The target of the session as it is now: the selected cells, else the cell of the end of the text selection; null when there is neither.</summary>
    public static TableTarget? Of(EditSession session, TextRange selection)
    {
        ArgumentNullException.ThrowIfNull(session);
        var caret = selection.End;
        if (session.ObjectSelection is CellSelection cells)
        {
            return new TableTarget(cells.Table, cells.Range, caret);
        }

        return session.Editor.TableOf(caret) is { } place
            ? new TableTarget(place.Table, new CellRange(place.Table, place.Row, place.Column, place.Row, place.Column), caret)
            : null;
    }
}

/// <summary>
/// Reads <see cref="TableCommandFacts"/> for the rules of the table commands, lazily per command: nothing exists before the first question, and nothing is read for a tab without an editing session (the prefix
/// refusal, a flag of the file, and the writing view, a flag of the view, are answered without one). The answer of each command is remembered for the model, its revision, the text selection and the object
/// selection, so a refresh of the ribbon asks the editor (which works on a copy of the table) once per command and change, never per key and never per frame. The asks come from the refresh of the ribbon, the command
/// search and the menu, never from the key path; a table command that is not on the shown tab is not asked.
/// </summary>
internal sealed class TableFactsSource
{
    private (WordDocument Model, int Revision, TextRange Range, ObjectSelection? Objects)? _key;
    private readonly Dictionary<string, TableCommandFacts> _known = [];

    /// <summary>How many times the editor was asked for the facts (tests; 0 before the first ask of a table command).</summary>
    public int Reads { get; private set; }

    /// <summary>The facts for the command <paramref name="id"/> of the tab; <paramref name="writingView"/> is whether the tab shows the writing view.</summary>
    public TableCommandFacts Read(WriteTab? tab, string id, bool writingView)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (tab is null)
        {
            return TableCommandFacts.Permissive;
        }

        if (tab.Docx.IsPrefix)
        {
            return new TableCommandFacts(TableBlock.PrefixDocument);
        }

        if (writingView && WriteCommandIds.NeedsDrawnWidths(id))
        {
            return new TableCommandFacts(TableBlock.WritingViewWidths);
        }

        if (tab.Editor is not { BlockedReason: null } session || session.Selection is not { } range)
        {
            return TableCommandFacts.Permissive;
        }

        var model = tab.Docx.Model;
        var key = (model, model.Edits.Revision, range, session.ObjectSelection);
        if (_key is not { } known || !known.Equals(key))
        {
            _key = key;
            _known.Clear();
        }

        if (_known.TryGetValue(id, out var cached))
        {
            return cached;
        }

        try
        {
            Reads++;
            var facts = Evaluate(session, id, range);
            _known[id] = facts;
            return facts;
        }
        catch (StalePositionException)
        {
            // A selection that does not belong to the model any more has no facts; the next selection or edit reads again.
            return TableCommandFacts.Permissive;
        }
    }

    /// <summary>Asks the editor whether the command may run at the selection (it works on a copy of the table and changes nothing); the page view asks it again when the command runs, so a stale answer can never run a refused command.</summary>
    internal static TableCommandFacts Evaluate(EditSession session, string id, TextRange selection)
    {
        var editor = session.Editor;
        if (id == WriteCommandIds.TableInsert)
        {
            return Of(editor.RefusalForInsertTable(selection.End, 1, 1));
        }

        if (TableTarget.Of(session, selection) is not { } target)
        {
            return new TableCommandFacts(TableBlock.NotInTable);
        }

        var (table, range) = (target.Table, target.Range);
        switch (id)
        {
            case WriteCommandIds.TableSelectRow or WriteCommandIds.TableSelectColumn or WriteCommandIds.TableSelectTable:
                return editor.RefusalForDeleteTable(table) is TableRefusal.NoTable ? Of(TableRefusal.NoTable) : TableCommandFacts.Permissive;
            case WriteCommandIds.TableRowAbove:
                return Of(editor.RefusalForAddRow(table, range.FirstRow, after: false));
            case WriteCommandIds.TableRowBelow:
                return Of(editor.RefusalForAddRow(table, range.LastRow, after: true));
            case WriteCommandIds.TableColumnLeft:
                return Of(editor.RefusalForAddColumn(table, range.FirstColumn, after: false));
            case WriteCommandIds.TableColumnRight:
                return Of(editor.RefusalForAddColumn(table, range.LastColumn, after: true));
            case WriteCommandIds.TableDeleteRows:
                return Of(editor.RefusalForDeleteRows(range));
            case WriteCommandIds.TableDeleteColumns:
                return Of(editor.RefusalForDeleteColumns(range));
            case WriteCommandIds.TableDelete:
                return Of(editor.RefusalForDeleteTable(table));
            case WriteCommandIds.TableMerge:
                return Of(editor.RefusalForMerge(range));
            case WriteCommandIds.TableSplit:
                return Of(editor.RefusalForSplit(table, range.FirstRow, range.FirstColumn));
            case WriteCommandIds.TableBorders:
                return Of(editor.RefusalForSetBorders(range, BorderSpec.All()));
            case WriteCommandIds.TableShading:
                return Of(editor.RefusalForSetShading(range, null));
            case WriteCommandIds.TableHeaderRow:
                var refusal = editor.RefusalForSetHeaderRow(table, true);
                return refusal is null ? new TableCommandFacts(TableBlock.None, null, editor.ReadTableValues(table, range.FirstRow)?.HeaderRow == true) : Of(refusal);
            case WriteCommandIds.TableProperties:
                return Of(editor.RefusalForSetTableProperties(table, new TableProperties(HeaderRow: true)));
            case WriteCommandIds.TableDistributeColumns:
                return Of(editor.RefusalForDistributeColumns(table));
            case WriteCommandIds.TableRowUp or WriteCommandIds.TableRowDown:
                return range.FirstRow != range.LastRow
                    ? new TableCommandFacts(TableBlock.NeedsOneRow)
                    : Of(editor.RefusalForMoveRow(table, range.FirstRow, id == WriteCommandIds.TableRowUp ? -1 : +1));
            default:
                return TableCommandFacts.Permissive;
        }
    }

    private static TableCommandFacts Of(TableRefusal? refusal) => refusal is { } r ? new TableCommandFacts(TableBlock.Refused, r) : TableCommandFacts.Permissive;
}
