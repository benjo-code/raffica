namespace Raffica.Write.Commands;

/// <summary>The ids of the commands of Write (the ids are the ones the command search and the field codes use).</summary>
internal static class WriteCommandIds
{
    public const string Open = "file.open";
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
}
