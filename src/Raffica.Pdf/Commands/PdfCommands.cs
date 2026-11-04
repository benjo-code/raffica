namespace Raffica.Pdf.Commands;

/// <summary>
/// What the commands do, one entry per command of <see cref="PdfCommandCatalog"/>. The ribbon is built from the catalog and this record
/// (see <see cref="PdfRibbon"/>); an entry that is null leaves its command without an action, and a command without an action is disabled
/// until the feature behind it exists.
/// </summary>
internal sealed record PdfCommands(
    Action? Open = null,
    Action? Print = null,
    Action? CloseTab = null,
    Action? Settings = null,
    Action? Search = null,
    Action? SearchNext = null,
    Action? SearchPrevious = null,
    Action? GoToPage = null,
    Action? PreviousPage = null,
    Action? NextPage = null,
    Action? FirstPage = null,
    Action? LastPage = null,
    Action? NextTab = null,
    Action? PreviousTab = null,
    Action? SelectAll = null,
    Action? Copy = null,
    Action? Help = null,
    Action? ZoomIn = null,
    Action? ZoomOut = null,
    Action? FitWidth = null,
    Action? FitPage = null,
    Action? ActualSize = null,
    Action? Continuous = null,
    Action? SinglePage = null,
    Action? TwoPages = null,
    Action? TwoPagesCover = null,
    Action? DarkPage = null,
    Action? PagesPane = null,
    Action? OutlinePane = null)
{
    /// <summary>The action behind a command id of <see cref="PdfCommandIds"/>, or null when there is none.</summary>
    public Action? RunOf(string id) => id switch
    {
        PdfCommandIds.Open => Open,
        PdfCommandIds.Print => Print,
        PdfCommandIds.CloseTab => CloseTab,
        PdfCommandIds.Settings => Settings,
        PdfCommandIds.Search => Search,
        PdfCommandIds.SearchNext => SearchNext,
        PdfCommandIds.SearchPrevious => SearchPrevious,
        PdfCommandIds.GoToPage => GoToPage,
        PdfCommandIds.PreviousPage => PreviousPage,
        PdfCommandIds.NextPage => NextPage,
        PdfCommandIds.FirstPage => FirstPage,
        PdfCommandIds.LastPage => LastPage,
        PdfCommandIds.NextTab => NextTab,
        PdfCommandIds.PreviousTab => PreviousTab,
        PdfCommandIds.SelectAll => SelectAll,
        PdfCommandIds.Copy => Copy,
        PdfCommandIds.Help => Help,
        PdfCommandIds.ZoomIn => ZoomIn,
        PdfCommandIds.ZoomOut => ZoomOut,
        PdfCommandIds.FitWidth => FitWidth,
        PdfCommandIds.FitPage => FitPage,
        PdfCommandIds.ActualSize => ActualSize,
        PdfCommandIds.Continuous => Continuous,
        PdfCommandIds.SinglePage => SinglePage,
        PdfCommandIds.TwoPages => TwoPages,
        PdfCommandIds.TwoPagesCover => TwoPagesCover,
        PdfCommandIds.DarkPage => DarkPage,
        PdfCommandIds.PagesPane => PagesPane,
        PdfCommandIds.OutlinePane => OutlinePane,
        _ => null,
    };
}
