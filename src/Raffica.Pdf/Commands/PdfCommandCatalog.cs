using Avalonia.Input;
using Raffica.Pdf.Dialogs;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Commands;

/// <summary>A ribbon tab of the catalog.</summary>
internal sealed class CatalogTab(string nameKey, string letter)
{
    private readonly List<CatalogGroup> _groups = [];

    public string NameKey { get; } = nameKey;

    /// <summary>The field-code letter of the tab.</summary>
    public string Letter { get; } = letter;

    public string Name => PdfStrings.Get(NameKey);

    public IReadOnlyList<CatalogGroup> Groups => _groups;

    internal CatalogGroup Group(string labelKey, string digit, bool destructive = false)
    {
        var group = new CatalogGroup(this, labelKey, digit, destructive);
        _groups.Add(group);
        return group;
    }
}

/// <summary>A group of commands in a tab; its digit is the first character of the field codes of its commands.</summary>
internal sealed class CatalogGroup(CatalogTab tab, string labelKey, string digit, bool destructive = false)
{
    private readonly List<CatalogCommand> _commands = [];

    public CatalogTab Tab { get; } = tab;

    public string LabelKey { get; } = labelKey;

    public string Digit { get; } = digit;

    public string Label => PdfStrings.Get(LabelKey);

    /// <summary>Whether the group is set apart at the end of the tab (the commands that remove things).</summary>
    public bool Destructive { get; } = destructive;

    public IReadOnlyList<CatalogCommand> Commands => _commands;

    internal CatalogGroup Add(string id, string nameKey, string icon, string code, PdfShortcut? shortcut = null, CommandSize size = CommandSize.Small)
    {
        _commands.Add(new CatalogCommand(this, id, nameKey, icon, code, shortcut, size));
        return this;
    }
}

/// <summary>One command: id, name (a string key), icon, field code and shortcut, in one place.</summary>
internal sealed class CatalogCommand(CatalogGroup group, string id, string nameKey, string icon, string fieldCode, PdfShortcut? shortcut, CommandSize size)
{
    public CatalogGroup Group { get; } = group;

    public CatalogTab Tab => Group.Tab;

    public string Id { get; } = id;

    public string NameKey { get; } = nameKey;

    public string Name => PdfStrings.Get(NameKey);

    /// <summary>The name of the icon in the icon set of the shell (<c>Raffica.Icon.&lt;name&gt;</c>).</summary>
    public string Icon { get; } = icon;

    /// <summary>The field code inside its tab: the group digit plus the position (<c>11</c> is the first command of the first group).</summary>
    public string FieldCode { get; } = fieldCode;

    public PdfShortcut? Shortcut { get; } = shortcut;

    public CommandSize Size { get; } = size;
}

