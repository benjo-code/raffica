using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Settings;

namespace Raffica.Write.Viewing;

/// <summary>
/// The table commands of the page view (<c>table.*</c>: the grid of the Insert tab and the Table tab): the ribbon, the command search and the menu end here. Every change is one <see cref="EditSession.Apply"/>
/// through <see cref="ApplyGuarded"/> (one history step, the failure rule of a key); the three selecting commands change no document. Each command reads its target (the selected cells, else the cell of the caret) from
/// the model as it is when the operation runs, and asks the editor again whether it may run (the editor works on a copy of the table), so a stale answer of the facts can never change the wrong cells; a command that
/// finds nothing to do says so through <see cref="StructureNote"/>, never silently.
/// </summary>
/// <remarks>
/// Rules. The operations replace the table and its paragraphs, so a cell selection that went through a structure change is gone (the session drops it when the caret moves, and the commands select nothing new);
/// after a formatting operation, which changes no shape, the same cells are selected again by row and column. The two width commands pass the widths the page layout draws now (the column borders of the object
/// map, in twips), so the first width write freezes what the reader sees; the writing view draws the table at another width, so they are refused there. Insert table and the row and column commands keep the
/// editor's caret.
/// </remarks>
internal sealed partial class PageView
{
    /// <summary>
    /// Runs a table command; true when it was taken (also when it changed nothing), false for a released view, an unknown command or a payload that is not a value of the command (nothing changes then).
    /// <paramref name="payload"/> is a <see cref="TableSize"/> for Insert table, a preset id or a <see cref="BorderSpec"/> for Borders, a colour id for Shading and a <see cref="TableProperties"/> for Table properties.
    /// </summary>
    internal bool RunTableCommand(string id, object? payload = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (_released || !WriteCommandIds.IsTable(id) || !TablePayloads.IsValid(id, payload))
        {
            return false;
        }

        var prefix = IsPrefixDocument;
        if (prefix)
        {
            StructureNote?.Invoke(WriteStrings.ReasonTablePrefixDocument);
            return true;
        }

        if (_mode == WriteView.Writing && WriteCommandIds.NeedsDrawnWidths(id))
        {
            StructureNote?.Invoke(WriteStrings.ReasonTableWritingViewWidths);
            return true;
        }

        if (SessionForCommand() is not { } session)
        {
            return true;
        }

        if (id is WriteCommandIds.TableSelectRow or WriteCommandIds.TableSelectColumn or WriteCommandIds.TableSelectTable)
        {
            SelectTablePart(session, id);
            return true;
        }

        // The cells that were selected: a formatting operation keeps them, the layout of the table does not change.
        var before = session.ObjectSelection as CellSelection;
        var (anchor, focus) = (session.CellAnchor, session.CellFocus);
        var layout = _mode == WriteView.Page ? ActiveLayout() : null;
        TableCommandFacts? refused = null;
        var result = ApplyGuarded(session, ed => session.Selection is { } selection ? TableOperation(ed, session, id, payload, selection, layout, prefix, out refused) : null);

        // The rules said yes but the editor had nothing to do (a refusal of the copy, or no place): a visible note, never silence. A save queues the operation and says nothing.
        if (result is null && session.BlockedReason is null)
        {
            StructureNote?.Invoke(refused is { } facts ? TableWording.ReasonText(facts) ?? WriteStrings.NoteTableNoChange : WriteStrings.NoteTableNoChange);
        }
        else if (result is not null && before is not null && anchor is { } a && focus is { } f && KeepsTheCells(id))
        {
            session.SelectCells(before.Table, a, f);
        }

        return true;
    }

    // The commands that change no shape: the cells that were selected are still the cells the user means.
    private static bool KeepsTheCells(string id) => id is WriteCommandIds.TableBorders or WriteCommandIds.TableShading or WriteCommandIds.TableHeaderRow or WriteCommandIds.TableProperties or WriteCommandIds.TableDistributeColumns;

    private static void SelectTablePart(EditSession session, string id)
    {
        // The place of the caret (not the selected cells: after Select row the row is selected, and Select column still means the column of the caret).
        if (session.Selection is not { } selection || session.Editor.TableOf(selection.End) is not { } place)
        {
            return;
        }

        _ = id switch
        {
            WriteCommandIds.TableSelectRow => session.SelectRow(place.Table, place.Row),
            WriteCommandIds.TableSelectColumn => session.SelectColumn(place.Table, place.Column),
            _ => session.SelectTable(place.Table),
        };
    }

