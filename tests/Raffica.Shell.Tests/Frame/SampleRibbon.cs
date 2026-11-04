using Raffica.Shell.Frame;

namespace Raffica.Shell.Tests.Frame;

/// <summary>A Write-like ribbon of about 40 commands for the ribbon tests (icons are the ones that exist in the icon set).</summary>
internal static class SampleRibbon
{
    private const CompactPlacement Overflow = CompactPlacement.Overflow;
    private const CommandSize Large = CommandSize.Large;

    public static RibbonDefinition Create()
    {
        var home = new RibbonTabDefinition("Home", "H",
        [
            new RibbonGroupDefinition("History", "1",
            [
                Cmd("undo", "Undo", "Undo", "U", iconOnly: true, shortcut: "Ctrl+Z", description: "Reverse the last change."),
                Cmd("redo", "Redo", "Redo", "RE", iconOnly: true, shortcut: "Ctrl+Y", description: "Repeat the change you undid."),
            ]),
            new RibbonGroupDefinition("Clipboard", "2",
            [
                Cmd("paste", "Paste", "ChevronDown", "V", Large, shortcut: "Ctrl+V", description: "Paste what you copied.", items:
                [
                    Cmd("paste.keep", "Keep source formatting", "Check", "K"),
                    Cmd("paste.text", "Keep text only", "AlignLeft", "T"),
                ]),
                Cmd("cut", "Cut", "Minus", "X", shortcut: "Ctrl+X"),
                Cmd("copy", "Copy", "Plus", "CO", shortcut: "Ctrl+C"),
                Cmd("painter", "Format painter", "Search", "FP", compact: Overflow),
            ]),
            new RibbonGroupDefinition("Font", "3",
            [
                Cmd("bold", "Bold", "Bold", "B", iconOnly: true, shortcut: "Ctrl+B"),
                Cmd("italic", "Italic", "Italic", "I", iconOnly: true, shortcut: "Ctrl+I"),
                Cmd("underline", "Underline", "Underline", "N", iconOnly: true, shortcut: "Ctrl+U"),
                Cmd("strike", "Strikethrough", "Minus", "ST", compact: Overflow),
                Cmd("sup", "Superscript", "ChevronRight", "SU", compact: Overflow),
                Cmd("sub", "Subscript", "ChevronDown", "SB", compact: Overflow),
            ]),
            new RibbonGroupDefinition("Paragraph", "4",
            [
                Cmd("left", "Align left", "AlignLeft", "AL", iconOnly: true),
                Cmd("center", "Center", "AlignCenter", "AC", iconOnly: true),
                Cmd("right", "Align right", "AlignRight", "AR", iconOnly: true),
                Cmd("bullets", "Bullets", "Plus", "LB", compact: Overflow),
                Cmd("spacing", "Line spacing", "Info", "LS", compact: Overflow),
            ]),
            new RibbonGroupDefinition("Find", "5",
            [
                Cmd("find", "Find", "Search", "FD", Large, shortcut: "Ctrl+F", description: "Search the document."),
                Cmd("replace", "Replace", "Search", "RP", compact: Overflow),
            ]),
            new RibbonGroupDefinition("Clear", "6",
            [
                Cmd("clearfmt", "Clear formatting", "X", "CF", Large),
                Cmd("deletepage", "Delete page", "OctagonAlert", "DP", compact: Overflow),
            ], Destructive: true),
        ]);

        var insert = new RibbonTabDefinition("Insert", "N",
        [
            new RibbonGroupDefinition("Pages", "1",
            [
                Cmd("blank", "Blank page", "Plus", "BP", Large),
                Cmd("cover", "Cover page", "Info", "CP", compact: Overflow),
                Cmd("break", "Page break", "Minus", "PB", compact: Overflow),
            ]),
            new RibbonGroupDefinition("Illustrations", "2",
            [
                Cmd("picture", "Picture", "Plus", "PI", Large),
                Cmd("shape", "Shape", "TriangleAlert", "SH"),
                Cmd("chart", "Chart", "Info", "CH"),
            ]),
            new RibbonGroupDefinition("Links", "3",
            [
                Cmd("link", "Link", "ChevronRight", "LK", Large, shortcut: "Ctrl+K"),
                Cmd("bookmark", "Bookmark", "Check", "BM", compact: Overflow),
            ]),
        ]);

        var review = new RibbonTabDefinition("Review", "R",
        [
            new RibbonGroupDefinition("Proofing", "1",
            [
                Cmd("spelling", "Spelling", "Check", "SP", Large),
                Cmd("count", "Word count", "Info", "WC", compact: Overflow),
            ]),
            new RibbonGroupDefinition("Comments", "2",
            [
                Cmd("newcomment", "New comment", "Plus", "NC", Large),
                Cmd("deletecomment", "Delete comment", "X", "DC", compact: Overflow),
            ]),
        ]);

        var view = new RibbonTabDefinition("View", "W",
        [
            new RibbonGroupDefinition("Zoom", "1",
            [
                Cmd("zoomin", "Zoom in", "Plus", "ZI", iconOnly: true),
                Cmd("zoomout", "Zoom out", "Minus", "ZO", iconOnly: true),
                Cmd("fit", "Fit page", "Search", "FT", compact: Overflow),
            ]),
            new RibbonGroupDefinition("Show", "2",
            [
                Cmd("ruler", "Ruler", "AlignLeft", "RU"),
                Cmd("grid", "Gridlines", "AlignCenter", "GR"),
                Cmd("nav", "Navigation pane", "ChevronRight", "NP"),
            ]),
        ]);

        var definition = new RibbonDefinition([home, insert, review, view]);
        definition.Validate();
        return definition;
    }

    public static IEnumerable<CommandDefinition> AllCommands(RibbonTabDefinition tab) =>
        tab.Groups.SelectMany(g => g.Commands).SelectMany(Flatten);

    private static CommandDefinition Cmd(
        string id,
        string name,
        string icon,
        string code,
        CommandSize size = CommandSize.Small,
        CompactPlacement compact = CompactPlacement.Inline,
        bool iconOnly = false,
        string? shortcut = null,
        string? description = null,
        IReadOnlyList<CommandDefinition>? items = null) =>
        new(id, name, "Raffica.Icon." + icon, code, null, size, compact, iconOnly, shortcut, description, items);

    private static IEnumerable<CommandDefinition> Flatten(CommandDefinition command) =>
        command.Items is null ? [command] : [command, .. command.Items.SelectMany(Flatten)];
}
