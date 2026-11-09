using Avalonia.Input;
using Raffica.Pdf.Dialogs;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;

namespace Raffica.Pdf.Commands;

/*
 * Icons: command id -> Lucide icon (tag 1.63.0, key Raffica.Icon.<PascalCase>). One glyph per meaning; only the same action in two tabs
 * (Undo and Redo of Home and Pages) shares one, CommandIconTests pins it.
 *   file.open -> folder-open
 *   file.print -> printer
 *   file.close -> x
 *   file.settings -> settings
 *   file.save -> save
 *   file.saveAs -> save-all
 *   find.search -> search
 *   find.next -> arrow-down
 *   find.previous -> arrow-up
 *   goto.page -> corner-down-right
 *   pages.previous -> chevron-left
 *   pages.next -> chevron-right
 *   pages.first -> chevrons-left
 *   pages.last -> chevrons-right
 *   tabs.next -> arrow-right
 *   tabs.previous -> arrow-left
 *   text.selectall -> text-select
 *   text.copy -> copy
 *   help.shortcuts -> circle-help
 *   doc.info -> info
 *   app.update -> refresh-cw
 *   edit.undo -> undo
 *   edit.redo -> redo
 *   annotate.highlight -> highlighter
 *   annotate.underline -> underline
 *   annotate.strikeOut -> strikethrough
 *   annotate.freehand -> pencil
 *   annotate.rectangle -> square
 *   annotate.ellipse -> circle
 *   annotate.line -> slash
 *   annotate.arrow -> move-up-right
 *   annotate.textBox -> type
 *   annotate.mode -> pen-line
 *   annotate.select -> mouse-pointer
 *   annotate.done -> check
 *   annotate.marks -> panel-right
 *   annotate.sign -> signature
 *   annotate.delete -> trash-2
 *   forms.next -> corner-right-down
 *   forms.previous -> corner-left-up
 *   forms.nextRequired -> triangle-alert
 *   forms.highlight -> brush
 *   pages.organize -> layout-grid
 *   pages.rotateLeft -> rotate-ccw
 *   pages.rotateRight -> rotate-cw
 *   pages.duplicate -> copy-plus
 *   pages.blank -> file-plus
 *   pages.insertFile -> file-input
 *   pages.moveUp -> move-up
 *   pages.moveDown -> move-down
 *   pages.undo -> undo
 *   pages.redo -> redo
 *   pages.apply -> list-checks
 *   pages.discard -> circle-x
 *   pages.extract -> file-output
 *   pages.split -> scissors
 *   pages.exportImages -> image-down
 *   pages.delete -> file-x
 *   redact.mode -> eye-off
 *   redact.markSelection -> square-dashed
 *   redact.find -> scan-search
 *   redact.rasterPage -> image
 *   redact.apply -> eraser
 *   redact.discard -> undo-2
 *   redact.deleteMarks -> square-x
 *   text.edit -> square-pen
 *   text.editDone -> square-check
 *   ocr.run -> scan-text
 *   ocr.languages -> languages
 *   ocr.cancel -> octagon-x
 *   zoom.in -> zoom-in
 *   zoom.out -> zoom-out
 *   zoom.fitwidth -> move-horizontal
 *   zoom.fitpage -> maximize
 *   zoom.actual -> percent
 *   layout.continuous -> gallery-vertical
 *   layout.single -> file
 *   layout.two -> book-open
 *   layout.twocover -> book-copy
 *   page.dark -> moon
 *   panes.pages -> panel-left
 *   panes.outline -> list-tree
 */

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

    internal CatalogGroup Add(string id, string nameKey, string icon, string code, PdfShortcut? shortcut = null, CommandSize size = CommandSize.Small, bool toggle = false)
    {
        _commands.Add(new CatalogCommand(this, id, nameKey, icon, code, shortcut, size, toggle));
        return this;
    }
}

/// <summary>One command: id, name (a string key), icon, field code and shortcut, in one place.</summary>
internal sealed class CatalogCommand(CatalogGroup group, string id, string nameKey, string icon, string fieldCode, PdfShortcut? shortcut, CommandSize size, bool toggle)
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

    /// <summary>
    /// Whether the command is a switch or a choice that shows its state (pressed and the UIA Toggle state): the zoom fit modes, the page
    /// layouts, the dark page and the panes. The window tells which of them is on (<see cref="PdfRibbon.Create"/>).
    /// </summary>
    public bool Toggle { get; } = toggle;
}

