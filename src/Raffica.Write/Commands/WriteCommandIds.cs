using Raffica.Write.Editing;

namespace Raffica.Write.Commands;

/// <summary>The ids of the commands of Write (the ids are the ones the command search and the field codes use).</summary>
internal static class WriteCommandIds
{
    public const string New = "file.new";
    public const string Open = "file.open";
    public const string Save = "file.save";
    public const string SaveAs = "file.saveAs";
    public const string CloseTab = "file.close";
    public const string Settings = "app.settings";
    public const string Search = "app.search";
    public const string PageView = "view.page";
    public const string WritingView = "view.writing";
    public const string ZoomIn = "view.zoomIn";
    public const string ZoomOut = "view.zoomOut";
    public const string ActualSize = "view.actualSize";
    public const string FitWidth = "view.fitWidth";
    public const string FitPage = "view.fitPage";
    public const string Outline = "view.outline";
    public const string Undo = EditingKeys.UndoCommand;
    public const string Redo = EditingKeys.RedoCommand;
    public const string Cut = EditingKeys.CutCommand;
    public const string Copy = EditingKeys.CopyCommand;
    public const string Paste = EditingKeys.PasteCommand;
    public const string SelectAll = "edit.selectAll";
    public const string FormattingPane = "view.formatting";
    public const string Bold = "format.bold";
    public const string Italic = "format.italic";
    public const string Underline = "format.underline";
    public const string Strike = "format.strike";
    public const string Superscript = "format.superscript";
    public const string Subscript = "format.subscript";
    public const string SmallCaps = "format.smallCaps";
    public const string AllCaps = "format.allCaps";
    public const string AlignLeft = "format.alignLeft";
    public const string AlignCenter = "format.alignCenter";
    public const string AlignRight = "format.alignRight";
    public const string Justify = "format.justify";
    public const string IndentMore = "format.indentMore";
    public const string IndentLess = "format.indentLess";
    public const string KeepNext = "format.keepNext";
    public const string KeepLines = "format.keepLines";
    public const string PageBreakBefore = "format.pageBreakBefore";
    public const string WidowControl = "format.widowControl";
    public const string ClearDirect = "format.clearDirect";
    public const string UpdateStyle = "format.updateStyle";
    public const string Font = "format.font";
    public const string Size = "format.size";
    public const string SizeUp = "format.sizeUp";
    public const string SizeDown = "format.sizeDown";
    public const string Color = "format.color";
    public const string Highlight = "format.highlight";
    public const string SpaceBefore = "format.spaceBefore";
    public const string SpaceAfter = "format.spaceAfter";
    public const string LineSpacing = "format.lineSpacing";
    public const string ParagraphStyle = "format.paragraphStyle";
    public const string CharacterStyle = "format.characterStyle";
    public const string NumberHeadings = "structure.numberHeadings";
    public const string Bullets = "list.bullets";
    public const string Numbers = "list.numbers";
    public const string RestartNumbering = "list.restart";
    public const string ContinueNumbering = "list.continue";
    public const string MoveUp = "outline.moveUp";
    public const string MoveDown = "outline.moveDown";
    public const string Promote = "outline.promote";
    public const string Demote = "outline.demote";

    /// <summary>Whether the command belongs to the text (undo, redo, the clipboard and select all): the page view owns their keys, so the window never matches them itself.</summary>
    public static bool IsEditing(string id) => id.StartsWith("edit.", StringComparison.Ordinal);

    /// <summary>
    /// Whether the command formats the text (character, paragraph and style commands, <c>format.*</c>): like the edit commands they act on the selection of the page view, which owns their keys
    /// (a text box in a pane keeps its own Ctrl+B), so the window never matches them itself. The Formatting pane switch (<see cref="FormattingPane"/>) is a view command and not one of them.
    /// </summary>
    public static bool IsFormatting(string id) => id.StartsWith("format.", StringComparison.Ordinal);

    /// <summary>
    /// Whether the command is one of the structure commands: the heading numbering switch, the list commands and the outline commands (move, promote, demote). Their keys (Alt+Shift with the arrows,
    /// Tab in a list) belong to the page view like the formatting keys, so the window never matches them itself; the facts that enable them are read through <see cref="StructureFacts"/>.
    /// </summary>
    public static bool IsStructure(string id) => id.StartsWith("structure.", StringComparison.Ordinal) || id.StartsWith("list.", StringComparison.Ordinal) || id.StartsWith("outline.", StringComparison.Ordinal);

    /// <summary>Whether the command is one of the four outline commands (they act on a heading: the one of the selected outline row, or the one at the caret).</summary>
    public static bool IsOutline(string id) => id.StartsWith("outline.", StringComparison.Ordinal);

    /// <summary>Whether the command is a value command: the ribbon picker that chooses the value passes it to the action as the payload (<see cref="CatalogCommand.TakesPayload"/>).</summary>
    public static bool TakesPayload(string id) => id is Font or Size or SizeUp or SizeDown or Color or Highlight or SpaceBefore or SpaceAfter or LineSpacing or ParagraphStyle or CharacterStyle;
}
