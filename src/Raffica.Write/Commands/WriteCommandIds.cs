using Raffica.Write.Editing;

namespace Raffica.Write.Commands;

/// <summary>The ids of the commands of Write (the ids are the ones the command search and the field codes use).</summary>
internal static class WriteCommandIds
{
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

    /// <summary>Whether the command belongs to the text (undo, redo, the clipboard and select all): the page view owns their keys, so the window never matches them itself.</summary>
    public static bool IsEditing(string id) => id.StartsWith("edit.", StringComparison.Ordinal);
}