/// <summary>
/// The single source of every command of the viewer: id, name, icon, field code, shortcut and where it sits in the ribbon. The ribbon
/// (<see cref="PdfRibbon"/>), the key handling of the window, the help card and the tests all read this list. Icons are the closest of
/// the shell's icon set; where none fits, one of the same family stands in.
/// </summary>
internal static class PdfCommandCatalog
{
    static PdfCommandCatalog()
    {
        var home = new CatalogTab("TabHome", "H");
        home.Group("GroupFile", "1")
            .Add(PdfCommandIds.Open, "CmdOpen", "Plus", "11", Cmd(Key.O, "O"), CommandSize.Large)
            .Add(PdfCommandIds.Print, "CmdPrint", "AlignLeft", "12", Cmd(Key.P, "P"))
            .Add(PdfCommandIds.CloseTab, "CmdCloseTab", "X", "13", Cmd(Key.W, "W"))
            .Add(PdfCommandIds.Settings, "CmdSettings", "AlignCenter", "14", Cmd(Key.OemComma, ","))
            .Add(PdfCommandIds.Save, "CmdSave", "Check", "15", Cmd(Key.S, "S"))
            .Add(PdfCommandIds.SaveAs, "CmdSaveAs", "Check", "16", Cmd(Key.S, "S", shift: true));
        home.Group("GroupFind", "2")
            .Add(PdfCommandIds.Search, "CmdSearch", "Search", "21", Cmd(Key.F, "F"), CommandSize.Large)
            .Add(PdfCommandIds.SearchNext, "CmdSearchNext", "Redo", "22", Plain(Key.F3, "F3", repeat: true))
            .Add(PdfCommandIds.SearchPrevious, "CmdSearchPrevious", "Undo", "23", Plain(Key.F3, "F3", shift: true, repeat: true));
        home.Group("GroupGoTo", "3")
            .Add(PdfCommandIds.GoToPage, "CmdGoToPage", "ChevronRight", "31", Cmd(Key.G, "G"), CommandSize.Large);
        home.Group("GroupPages", "4")
            .Add(PdfCommandIds.PreviousPage, "CmdPreviousPage", "Undo", "41")
            .Add(PdfCommandIds.NextPage, "CmdNextPage", "Redo", "42")
            .Add(PdfCommandIds.FirstPage, "CmdFirstPage", "Undo", "43")
            .Add(PdfCommandIds.LastPage, "CmdLastPage", "Redo", "44");
        home.Group("GroupTabs", "5")
            .Add(PdfCommandIds.NextTab, "CmdNextTab", "Redo", "51", CtrlKey(Key.Tab, "Tab"))
            .Add(PdfCommandIds.PreviousTab, "CmdPreviousTab", "Undo", "52", CtrlKey(Key.Tab, "Tab", shift: true));
        home.Group("GroupText", "6")
            .Add(PdfCommandIds.SelectAll, "CmdSelectAll", "AlignLeft", "61", Cmd(Key.A, "A"))
            .Add(PdfCommandIds.Copy, "CmdCopy", "Plus", "62", Cmd(Key.C, "C"));
        home.Group("GroupHelp", "7")
            .Add(PdfCommandIds.Help, "CmdHelp", "Info", "71", Plain(Key.F1, "F1"));
        home.Group("GroupHistory", "8")
            .Add(PdfCommandIds.Undo, "CmdUndo", "Undo", "81", Cmd(Key.Z, "Z", repeat: true))
            .Add(PdfCommandIds.Redo, "CmdRedo", "Redo", "82", Cmd(Key.Z, "Z", shift: true, repeat: true, also: Cmd(Key.Y, "Y", repeat: true)));

        // Letter A for the tab (the pane codes 6 and 7 belong to the panes and Q to the title bar search, none is a tab letter). The icon set
        // has no marker, pen or shape icons yet: the closest of the set stand in, as in the other tabs, and the real ones come with the icon pass.
        var annotate = new CatalogTab("TabAnnotate", "A");
        annotate.Group("GroupMarkup", "1")
            .Add(PdfCommandIds.Highlight, "CmdHighlight", "Bold", "11", Cmd(Key.H, "H", shift: true))
            .Add(PdfCommandIds.Underline, "CmdUnderline", "Underline", "12", Cmd(Key.U, "U", shift: true))
            .Add(PdfCommandIds.StrikeOut, "CmdStrikeOut", "Minus", "13", Cmd(Key.X, "X", shift: true));
        annotate.Group("GroupDraw", "2")
            .Add(PdfCommandIds.Freehand, "CmdFreehand", "Plus", "21")
            .Add(PdfCommandIds.Rectangle, "CmdRectangle", "AlignLeft", "22")
            .Add(PdfCommandIds.Ellipse, "CmdEllipse", "AlignCenter", "23")
            .Add(PdfCommandIds.Line, "CmdLine", "Minus", "24")
            .Add(PdfCommandIds.Arrow, "CmdArrow", "ChevronRight", "25")
            .Add(PdfCommandIds.TextBox, "CmdTextBox", "Italic", "26");
        annotate.Group("GroupEdit", "3")
            .Add(PdfCommandIds.AnnotateMode, "CmdAnnotateMode", "Plus", "31", Cmd(Key.K, "K", shift: true), CommandSize.Large)
            .Add(PdfCommandIds.SelectMark, "CmdSelectMark", "Search", "32", Cmd(Key.M, "M", shift: true))
            .Add(PdfCommandIds.AnnotateDone, "CmdAnnotateDone", "Check", "33")
            .Add(PdfCommandIds.MarksPane, "CmdMarksPane", "AlignRight", "34");
        // The Sign group sits before the destructive one so that group stays last; its digit is 5 because 4 is taken by Delete. The icon is a
        // stand-in (the set has no pen or signature icon yet), like the other annotate icons.
        annotate.Group("GroupSign", "5")
            .Add(PdfCommandIds.Sign, "CmdSign", "Italic", "51", size: CommandSize.Large);
        annotate.Group("GroupDelete", "4", destructive: true)
            .Add(PdfCommandIds.DeleteMark, "CmdDeleteMark", "X", "41");

        // Letter F for the tab (free: the frame keeps Q, 6 and 7). The four commands only exist for a document with fillable fields; the keys of the
        // form (Tab, Shift+Tab, Enter, Space, Esc) belong to the page view and are no catalog shortcuts. The icons are stand-ins as in the other tabs.
        var forms = new CatalogTab("TabForms", "F");
        forms.Group("GroupFill", "1")
            .Add(PdfCommandIds.FormsNext, "CmdFormsNext", "Redo", "11")
            .Add(PdfCommandIds.FormsPrevious, "CmdFormsPrevious", "Undo", "12")
            .Add(PdfCommandIds.FormsNextRequired, "CmdFormsNextRequired", "TriangleAlert", "13")
            .Add(PdfCommandIds.FormsHighlight, "CmdFormsHighlight", "Bold", "14");

        var view = new CatalogTab("TabView", "V");
        view.Group("GroupZoom", "1")
            .Add(PdfCommandIds.ZoomIn, "CmdZoomIn", "Plus", "11", Cmd(Key.OemPlus, "+", anyShift: true, alternates: [Key.Add], repeat: true))
            .Add(PdfCommandIds.ZoomOut, "CmdZoomOut", "Minus", "12", Cmd(Key.OemMinus, "-", anyShift: true, alternates: [Key.Subtract], repeat: true))
            .Add(PdfCommandIds.FitWidth, "CmdFitWidth", "AlignLeft", "13", Cmd(Key.D1, "1", alternates: [Key.NumPad1]))
            .Add(PdfCommandIds.FitPage, "CmdFitPage", "Search", "14", Cmd(Key.D2, "2", alternates: [Key.NumPad2]))
            .Add(PdfCommandIds.ActualSize, "CmdActualSize", "Check", "15", Cmd(Key.D0, "0", alternates: [Key.NumPad0]));
        view.Group("GroupLayout", "2")
            .Add(PdfCommandIds.Continuous, "CmdContinuous", "ChevronDown", "21")
            .Add(PdfCommandIds.SinglePage, "CmdSinglePage", "AlignCenter", "22")
            .Add(PdfCommandIds.TwoPages, "CmdTwoPages", "AlignRight", "23")
            .Add(PdfCommandIds.TwoPagesCover, "CmdTwoPagesCover", "AlignLeft", "24");
        view.Group("GroupPage", "3")
            .Add(PdfCommandIds.DarkPage, "CmdDarkPage", "Check", "31", Cmd(Key.D, "D", shift: true), CommandSize.Large);
        view.Group("GroupPanes", "4")
            .Add(PdfCommandIds.PagesPane, "CmdPagesPane", "ChevronRight", "41", Cmd(Key.D1, "1", shift: true))
            .Add(PdfCommandIds.OutlinePane, "CmdOutlinePane", "AlignLeft", "42", Cmd(Key.D2, "2", shift: true));

        Tabs = [home, annotate, forms, view];
        Commands = Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).ToList();
    }

    /// <summary>The tabs of the ribbon, in order.</summary>
    public static IReadOnlyList<CatalogTab> Tabs { get; }

    /// <summary>Every command, in ribbon order.</summary>
    public static IReadOnlyList<CatalogCommand> Commands { get; }

    /// <summary>The command a key press stands for, or null. <paramref name="command"/> is the command modifier of the platform.</summary>
    public static CatalogCommand? Find(Key key, KeyModifiers modifiers, KeyModifiers command)
    {
        foreach (var candidate in Commands)
        {
            if (candidate.Shortcut is { } shortcut && shortcut.Matches(key, modifiers, command))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>The lines of the help card: every command that has a shortcut, in ribbon order, grouped by its ribbon group, the shortcut written for the platform and the UI language.</summary>
    public static IReadOnlyList<HelpEntry> HelpEntries() =>
        Commands.Where(c => c.Shortcut is not null)
            .Select(c => new HelpEntry(c.Group.Label, c.Name, KeyNames.Format(c.Shortcut!.Text(OperatingSystem.IsMacOS()), PdfStrings.Culture)))
            .ToList();

    private static PdfShortcut Cmd(Key key, string token, bool shift = false, bool anyShift = false, Key[]? alternates = null, bool repeat = false, PdfShortcut? also = null) =>
        new(key, token, ShortcutBase.Command, shift, anyShift, alternates, repeat, also);

    private static PdfShortcut Plain(Key key, string token, bool shift = false, bool repeat = false) =>
        new(key, token, ShortcutBase.None, shift, AllowsRepeat: repeat);

    private static PdfShortcut CtrlKey(Key key, string token, bool shift = false) => new(key, token, ShortcutBase.Control, shift);
}
