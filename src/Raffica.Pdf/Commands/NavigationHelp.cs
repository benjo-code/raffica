using System.Globalization;
using Raffica.Pdf.Dialogs;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Commands;

/// <summary>
/// The fixed "Navigation" section of the help card: the keys that are not commands of the catalog (the search field, the pane ring, the field codes,
/// the keys of the page organizer, the forms and the text fields) and so would be in no list otherwise. Every key here has a handler in the code
/// (<c>FrameKeys</c>, <c>PaneFocus</c>, <c>FieldCodeInput</c>, <c>SearchBar</c>, <c>OrganizeList</c>, <c>FormKeys</c>, <c>TextBoxEditor</c>, <c>MarksPane</c>,
/// <c>RunFieldEditor</c>); the keys are written by <see cref="KeyNames"/>, so the platform and the language decide how they read (Cmd on macOS, Strg in German).
/// </summary>
internal static class NavigationHelp
{
    /// <summary>The lines of the section, in the order of the help card.</summary>
    public static IReadOnlyList<HelpEntry> Entries() => Entries(OperatingSystem.IsMacOS());

    /// <summary>The lines written for macOS (Cmd) or for the other systems (Ctrl).</summary>
    internal static IReadOnlyList<HelpEntry> Entries(bool macOs)
    {
        var ui = PdfStrings.Culture;
        var group = PdfStrings.HelpGroupNavigation;
        var command = macOs ? "Meta" : "Ctrl";

        HelpEntry Line(string name, params string[] keys) => new(group, name, string.Join(" / ", keys.Select(k => KeyNames.Format(k, ui, macOs))));

        var lines = new List<HelpEntry>
        {
            Line(PdfStrings.HelpNavSearch, command + "+K"),
            Line(PdfStrings.HelpNavPanes, "F6", "Shift+F6"),
            Line(PdfStrings.CmdRedo, command + "+Y"),
            Line(PdfStrings.HelpNavSearchNext, "Enter"),
            Line(PdfStrings.HelpNavSearchPrevious, "Shift+Enter"),
            Line(PdfStrings.HelpNavOrganizeMove, "Alt+↑", "Alt+↓"),
            Line(PdfStrings.HelpNavOrganizeEnds, "Alt+" + PdfStrings.HelpKeyHome, "Alt+" + PdfStrings.HelpKeyEnd),
            Line(PdfStrings.HelpNavOrganizeRotate, "[", "]"),
            Line(PdfStrings.HelpNavOrganizeDuplicate, command + "+D"),
            Line(PdfStrings.HelpNavOrganizeSelectAll, command + "+A"),
            Line(PdfStrings.HelpNavOrganizeDelete, "Delete"),
            Line(PdfStrings.HelpNavFormWalk, "Tab", "Shift+Tab"),
            Line(PdfStrings.HelpNavFormFill, "Enter", PdfStrings.HelpKeySpace),
            Line(PdfStrings.HelpNavFormLeave, "Esc"),
            Line(PdfStrings.HelpNavTextFinish, command + "+Enter"),
            Line(PdfStrings.HelpNavWholeLine, command + "+Shift+G"),
        };

        // The Alt key tips do not exist on macOS (spec 5.4), so the help does not list them.
        if (KeyTips.Offered(macOs))
        {
            lines.Insert(2, Line(PdfStrings.HelpNavFieldCodes, "Alt"));
        }

        return lines;
    }

    /// <summary>The same lines for a language other than the current one (tests).</summary>
    internal static IReadOnlyList<HelpEntry> EntriesIn(CultureInfo culture, bool macOs = false)
    {
        var before = PdfStrings.Culture;
        PdfStrings.Culture = culture;
        try
        {
            return Entries(macOs);
        }
        finally
        {
            PdfStrings.Culture = before;
        }
    }
}
