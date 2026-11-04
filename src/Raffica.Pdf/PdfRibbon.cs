using Raffica.Pdf.Commands;
using Raffica.Shell.Frame;

namespace Raffica.Pdf;

/// <summary>
/// The ribbon of the PDF app: the tabs Home (letter H) and View (letter V). The field codes follow the scheme of the frame: the digit of
/// the group plus the position in the group (13 is the third command of group 1), so the codes of a tab never begin one another. A command
/// without an action in <see cref="PdfCommands"/> has no <see cref="CommandDefinition.Run"/> and does nothing.
/// </summary>
internal static class PdfRibbon
{
    public const string HomeLetter = "H";
    public const string ViewLetter = "V";

    public static RibbonDefinition Create(PdfCommands commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        static CommandDefinition Cmd(string id, string name, string icon, string code, Action? run, CommandSize size = CommandSize.Small, bool iconOnly = false) =>
            new(id, name, "Raffica.Icon." + icon, code, run, size, CompactPlacement.Inline, iconOnly);

        var home = new RibbonTabDefinition(PdfStrings.TabHome, HomeLetter,
        [
            new RibbonGroupDefinition(PdfStrings.GroupFile, "1",
            [
                Cmd("file.open", PdfStrings.CmdOpen, "Plus", "11", commands.Open, CommandSize.Large),
                Cmd("file.print", PdfStrings.CmdPrint, "Info", "12", commands.Print),
            ]),
            new RibbonGroupDefinition(PdfStrings.GroupFind, "2",
            [
                Cmd("find.search", PdfStrings.CmdSearch, "Search", "21", commands.Search, CommandSize.Large),
                Cmd("find.next", PdfStrings.CmdSearchNext, "ChevronDown", "22", commands.SearchNext),
                Cmd("find.previous", PdfStrings.CmdSearchPrevious, "ChevronRight", "23", commands.SearchPrevious),
            ]),
            new RibbonGroupDefinition(PdfStrings.GroupGoTo, "3",
            [
                Cmd("goto.page", PdfStrings.CmdGoToPage, "AlignLeft", "31", commands.GoToPage, CommandSize.Large),
            ]),
            new RibbonGroupDefinition(PdfStrings.GroupPages, "4",
            [
                Cmd("pages.previous", PdfStrings.CmdPreviousPage, "ChevronRight", "41", commands.PreviousPage),
                Cmd("pages.next", PdfStrings.CmdNextPage, "ChevronDown", "42", commands.NextPage),
                Cmd("pages.first", PdfStrings.CmdFirstPage, "Minus", "43", commands.FirstPage),
                Cmd("pages.last", PdfStrings.CmdLastPage, "Plus", "44", commands.LastPage),
            ]),
        ]);

        var view = new RibbonTabDefinition(PdfStrings.TabView, ViewLetter,
        [
            new RibbonGroupDefinition(PdfStrings.GroupZoom, "1",
            [
                Cmd("zoom.in", PdfStrings.CmdZoomIn, "Plus", "11", commands.ZoomIn),
                Cmd("zoom.out", PdfStrings.CmdZoomOut, "Minus", "12", commands.ZoomOut),
                Cmd("zoom.fitwidth", PdfStrings.CmdFitWidth, "AlignLeft", "13", commands.FitWidth),
                Cmd("zoom.fitpage", PdfStrings.CmdFitPage, "Search", "14", commands.FitPage),
                Cmd("zoom.actual", PdfStrings.CmdActualSize, "Info", "15", commands.ActualSize),
            ]),
            new RibbonGroupDefinition(PdfStrings.GroupLayout, "2",
            [
                Cmd("layout.continuous", PdfStrings.CmdContinuous, "AlignLeft", "21", commands.Continuous),
                Cmd("layout.single", PdfStrings.CmdSinglePage, "AlignCenter", "22", commands.SinglePage),
                Cmd("layout.two", PdfStrings.CmdTwoPages, "AlignRight", "23", commands.TwoPages),
                Cmd("layout.twocover", PdfStrings.CmdTwoPagesCover, "AlignRight", "24", commands.TwoPagesCover),
            ]),
            new RibbonGroupDefinition(PdfStrings.GroupPage, "3",
            [
                Cmd("page.dark", PdfStrings.CmdDarkPage, "Check", "31", commands.DarkPage, CommandSize.Large),
            ]),
            new RibbonGroupDefinition(PdfStrings.GroupPanes, "4",
            [
                Cmd("panes.pages", PdfStrings.CmdPagesPane, "ChevronRight", "41", commands.PagesPane),
                Cmd("panes.outline", PdfStrings.CmdOutlinePane, "ChevronRight", "42", commands.OutlinePane),
            ]),
        ]);

        var definition = new RibbonDefinition([home, view]);
        definition.Validate();
        return definition;
    }
}
