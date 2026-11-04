using Raffica.Shell.Frame;

namespace Raffica.Gallery.FrameDemo;

/// <summary>
/// The sample Write ribbon of the frame demo: a File tab (the host shows the start view for it) and the tabs Home, Insert, Review and
/// View with about fifty commands. The names come from the gallery strings in the running language, so the German layout test sees real
/// German lengths. Every command does something small and harmless: it reports "Ran: name" through <c>ran</c>.
/// </summary>
public static class DemoRibbon
{
    /// <summary>The field-code letter of the File tab.</summary>
    public const string FileLetter = "F";

    private const CompactPlacement Overflow = CompactPlacement.Overflow;
    private const CommandSize Large = CommandSize.Large;

    /// <param name="ran">Called with the name of a command that ran.</param>
    /// <param name="toggleCompact">Called by the "Compact command bar" command of the View tab.</param>
    public static RibbonDefinition Create(Action<string> ran, Action toggleCompact)
    {
        ArgumentNullException.ThrowIfNull(ran);
        ArgumentNullException.ThrowIfNull(toggleCompact);

        CommandDefinition Cmd(
            string id,
            string name,
            string icon,
            string code,
            CommandSize size = CommandSize.Small,
            CompactPlacement compact = CompactPlacement.Inline,
            bool iconOnly = false,
            string? shortcut = null,
            string? description = null,
            IReadOnlyList<CommandDefinition>? items = null,
            Action? run = null) =>
            new(id, name, "Raffica.Icon." + icon, code, items is null ? run ?? (() => ran(name)) : null, size, compact, iconOnly, shortcut, description, items);

        var file = new RibbonTabDefinition(GalleryStrings.TabFile, FileLetter,
        [
            new RibbonGroupDefinition(GalleryStrings.GroupDocument, "1",
            [
                Cmd("file.new", GalleryStrings.CmdNew, "Plus", "N", Large, shortcut: "Ctrl+N"),
                Cmd("file.open", GalleryStrings.CmdOpen, "ChevronRight", "O", shortcut: "Ctrl+O"),
                Cmd("file.save", GalleryStrings.CmdSave, "Check", "S", shortcut: "Ctrl+S"),
                Cmd("file.saveas", GalleryStrings.CmdSaveAs, "ChevronDown", "SA", compact: Overflow),
            ]),
        ]);

        var home = new RibbonTabDefinition(GalleryStrings.TabHome, "H",
        [
            new RibbonGroupDefinition(GalleryStrings.GroupHistory, "1",
            [
                Cmd("undo", GalleryStrings.CmdUndo, "Undo", "U", iconOnly: true, shortcut: "Ctrl+Z", description: GalleryStrings.CmdUndoDescription),
                Cmd("redo", GalleryStrings.CmdRedo, "Redo", "R", iconOnly: true, shortcut: "Ctrl+Y", description: GalleryStrings.CmdRedoDescription),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupClipboard, "2",
            [
                Cmd("paste", GalleryStrings.CmdPaste, "ChevronDown", "V", Large, shortcut: "Ctrl+V", description: GalleryStrings.CmdPasteDescription, items:
                [
                    Cmd("paste.keep", GalleryStrings.CmdPasteKeep, "Check", "K"),
                    Cmd("paste.text", GalleryStrings.CmdPasteText, "AlignLeft", "T"),
                ]),
                Cmd("cut", GalleryStrings.CmdCut, "Minus", "X", shortcut: "Ctrl+X"),
                Cmd("copy", GalleryStrings.CmdCopy, "Plus", "C", shortcut: "Ctrl+C"),
                Cmd("painter", GalleryStrings.CmdPainter, "Search", "FP", compact: Overflow),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupFont, "3",
            [
                Cmd("bold", GalleryStrings.CmdBold, "Bold", "B", iconOnly: true, shortcut: "Ctrl+B"),
                Cmd("italic", GalleryStrings.CmdItalic, "Italic", "I", iconOnly: true, shortcut: "Ctrl+I"),
                Cmd("underline", GalleryStrings.CmdUnderline, "Underline", "N", iconOnly: true, shortcut: "Ctrl+U"),
                Cmd("strike", GalleryStrings.CmdStrike, "Minus", "S", compact: Overflow),
                Cmd("sup", GalleryStrings.CmdSuperscript, "ChevronRight", "SU", compact: Overflow),
                Cmd("sub", GalleryStrings.CmdSubscript, "ChevronDown", "SB", compact: Overflow),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupParagraph, "4",
            [
                Cmd("left", GalleryStrings.CmdAlignLeft, "AlignLeft", "AL", iconOnly: true),
                Cmd("center", GalleryStrings.CmdAlignCenter, "AlignCenter", "AC", iconOnly: true),
                Cmd("right", GalleryStrings.CmdAlignRight, "AlignRight", "AR", iconOnly: true),
                Cmd("bullets", GalleryStrings.CmdBullets, "Plus", "BU", compact: Overflow),
                Cmd("spacing", GalleryStrings.CmdSpacing, "Info", "LS", compact: Overflow),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupFind, "5",
            [
                Cmd("find", GalleryStrings.CmdFind, "Search", "F", Large, shortcut: "Ctrl+F", description: GalleryStrings.CmdFindDescription),
                Cmd("replace", GalleryStrings.CmdReplace, "Search", "RP", compact: Overflow),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupClear, "6",
            [
                Cmd("clearfmt", GalleryStrings.CmdClearFormatting, "X", "CF", Large),
                Cmd("deletepage", GalleryStrings.CmdDeletePage, "OctagonAlert", "DP", compact: Overflow),
            ], Destructive: true),
        ]);

        var insert = new RibbonTabDefinition(GalleryStrings.TabInsert, "N",
        [
            new RibbonGroupDefinition(GalleryStrings.GroupPages, "1",
            [
                Cmd("blank", GalleryStrings.CmdBlankPage, "Plus", "BP", Large),
                Cmd("cover", GalleryStrings.CmdCoverPage, "Info", "CP", compact: Overflow),
                Cmd("break", GalleryStrings.CmdPageBreak, "Minus", "PB", compact: Overflow),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupIllustrations, "2",
            [
                Cmd("picture", GalleryStrings.CmdPicture, "Plus", "PI", Large),
                Cmd("shape", GalleryStrings.CmdShape, "TriangleAlert", "SH"),
                Cmd("chart", GalleryStrings.CmdChart, "Info", "CH"),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupLinks, "3",
            [
                Cmd("link", GalleryStrings.CmdLink, "ChevronRight", "LK", Large, shortcut: "Ctrl+K"),
                Cmd("bookmark", GalleryStrings.CmdBookmark, "Check", "BM", compact: Overflow),
                Cmd("contents", GalleryStrings.CmdContents, "AlignLeft", "TC", compact: Overflow),
            ]),
        ]);

        var review = new RibbonTabDefinition(GalleryStrings.TabReview, "R",
        [
            new RibbonGroupDefinition(GalleryStrings.GroupProofing, "1",
            [
                Cmd("spelling", GalleryStrings.CmdSpelling, "Check", "SP", Large),
                Cmd("count", GalleryStrings.CmdWordCount, "Info", "WC", compact: Overflow),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupChanges, "2",
            [
                Cmd("track", GalleryStrings.CmdTrack, "Underline", "TR", Large),
                Cmd("acceptall", GalleryStrings.CmdAcceptAll, "Check", "AA"),
                Cmd("rejectall", GalleryStrings.CmdRejectAll, "X", "RA", compact: Overflow),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupComments, "3",
            [
                Cmd("newcomment", GalleryStrings.CmdNewComment, "Plus", "NC", Large),
                Cmd("deletecomment", GalleryStrings.CmdDeleteComment, "X", "DC", compact: Overflow),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupFinalize, "4",
            [
                Cmd("apply", GalleryStrings.CmdApply, "OctagonAlert", "AP", Large),
                Cmd("personal", GalleryStrings.CmdRemovePersonal, "X", "RD", compact: Overflow),
            ], Destructive: true),
        ]);

        var view = new RibbonTabDefinition(GalleryStrings.TabView, "W",
        [
            new RibbonGroupDefinition(GalleryStrings.GroupZoom, "1",
            [
                Cmd("zoomin", GalleryStrings.CmdZoomIn, "Plus", "ZI", iconOnly: true),
                Cmd("zoomout", GalleryStrings.CmdZoomOut, "Minus", "ZO", iconOnly: true),
                Cmd("fit", GalleryStrings.CmdFitPage, "Search", "FT", compact: Overflow),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupShow, "2",
            [
                Cmd("ruler", GalleryStrings.CmdRuler, "AlignLeft", "RU"),
                Cmd("grid", GalleryStrings.CmdGridlines, "AlignCenter", "GR"),
                Cmd("nav", GalleryStrings.CmdNavigationPane, "ChevronRight", "NP"),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupLayout, "3",
            [
                Cmd("compact", GalleryStrings.CmdCompact, "Minus", "CB", Large, run: toggleCompact),
                Cmd("fullscreen", GalleryStrings.CmdFullScreen, "Plus", "FS", compact: Overflow),
            ]),
        ]);

        var definition = new RibbonDefinition([file, home, insert, review, view]);
        definition.Validate();
        return definition;
    }
}
