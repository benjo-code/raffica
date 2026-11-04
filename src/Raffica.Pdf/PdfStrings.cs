using System.Globalization;
using Raffica.Shell.Localization;

namespace Raffica.Pdf;

/// <summary>The texts of the PDF app (English source, German translation); the shell controls bring their own.</summary>
internal static class PdfStrings
{
    private static readonly StringTable Table = new("Raffica.Pdf.Strings", typeof(PdfStrings).Assembly);

    /// <summary>The UI language; set once at startup, a change applies after a restart.</summary>
    public static CultureInfo Culture
    {
        get => Table.Culture;
        set => Table.Culture = value;
    }

    /// <summary>The text of a key; throws <see cref="InvalidOperationException"/> for a missing key.</summary>
    public static string Get(string key) => Table.Get(key);

    public static string WindowTitle => Table.Get("WindowTitle");
    public static string AppMarkName => Table.Get("AppMarkName");
    public static string PanePages => Table.Get("PanePages");
    public static string TabHome => Table.Get("TabHome");
    public static string TabView => Table.Get("TabView");
    public static string GroupFile => Table.Get("GroupFile");
    public static string GroupFind => Table.Get("GroupFind");
    public static string GroupGoTo => Table.Get("GroupGoTo");
    public static string GroupPages => Table.Get("GroupPages");
    public static string GroupZoom => Table.Get("GroupZoom");
    public static string GroupLayout => Table.Get("GroupLayout");
    public static string GroupPage => Table.Get("GroupPage");
    public static string GroupPanes => Table.Get("GroupPanes");
    public static string CmdOpen => Table.Get("CmdOpen");
    public static string CmdPrint => Table.Get("CmdPrint");
    public static string CmdSearch => Table.Get("CmdSearch");
    public static string CmdSearchNext => Table.Get("CmdSearchNext");
    public static string CmdSearchPrevious => Table.Get("CmdSearchPrevious");
    public static string CmdGoToPage => Table.Get("CmdGoToPage");
    public static string CmdPreviousPage => Table.Get("CmdPreviousPage");
    public static string CmdNextPage => Table.Get("CmdNextPage");
    public static string CmdFirstPage => Table.Get("CmdFirstPage");
    public static string CmdLastPage => Table.Get("CmdLastPage");
    public static string CmdZoomIn => Table.Get("CmdZoomIn");
    public static string CmdZoomOut => Table.Get("CmdZoomOut");
    public static string CmdFitWidth => Table.Get("CmdFitWidth");
    public static string CmdFitPage => Table.Get("CmdFitPage");
    public static string CmdActualSize => Table.Get("CmdActualSize");
    public static string CmdContinuous => Table.Get("CmdContinuous");
    public static string CmdSinglePage => Table.Get("CmdSinglePage");
    public static string CmdTwoPages => Table.Get("CmdTwoPages");
    public static string CmdTwoPagesCover => Table.Get("CmdTwoPagesCover");
    public static string CmdDarkPage => Table.Get("CmdDarkPage");
    public static string CmdPagesPane => Table.Get("CmdPagesPane");
    public static string CmdOutlinePane => Table.Get("CmdOutlinePane");
}
