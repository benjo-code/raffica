namespace Raffica.Pdf.Commands;

/// <summary>The ids of the commands of the catalog (they are also the ids of the ribbon commands).</summary>
internal static class PdfCommandIds
{
    public const string Open = "file.open";
    public const string Print = "file.print";
    public const string CloseTab = "file.close";
    public const string Settings = "file.settings";
    public const string Search = "find.search";
    public const string SearchNext = "find.next";
    public const string SearchPrevious = "find.previous";
    public const string GoToPage = "goto.page";
    public const string PreviousPage = "pages.previous";
    public const string NextPage = "pages.next";
    public const string FirstPage = "pages.first";
    public const string LastPage = "pages.last";
    public const string NextTab = "tabs.next";
    public const string PreviousTab = "tabs.previous";
    public const string SelectAll = "text.selectall";
    public const string Copy = "text.copy";
    public const string Help = "help.shortcuts";
    public const string ZoomIn = "zoom.in";
    public const string ZoomOut = "zoom.out";
    public const string FitWidth = "zoom.fitwidth";
    public const string FitPage = "zoom.fitpage";
    public const string ActualSize = "zoom.actual";
    public const string Continuous = "layout.continuous";
    public const string SinglePage = "layout.single";
    public const string TwoPages = "layout.two";
    public const string TwoPagesCover = "layout.twocover";
    public const string DarkPage = "page.dark";
    public const string PagesPane = "panes.pages";
    public const string OutlinePane = "panes.outline";
}
