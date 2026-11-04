using Raffica.Shell.Frame;

namespace Raffica.Gallery.FrameDemo;

/// <summary>
/// The sample Write ribbon of the frame demo: a File tab (the host shows the start view for it) and the tabs Home, Insert, Review and
/// View with about fifty commands. The names come from the gallery strings in the running language, so the German layout test sees real
/// German lengths. Every command does something small and harmless: it reports "Ran: name" through <c>ran</c>. The field codes follow
/// the scheme of the design spec: the digit of the group plus the position in the group (13 is the third command of group 1), so the
/// codes of a tab never begin one another; the items of the paste menu carry the letters K and T.
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
                Cmd("file.new", GalleryStrings.CmdNew, "Plus", "11", Large, shortcut: "Ctrl+N"),
                Cmd("file.open", GalleryStrings.CmdOpen, "ChevronRight", "12", shortcut: "Ctrl+O"),
                Cmd("file.save", GalleryStrings.CmdSave, "Check", "13", shortcut: "Ctrl+S"),
                Cmd("file.saveas", GalleryStrings.CmdSaveAs, "ChevronDown", "14", compact: Overflow),
            ]),
        ]);

        var home = new RibbonTabDefinition(GalleryStrings.TabHome, "H",
        [
            new RibbonGroupDefinition(GalleryStrings.GroupHistory, "1",
            [
                Cmd("undo", GalleryStrings.CmdUndo, "Undo", "11", iconOnly: true, shortcut: "Ctrl+Z", description: GalleryStrings.CmdUndoDescription),
                Cmd("redo", GalleryStrings.CmdRedo, "Redo", "12", iconOnly: true, shortcut: "Ctrl+Y", description: GalleryStrings.CmdRedoDescription),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupClipboard, "2",
            [
                Cmd("paste", GalleryStrings.CmdPaste, "ChevronDown", "21", Large, shortcut: "Ctrl+V", description: GalleryStrings.CmdPasteDescription, items:
                [
                    Cmd("paste.keep", GalleryStrings.CmdPasteKeep, "Check", "K"),
                    Cmd("paste.text", GalleryStrings.CmdPasteText, "AlignLeft", "T"),
                ]),
                Cmd("cut", GalleryStrings.CmdCut, "Minus", "22", shortcut: "Ctrl+X"),
                Cmd("copy", GalleryStrings.CmdCopy, "Plus", "23", shortcut: "Ctrl+C"),
                Cmd("painter", GalleryStrings.CmdPainter, "Search", "24", compact: Overflow),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupFont, "3",
            [
                Cmd("bold", GalleryStrings.CmdBold, "Bold", "31", iconOnly: true, shortcut: "Ctrl+B"),
                Cmd("italic", GalleryStrings.CmdItalic, "Italic", "32", iconOnly: true, shortcut: "Ctrl+I"),
                Cmd("underline", GalleryStrings.CmdUnderline, "Underline", "33", iconOnly: true, shortcut: "Ctrl+U"),
                Cmd("strike", GalleryStrings.CmdStrike, "Minus", "34", compact: Overflow),
                Cmd("sup", GalleryStrings.CmdSuperscript, "ChevronRight", "35", compact: Overflow),
                Cmd("sub", GalleryStrings.CmdSubscript, "ChevronDown", "36", compact: Overflow),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupParagraph, "4",
            [
                Cmd("left", GalleryStrings.CmdAlignLeft, "AlignLeft", "41", iconOnly: true),
                Cmd("center", GalleryStrings.CmdAlignCenter, "AlignCenter", "42", iconOnly: true),
                Cmd("right", GalleryStrings.CmdAlignRight, "AlignRight", "43", iconOnly: true),
                Cmd("bullets", GalleryStrings.CmdBullets, "Plus", "44", compact: Overflow),
                Cmd("spacing", GalleryStrings.CmdSpacing, "Info", "45", compact: Overflow),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupFind, "5",
            [
                Cmd("find", GalleryStrings.CmdFind, "Search", "51", Large, shortcut: "Ctrl+F", description: GalleryStrings.CmdFindDescription),
                Cmd("replace", GalleryStrings.CmdReplace, "Search", "52", compact: Overflow),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupClear, "6",
            [
                Cmd("clearfmt", GalleryStrings.CmdClearFormatting, "X", "61", Large),
                Cmd("deletepage", GalleryStrings.CmdDeletePage, "OctagonAlert", "62", compact: Overflow),
            ], Destructive: true),
        ]);

        var insert = new RibbonTabDefinition(GalleryStrings.TabInsert, "N",
        [
            new RibbonGroupDefinition(GalleryStrings.GroupPages, "1",
            [
                Cmd("blank", GalleryStrings.CmdBlankPage, "Plus", "11", Large),
                Cmd("cover", GalleryStrings.CmdCoverPage, "Info", "12", compact: Overflow),
                Cmd("break", GalleryStrings.CmdPageBreak, "Minus", "13", compact: Overflow),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupIllustrations, "2",
            [
                Cmd("picture", GalleryStrings.CmdPicture, "Plus", "21", Large),
                Cmd("shape", GalleryStrings.CmdShape, "TriangleAlert", "22"),
                Cmd("chart", GalleryStrings.CmdChart, "Info", "23"),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupLinks, "3",
            [
                Cmd("link", GalleryStrings.CmdLink, "ChevronRight", "31", Large, shortcut: "Ctrl+Shift+K"),
                Cmd("bookmark", GalleryStrings.CmdBookmark, "Check", "32", compact: Overflow),
                Cmd("contents", GalleryStrings.CmdContents, "AlignLeft", "33", compact: Overflow),
            ]),
        ]);

        var review = new RibbonTabDefinition(GalleryStrings.TabReview, "R",
        [
            new RibbonGroupDefinition(GalleryStrings.GroupProofing, "1",
            [
                Cmd("spelling", GalleryStrings.CmdSpelling, "Check", "11", Large),
                Cmd("count", GalleryStrings.CmdWordCount, "Info", "12", compact: Overflow),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupChanges, "2",
            [
                Cmd("track", GalleryStrings.CmdTrack, "Underline", "21", Large),
                Cmd("acceptall", GalleryStrings.CmdAcceptAll, "Check", "22"),
                Cmd("rejectall", GalleryStrings.CmdRejectAll, "X", "23", compact: Overflow),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupComments, "3",
            [
                Cmd("newcomment", GalleryStrings.CmdNewComment, "Plus", "31", Large),
                Cmd("deletecomment", GalleryStrings.CmdDeleteComment, "X", "32", compact: Overflow),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupFinalize, "4",
            [
                Cmd("apply", GalleryStrings.CmdApply, "OctagonAlert", "41", Large),
                Cmd("personal", GalleryStrings.CmdRemovePersonal, "X", "42", compact: Overflow),
            ], Destructive: true),
        ]);

        var view = new RibbonTabDefinition(GalleryStrings.TabView, "W",
        [
            new RibbonGroupDefinition(GalleryStrings.GroupZoom, "1",
            [
                Cmd("zoomin", GalleryStrings.CmdZoomIn, "Plus", "11", iconOnly: true),
                Cmd("zoomout", GalleryStrings.CmdZoomOut, "Minus", "12", iconOnly: true),
                Cmd("fit", GalleryStrings.CmdFitPage, "Search", "13", compact: Overflow),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupShow, "2",
            [
                Cmd("ruler", GalleryStrings.CmdRuler, "AlignLeft", "21"),
                Cmd("grid", GalleryStrings.CmdGridlines, "AlignCenter", "22"),
                Cmd("nav", GalleryStrings.CmdNavigationPane, "ChevronRight", "23"),
            ]),
            new RibbonGroupDefinition(GalleryStrings.GroupLayout, "3",
            [
                Cmd("compact", GalleryStrings.CmdCompact, "Minus", "31", Large, run: toggleCompact),
                Cmd("fullscreen", GalleryStrings.CmdFullScreen, "Plus", "32", compact: Overflow),
            ]),
        ]);

        var definition = new RibbonDefinition([file, home, insert, review, view]);
        definition.Validate();
        return definition;
    }
}
