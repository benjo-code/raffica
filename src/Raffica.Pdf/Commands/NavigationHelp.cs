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
    public static IReadOnlyList<HelpEntry> Entries()
    {
        var ui = PdfStrings.Culture;
        var group = PdfStrings.HelpGroupNavigation;
        var command = OperatingSystem.IsMacOS() ? "Meta" : "Ctrl";

        HelpEntry Line(string name, params string[] keys) => new(group, name, string.Join(" / ", keys.Select(k => KeyNames.Format(k, ui))));

        return
        [
            Line(PdfStrings.HelpNavSearch, command + "+K"),
            Line(PdfStrings.HelpNavPanes, "F6", "Shift+F6"),
            Line(PdfStrings.HelpNavFieldCodes, "Alt"),
            Line(PdfStrings.CmdRedo, command + "+Y"),
            Line(PdfStrings.HelpNavSearchNext, "Enter"),
            Line(PdfStrings.HelpNavSearchPrevious, "Shift+Enter"),
            Line(PdfStrings.HelpNavOrganizeMove, "Alt+↑", "Alt+↓"),
            Line(PdfStrings.HelpNavOrganizeEnds, "Alt+" + PdfStrings.HelpKeyHome, "Alt+" + PdfStrings.HelpKeyEnd),
            Line(PdfStrings.HelpNavOrganizeRotate, "[", "]"),
            Line(PdfStrings.HelpNavOrganizeDuplicate, "Ctrl+D"),
            Line(PdfStrings.HelpNavOrganizeSelectAll, "Ctrl+A"),
            Line(PdfStrings.HelpNavOrganizeDelete, "Delete"),
            Line(PdfStrings.HelpNavFormWalk, "Tab", "Shift+Tab"),
            Line(PdfStrings.HelpNavFormFill, "Enter", PdfStrings.HelpKeySpace),
            Line(PdfStrings.HelpNavFormLeave, "Esc"),
            Line(PdfStrings.HelpNavTextFinish, command + "+Enter"),
            Line(PdfStrings.HelpNavWholeLine, command + "+Shift+G"),
        ];
    }

    /// <summary>The same lines for a language other than the current one (tests).</summary>
    internal static IReadOnlyList<HelpEntry> EntriesIn(CultureInfo culture)
    {
        var before = PdfStrings.Culture;
        PdfStrings.Culture = culture;
        try
        {
            return Entries();
        }
        finally
        {
            PdfStrings.Culture = before;
        }
    }
}
