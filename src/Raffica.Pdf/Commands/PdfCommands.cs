namespace Raffica.Pdf.Commands;

/// <summary>
/// What the commands of the ribbon do, one entry per command. The ribbon is built from this record (see <see cref="PdfRibbon"/>); an entry
/// that is null leaves its command without an action, so it does nothing until the feature behind it exists.
/// </summary>
internal sealed record PdfCommands(
    Action? Open = null,
    Action? Print = null,
    Action? Search = null,
    Action? SearchNext = null,
    Action? SearchPrevious = null,
    Action? GoToPage = null,
    Action? PreviousPage = null,
    Action? NextPage = null,
    Action? FirstPage = null,
    Action? LastPage = null,
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
    Action? OutlinePane = null);