    // The operation, under the lease and on the model as it is now: the editor is asked first (a refusal ends the command with its reason), then it runs.
    private static EditResult? TableOperation(TextEditor ed, EditSession session, string id, object? payload, TextRange selection, DocumentLayout? layout, bool prefix, out TableCommandFacts? refused)
    {
        refused = null;
        var facts = TableFactsSource.Evaluate(session, id, selection);
        if (facts.Block != TableBlock.None)
        {
            refused = facts;
            return null;
        }

        if (id == WriteCommandIds.TableInsert)
        {
            return payload is TableSize size ? ed.InsertTable(selection.End, size.Columns, size.Rows, prefix) : null;
        }

        if (TableTarget.Of(session, selection) is not { } target)
        {
            refused = new TableCommandFacts(TableBlock.NotInTable);
            return null;
        }

        var (table, range, caret) = (target.Table, target.Range, target.Caret);
        switch (id)
        {
            case WriteCommandIds.TableRowAbove:
                return ed.AddRow(table, range.FirstRow, after: false, prefix);
            case WriteCommandIds.TableRowBelow:
                return ed.AddRow(table, range.LastRow, after: true, prefix);
            case WriteCommandIds.TableColumnLeft:
                return ed.AddColumn(table, range.FirstColumn, after: false, prefix);
            case WriteCommandIds.TableColumnRight:
                return ed.AddColumn(table, range.LastColumn, after: true, prefix);
            case WriteCommandIds.TableDeleteRows:
                return ed.DeleteRows(range, prefix);
            case WriteCommandIds.TableDeleteColumns:
                return ed.DeleteColumns(range, prefix);
            case WriteCommandIds.TableDelete:
                return ed.DeleteTable(table, prefix);
            case WriteCommandIds.TableMerge:
                return ed.MergeCells(range, prefix);
            case WriteCommandIds.TableSplit:
                return ed.SplitCell(table, range.FirstRow, range.FirstColumn, prefix);
            case WriteCommandIds.TableRowUp:
                return ed.MoveRow(table, range.FirstRow, -1, prefix);
            case WriteCommandIds.TableRowDown:
                return ed.MoveRow(table, range.FirstRow, +1, prefix);
            case WriteCommandIds.TableBorders:
                return (payload is BorderSpec spec ? spec : payload is string preset ? TablePayloads.Preset(preset) : null) is { } border ? ed.SetBorders(range, border, caret, prefix) : null;
            case WriteCommandIds.TableShading:
                return payload is string color && TablePayloads.TryShading(color, out var argb) ? ed.SetShading(range, argb, caret, prefix) : null;
            case WriteCommandIds.TableHeaderRow:
                return ed.SetHeaderRow(table, ed.ReadTableValues(table, range.FirstRow)?.HeaderRow != true, caret, prefix);
            case WriteCommandIds.TableProperties:
                return payload is TableProperties properties ? ed.SetTableProperties(table, properties, DrawnColumnTwips(ed, layout, table), caret, prefix) : null;
            case WriteCommandIds.TableDistributeColumns:
                return ed.DistributeColumns(table, DrawnColumnTwips(ed, layout, table), caret, prefix);
            default:
                return null;
        }
    }

    /// <summary>
    /// The widths the page layout draws for the columns of the table now, in twips, read from the column borders of the object map of the first laid-out page that shows the table; null when the table is on no
    /// laid-out page (the editor then reads the widths from the model: the documented fallback) or there is no page layout. Never reads a page that is gone.
    /// </summary>
    internal static IReadOnlyList<int>? DrawnColumnTwips(TextEditor editor, DocumentLayout? layout, TableRef table)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(table);
        if (layout is null || editor.ReadTableValues(table, 0) is not { } values)
        {
            return null;
        }

        var columns = values.GridTwips.Count;
        for (var p = 0; p < layout.LaidOutPages; p++)
        {
            if (!layout.TryGetPage(p, out var page) || page is null)
            {
                continue;
            }

            var grid = page.Objects.Tables.FirstOrDefault(t => t.Story == table.Story && t.BlockIndex == table.BlockIndex && t.ColumnBorders.Count == columns + 1);
            if (grid is not null)
            {
                return [.. Enumerable.Range(0, columns).Select(c => (int)Math.Round((grid.ColumnBorders[c + 1] - grid.ColumnBorders[c]) * 20.0))];
            }
        }

        return null;
    }

    /// <summary>
    /// What the Table properties card shows: the model's numbers (<see cref="TableValues"/>) with the column widths and the table width the page layout draws now where it can say (a table that is not fixed is
    /// drawn from its content or from the room), the selected rows and the grid's own column count. Null, with <paramref name="note"/> naming why, when the command cannot be described (no table at the caret, a prefix
    /// document, the writing view, a document that cannot be edited).
    /// </summary>
    internal TablePropertiesFacts? TablePropertiesFactsAtCaret(out string? note)
    {
        note = null;
        if (IsPrefixDocument)
        {
            note = WriteStrings.ReasonTablePrefixDocument;
            return null;
        }

        if (_mode == WriteView.Writing)
        {
            note = WriteStrings.ReasonTableWritingViewWidths;
            return null;
        }

        if (SessionForCommand() is not { } session || session.Selection is not { } selection)
        {
            note = WriteStrings.NoteTableNoChange;
            return null;
        }

        if (TableTarget.Of(session, selection) is not { } target)
        {
            note = WriteStrings.ReasonTableNotInTable;
            return null;
        }

        var editor = session.Editor;
        if (editor.ReadTableValues(target.Table, target.Range.FirstRow) is not { } values)
        {
            note = TableWording.ReasonText(TableRefusal.UnsupportedStructure);
            return null;
        }

        var drawn = DrawnColumnTwips(editor, ActiveLayout(), target.Table);
        var columns = drawn ?? values.GridTwips;
        var total = drawn is not null ? drawn.Sum() : values.TableWidthTwips ?? values.GridTwips.Sum();
        return new TablePropertiesFacts(columns, total, values.HeaderRow, values.RowHeightTwips, values.RowHeightExact, target.Range.FirstRow, target.Range.LastRow);
    }
}