/// <summary>
/// The single source of every command of the viewer: id, name, icon, field code, shortcut and where it sits in the ribbon. The ribbon
/// (<see cref="PdfRibbon"/>), the key handling of the window, the help card and the tests all read this list. Every command has an icon of its own meaning
/// (table at the top of the file).
/// </summary>
internal static class PdfCommandCatalog
{
    static PdfCommandCatalog()
    {
        var home = new CatalogTab("TabHome", "H");
        home.Group("GroupFile", "1")
            .Add(PdfCommandIds.Open, "CmdOpen", "FolderOpen", "11", Cmd(Key.O, "O"), CommandSize.Large)
            .Add(PdfCommandIds.Print, "CmdPrint", "Printer", "12", Cmd(Key.P, "P"))
            .Add(PdfCommandIds.CloseTab, "CmdCloseTab", "X", "13", Cmd(Key.W, "W"))
            .Add(PdfCommandIds.Settings, "CmdSettings", "Settings", "14", Cmd(Key.OemComma, ","))
            .Add(PdfCommandIds.Save, "CmdSave", "Save", "15", Cmd(Key.S, "S"))
            .Add(PdfCommandIds.SaveAs, "CmdSaveAs", "SaveAll", "16", Cmd(Key.S, "S", shift: true));
        home.Group("GroupFind", "2")
            .Add(PdfCommandIds.Search, "CmdSearch", "Search", "21", Cmd(Key.F, "F"), CommandSize.Large)
            .Add(PdfCommandIds.SearchNext, "CmdSearchNext", "ArrowDown", "22", Plain(Key.F3, "F3", repeat: true))
            .Add(PdfCommandIds.SearchPrevious, "CmdSearchPrevious", "ArrowUp", "23", Plain(Key.F3, "F3", shift: true, repeat: true));
        home.Group("GroupGoTo", "3")
            .Add(PdfCommandIds.GoToPage, "CmdGoToPage", "CornerDownRight", "31", Cmd(Key.G, "G"), CommandSize.Large);
        home.Group("GroupPages", "4")
            .Add(PdfCommandIds.PreviousPage, "CmdPreviousPage", "ChevronLeft", "41")
            .Add(PdfCommandIds.NextPage, "CmdNextPage", "ChevronRight", "42")
            .Add(PdfCommandIds.FirstPage, "CmdFirstPage", "ChevronsLeft", "43")
            .Add(PdfCommandIds.LastPage, "CmdLastPage", "ChevronsRight", "44");
        home.Group("GroupTabs", "5")
            .Add(PdfCommandIds.NextTab, "CmdNextTab", "ArrowRight", "51", CtrlKey(Key.Tab, "Tab"))
            .Add(PdfCommandIds.PreviousTab, "CmdPreviousTab", "ArrowLeft", "52", CtrlKey(Key.Tab, "Tab", shift: true));
        home.Group("GroupText", "6")
            .Add(PdfCommandIds.SelectAll, "CmdSelectAll", "TextSelect", "61", Cmd(Key.A, "A"))
            .Add(PdfCommandIds.Copy, "CmdCopy", "Copy", "62", Cmd(Key.C, "C"));
        home.Group("GroupHelp", "7")
            .Add(PdfCommandIds.Help, "CmdHelp", "CircleHelp", "71", Plain(Key.F1, "F1"))
            .Add(PdfCommandIds.DocumentInfo, "CmdDocumentInfo", "Info", "72")
            .Add(PdfCommandIds.Update, "CmdCheckForUpdates", "RefreshCw", "73");
        home.Group("GroupHistory", "8")
            .Add(PdfCommandIds.Undo, "CmdUndo", "Undo", "81", Cmd(Key.Z, "Z", repeat: true))
            .Add(PdfCommandIds.Redo, "CmdRedo", "Redo", "82", Cmd(Key.Z, "Z", shift: true, repeat: true, also: Cmd(Key.Y, "Y", repeat: true)));

        // Letter A for the tab (the pane codes 6 and 7 belong to the panes and Q to the title bar search, none is a tab letter).
        var annotate = new CatalogTab("TabAnnotate", "A");
        annotate.Group("GroupMarkup", "1")
            .Add(PdfCommandIds.Highlight, "CmdHighlight", "Highlighter", "11", Cmd(Key.H, "H", shift: true))
            .Add(PdfCommandIds.Underline, "CmdUnderline", "Underline", "12", Cmd(Key.U, "U", shift: true))
            .Add(PdfCommandIds.StrikeOut, "CmdStrikeOut", "Strikethrough", "13", Cmd(Key.X, "X", shift: true));
        annotate.Group("GroupDraw", "2")
            .Add(PdfCommandIds.Freehand, "CmdFreehand", "Pencil", "21")
            .Add(PdfCommandIds.Rectangle, "CmdRectangle", "Square", "22")
            .Add(PdfCommandIds.Ellipse, "CmdEllipse", "Circle", "23")
            .Add(PdfCommandIds.Line, "CmdLine", "Slash", "24")
            .Add(PdfCommandIds.Arrow, "CmdArrow", "MoveUpRight", "25")
            .Add(PdfCommandIds.TextBox, "CmdTextBox", "Type", "26");
        annotate.Group("GroupEdit", "3")
            .Add(PdfCommandIds.AnnotateMode, "CmdAnnotateMode", "PenLine", "31", Cmd(Key.K, "K", shift: true), CommandSize.Large)
            .Add(PdfCommandIds.SelectMark, "CmdSelectMark", "MousePointer", "32", Cmd(Key.M, "M", shift: true))
            .Add(PdfCommandIds.AnnotateDone, "CmdAnnotateDone", "Check", "33")
            .Add(PdfCommandIds.MarksPane, "CmdMarksPane", "PanelRight", "34");
        // The Sign group sits before the destructive one so that group stays last; its digit is 5 because 4 is taken by Delete.
        annotate.Group("GroupSign", "5")
            .Add(PdfCommandIds.Sign, "CmdSign", "Signature", "51", size: CommandSize.Large);
        annotate.Group("GroupDelete", "4", destructive: true)
            .Add(PdfCommandIds.DeleteMark, "CmdDeleteMark", "Trash2", "41");

        // Letter F for the tab (free: the frame keeps Q, 6 and 7). The four commands only exist for a document with fillable fields; the keys of the
        // form (Tab, Shift+Tab, Enter, Space, Esc) belong to the page view and are no catalog shortcuts.
        var forms = new CatalogTab("TabForms", "F");
        forms.Group("GroupFill", "1")
            .Add(PdfCommandIds.FormsNext, "CmdFormsNext", "CornerRightDown", "11")
            .Add(PdfCommandIds.FormsPrevious, "CmdFormsPrevious", "CornerLeftUp", "12")
            .Add(PdfCommandIds.FormsNextRequired, "CmdFormsNextRequired", "TriangleAlert", "13")
            .Add(PdfCommandIds.FormsHighlight, "CmdFormsHighlight", "Brush", "14");

        // Letter P for the tab (free: the frame keeps Q, 6 and 7). No command has a shortcut: the keys of the mode belong to the list of the pages pane
        // (Alt+Up, Delete, [ and ] and so on), and Ctrl+Z and Ctrl+Y are the Home tab's, which go to the plan while the mode is on. The Output group
        // (digit 5) sits before the destructive one (digit 6) so that stays last.
        var pages = new CatalogTab("TabPages", "P");
        pages.Group("GroupOrganize", "1")
            .Add(PdfCommandIds.Organize, "CmdOrganize", "LayoutGrid", "11", size: CommandSize.Large);
        pages.Group("GroupEdit", "2")
            .Add(PdfCommandIds.RotateLeft, "CmdRotateLeft", "RotateCcw", "21")
            .Add(PdfCommandIds.RotateRight, "CmdRotateRight", "RotateCw", "22")
            .Add(PdfCommandIds.DuplicatePages, "CmdDuplicatePages", "CopyPlus", "23")
            .Add(PdfCommandIds.BlankPage, "CmdBlankPage", "FilePlus", "24")
            .Add(PdfCommandIds.InsertFile, "CmdInsertFile", "FileInput", "25")
            .Add(PdfCommandIds.MovePagesUp, "CmdMovePagesUp", "MoveUp", "26")
            .Add(PdfCommandIds.MovePagesDown, "CmdMovePagesDown", "MoveDown", "27");
        pages.Group("GroupHistory", "3")
            .Add(PdfCommandIds.PagesUndo, "CmdPagesUndo", "Undo", "31")
            .Add(PdfCommandIds.PagesRedo, "CmdPagesRedo", "Redo", "32");
        pages.Group("GroupApply", "4")
            .Add(PdfCommandIds.ApplyPages, "CmdApplyPages", "ListChecks", "41", size: CommandSize.Large)
            .Add(PdfCommandIds.DiscardPages, "CmdDiscardPages", "CircleX", "42");
        pages.Group("GroupOutput", "5")
            .Add(PdfCommandIds.ExtractPages, "CmdExtractPages", "FileOutput", "51")
            .Add(PdfCommandIds.SplitDocument, "CmdSplitDocument", "Scissors", "52")
            .Add(PdfCommandIds.ExportImages, "CmdExportImages", "ImageDown", "53");
        pages.Group("GroupDelete", "6", destructive: true)
            .Add(PdfCommandIds.DeletePages, "CmdDeletePages", "FileX", "61");

        // Letter R for the tab (free: the frame keeps Q, 6 and 7). Only the mode command is usable before the mode is on; the rest follow it.
        // The Delete group (digit 5) holds the one command that removes marks and stays last.
        var redact = new CatalogTab("TabRedact", "R");
        redact.Group("GroupMark", "1")
            .Add(PdfCommandIds.RedactMode, "CmdRedactMode", "EyeOff", "11", Cmd(Key.P, "P", shift: true), CommandSize.Large)
            .Add(PdfCommandIds.RedactMarkSelection, "CmdRedactMarkSelection", "SquareDashed", "12");
        redact.Group("GroupFind", "2")
            .Add(PdfCommandIds.RedactFind, "CmdRedactFind", "ScanSearch", "21");
        redact.Group("GroupLook", "3")
            .Add(PdfCommandIds.RedactRasterPage, "CmdRedactRasterPage", "Image", "31");
        redact.Group("GroupApply", "4")
            .Add(PdfCommandIds.RedactApply, "CmdRedactApply", "Eraser", "41", size: CommandSize.Large)
            .Add(PdfCommandIds.RedactDiscard, "CmdRedactDiscard", "Undo2", "42");
        redact.Group("GroupDelete", "5", destructive: true)
            .Add(PdfCommandIds.RedactDeleteMarks, "CmdRedactDeleteMarks", "SquareX", "51");

        // Letter E for the tab. Editing the text of a page and recognizing text.
        var edit = new CatalogTab("TabEdit", "E");
        edit.Group("GroupText", "1")
            .Add(PdfCommandIds.TextEdit, "CmdTextEdit", "SquarePen", "11", Cmd(Key.W, "W", shift: true), CommandSize.Large)
            .Add(PdfCommandIds.TextEditDone, "CmdTextEditDone", "SquareCheck", "12");
        edit.Group("GroupScan", "2")
            .Add(PdfCommandIds.OcrRun, "CmdOcrRun", "ScanText", "21", Cmd(Key.Q, "Q", shift: true), CommandSize.Large)
            .Add(PdfCommandIds.OcrLanguages, "CmdOcrLanguages", "Languages", "22")
            .Add(PdfCommandIds.OcrCancel, "CmdOcrCancel", "OctagonX", "23");

        var view = new CatalogTab("TabView", "V");
        view.Group("GroupZoom", "1")
            .Add(PdfCommandIds.ZoomIn, "CmdZoomIn", "ZoomIn", "11", Cmd(Key.OemPlus, "+", anyShift: true, alternates: [Key.Add], repeat: true))
            .Add(PdfCommandIds.ZoomOut, "CmdZoomOut", "ZoomOut", "12", Cmd(Key.OemMinus, "-", anyShift: true, alternates: [Key.Subtract], repeat: true))
            .Add(PdfCommandIds.FitWidth, "CmdFitWidth", "MoveHorizontal", "13", Cmd(Key.D1, "1", alternates: [Key.NumPad1]), toggle: true)
            .Add(PdfCommandIds.FitPage, "CmdFitPage", "Maximize", "14", Cmd(Key.D2, "2", alternates: [Key.NumPad2]), toggle: true)
            .Add(PdfCommandIds.ActualSize, "CmdActualSize", "Percent", "15", Cmd(Key.D0, "0", alternates: [Key.NumPad0]), toggle: true);
        view.Group("GroupLayout", "2")
            .Add(PdfCommandIds.Continuous, "CmdContinuous", "GalleryVertical", "21", toggle: true)
            .Add(PdfCommandIds.SinglePage, "CmdSinglePage", "File", "22", toggle: true)
            .Add(PdfCommandIds.TwoPages, "CmdTwoPages", "BookOpen", "23", toggle: true)
            .Add(PdfCommandIds.TwoPagesCover, "CmdTwoPagesCover", "BookCopy", "24", toggle: true);
        view.Group("GroupPage", "3")
            .Add(PdfCommandIds.DarkPage, "CmdDarkPage", "Moon", "31", Cmd(Key.D, "D", shift: true), CommandSize.Large, toggle: true);
        view.Group("GroupPanes", "4")
            .Add(PdfCommandIds.PagesPane, "CmdPagesPane", "PanelLeft", "41", Cmd(Key.D1, "1", shift: true), toggle: true)
            .Add(PdfCommandIds.OutlinePane, "CmdOutlinePane", "ListTree", "42", Cmd(Key.D2, "2", shift: true), toggle: true);

        Tabs = [home, annotate, forms, pages, redact, edit, view];
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
