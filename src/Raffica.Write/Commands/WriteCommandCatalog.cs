using Avalonia.Input;
using Raffica.Shell.Frame;

namespace Raffica.Write.Commands;

/*
 * Icons: command id -> Lucide icon (tag 1.63.0, key Raffica.Icon.<PascalCase>, the set of the shell). One glyph per meaning; WriteCommandTests.EveryCommandHasItsOwnIcon pins it.
 *   file.open -> folder-open
 *   file.save -> save
 *   file.saveAs -> save-all
 *   file.close -> x
 *   app.settings -> settings
 *   app.search -> search
 *   view.zoomIn -> zoom-in
 *   view.zoomOut -> zoom-out
 *   view.actualSize -> percent
 *   view.fitWidth -> move-horizontal
 *   view.fitPage -> maximize
 *   view.page -> file
 *   view.writing -> pen-line
 *   view.outline -> list-tree
 *   edit.undo -> undo-2
 *   edit.redo -> redo-2
 *   edit.cut -> scissors
 *   edit.copy -> copy
 *   edit.paste -> clipboard-paste
 *   edit.selectAll -> text-select
 *   format.bold -> bold, format.italic -> italic, format.underline -> underline, format.strike -> strikethrough
 *   format.superscript -> superscript, format.subscript -> subscript, format.smallCaps -> case-sensitive, format.allCaps -> case-upper
 *   format.alignLeft -> text-align-start (the key AlignLeft), format.alignCenter -> text-align-center, format.alignRight -> text-align-end, format.justify -> text-align-justify
 *   format.indentMore -> list-indent-increase, format.indentLess -> list-indent-decrease
 *   format.keepNext -> link, format.keepLines -> pin, format.pageBreakBefore -> file-up, format.widowControl -> list-end
 *   format.clearDirect -> remove-formatting, format.updateStyle -> refresh-cw
 *   format.font -> type, format.size -> a-large-small, format.sizeUp -> a-arrow-up, format.sizeDown -> a-arrow-down, format.color -> palette, format.highlight -> highlighter
 *   format.spaceBefore -> arrow-up-to-line, format.spaceAfter -> arrow-down-to-line, format.lineSpacing -> list-chevrons-up-down
 *   format.paragraphStyle -> pilcrow, format.characterStyle -> baseline
 *   view.formatting -> panel-right
 */

/// <summary>Which modifier a shortcut starts with: the command key of the platform (Ctrl, Cmd on macOS) or Alt.</summary>
internal enum WriteShortcutBase
{
    /// <summary>The modifier the platform uses for everyday commands: Ctrl, Cmd on macOS.</summary>
    Command,

    /// <summary>The Alt key (the command search, which the frame handles itself).</summary>
    Alt,
}

/// <summary>
/// A shortcut of the catalog: its main key (and the keys that mean the same, such as the number pad), the modifier and the text the user reads. Keys are matched
/// as keys, not as text, so a German keyboard (Ctrl and the plus key) works like an English one.
/// </summary>
/// <param name="Key">The main key.</param>
/// <param name="Token">How the key is written in the shortcut text (<c>O</c>, <c>+</c>).</param>
/// <param name="Base">The modifier the shortcut starts with.</param>
/// <param name="AnyShift">Whether Shift is accepted but not required (Ctrl and the plus key is Ctrl+Shift+= on an English keyboard).</param>
/// <param name="Alternates">Keys that do the same as <paramref name="Key"/> (number pad keys).</param>
/// <param name="AllowsRepeat">Whether holding the key repeats the command (zoom); every other shortcut runs once per press.</param>
/// <param name="Shift">Whether Shift is part of the shortcut (redo on macOS is Cmd+Shift+Z).</param>
internal sealed record WriteShortcut(Key Key, string Token, WriteShortcutBase Base = WriteShortcutBase.Command, bool AnyShift = false, Key[]? Alternates = null, bool AllowsRepeat = false, bool Shift = false)
{
    private const KeyModifiers Relevant = KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta;

    /// <summary>The main key and its alternates.</summary>
    public IEnumerable<Key> Keys => Alternates is null ? [Key] : [Key, .. Alternates];

    /// <summary>The shortcut written with English names, the way the shell reads them (<c>Ctrl+O</c>, <c>Meta+O</c> on macOS, <c>Alt+Q</c>).</summary>
    public string Text(bool macOs) => (Base == WriteShortcutBase.Alt ? "Alt" : macOs ? "Meta" : "Ctrl") + (Shift ? "+Shift" : string.Empty) + "+" + Token;

    /// <summary>Whether a key press is this shortcut. <paramref name="command"/> is the command modifier of the platform; any modifier the shortcut does not name makes it no match.</summary>
    public bool Matches(Key key, KeyModifiers modifiers, KeyModifiers command)
    {
        if (!Keys.Contains(key))
        {
            return false;
        }

        var actual = modifiers & Relevant;
        if (AnyShift)
        {
            actual &= ~KeyModifiers.Shift;
        }

        return actual == ((Base == WriteShortcutBase.Alt ? KeyModifiers.Alt : command) | (Shift ? KeyModifiers.Shift : KeyModifiers.None));
    }
}

/// <summary>A ribbon tab of the catalog.</summary>
internal sealed class CatalogTab(string nameKey, string letter)
{
    private readonly List<CatalogGroup> _groups = [];

    public string NameKey { get; } = nameKey;

    /// <summary>The field-code letter of the tab.</summary>
    public string Letter { get; } = letter;

    public string Name => WriteStrings.Get(NameKey);

    public IReadOnlyList<CatalogGroup> Groups => _groups;

    internal CatalogGroup Group(string labelKey, string digit)
    {
        var group = new CatalogGroup(this, labelKey, digit);
        _groups.Add(group);
        return group;
    }
}

/// <summary>A group of commands in a tab; its digit is the first character of the field codes of its commands.</summary>
internal sealed class CatalogGroup(CatalogTab tab, string labelKey, string digit)
{
    private readonly List<CatalogCommand> _commands = [];

    public CatalogTab Tab { get; } = tab;

    public string LabelKey { get; } = labelKey;

    public string Digit { get; } = digit;

    public string Label => WriteStrings.Get(LabelKey);

    public IReadOnlyList<CatalogCommand> Commands => _commands;

    internal CatalogGroup Add(string id, string nameKey, string icon, string code, WriteShortcut? shortcut = null, CommandSize size = CommandSize.Small, bool toggle = false, WriteShortcut? macShortcut = null, bool describe = false)
    {
        _commands.Add(new CatalogCommand(this, id, nameKey, icon, code, shortcut, size, toggle, macShortcut, describe ? "Desc" + nameKey[3..] : null, WriteCommandIds.TakesPayload(id)));
        return this;
    }
}

/// <summary>One command: id, name (a string key), icon, field code and shortcut, in one place.</summary>
internal sealed class CatalogCommand(CatalogGroup group, string id, string nameKey, string icon, string fieldCode, WriteShortcut? shortcut, CommandSize size, bool toggle, WriteShortcut? macShortcut, string? descriptionKey, bool takesPayload)
{
    public CatalogGroup Group { get; } = group;

    public CatalogTab Tab => Group.Tab;

    public string Id { get; } = id;

    public string NameKey { get; } = nameKey;

    public string Name => WriteStrings.Get(NameKey);

    /// <summary>The key of the long wording of the command (<c>Desc&lt;Name&gt;</c>, shown as its description), or null when it has none.</summary>
    public string? DescriptionKey { get; } = descriptionKey;

    /// <summary>The long wording of the command in the UI language, or null.</summary>
    public string? Description => DescriptionKey is null ? null : WriteStrings.Get(DescriptionKey);

    /// <summary>The name of the icon in the icon set of the shell (<c>Raffica.Icon.&lt;name&gt;</c>).</summary>
    public string Icon { get; } = icon;

    /// <summary>The field code inside its tab: the group digit plus the position (<c>11</c> is the first command of the first group).</summary>
    public string FieldCode { get; } = fieldCode;

    /// <summary>The shortcut of this system.</summary>
    public WriteShortcut? Shortcut => ShortcutFor(OperatingSystem.IsMacOS());

    /// <summary>The shortcut on a system: the macOS one when the command has its own there, else the default.</summary>
    public WriteShortcut? ShortcutFor(bool macOs) => macOs && macShortcut is not null ? macShortcut : shortcut;

    public CommandSize Size { get; } = size;

    /// <summary>Whether the command is a switch or a choice that shows its state (pressed, and the UIA Toggle state): the zoom fit modes, the two views and the outline.</summary>
    public bool Toggle { get; } = toggle;

    /// <summary>
    /// Whether the command is a value command (font, size, colour, spacing, style): the picker that chooses the value passes it to the action as the payload of
    /// <c>WriteWindow.RunCommand(string, object?)</c>; a command without a payload is run by its id alone.
    /// </summary>
    public bool TakesPayload { get; } = takesPayload;
}

/// <summary>
/// The single source of every command of Write: id, name, icon, field code, shortcut and where it sits in the ribbon. The ribbon (<see cref="WriteRibbon"/>), the key
/// handling of the window, the macOS menu and the tests read this list.
/// </summary>
internal static class WriteCommandCatalog
{
    static WriteCommandCatalog()
    {
        var home = new CatalogTab("TabHome", "H");
        home.Group("GroupFile", "1")
            .Add(WriteCommandIds.New, "CmdNew", "FilePlus", "16", Cmd(Key.N, "N"), describe: true)
            .Add(WriteCommandIds.Open, "CmdOpen", "FolderOpen", "11", Cmd(Key.O, "O"), CommandSize.Large)
            .Add(WriteCommandIds.Save, "CmdSave", "Save", "14", Cmd(Key.S, "S"))
            .Add(WriteCommandIds.SaveAs, "CmdSaveAs", "SaveAll", "15", new WriteShortcut(Key.S, "S", Shift: true))
            .Add(WriteCommandIds.CloseTab, "CmdCloseTab", "X", "12", Cmd(Key.W, "W"))
            .Add(WriteCommandIds.Settings, "CmdSettings", "Settings", "13", Cmd(Key.OemComma, ","));
        home.Group("GroupUndo", "3")
            .Add(WriteCommandIds.Undo, "CmdUndo", "Undo2", "31", Cmd(Key.Z, "Z"), describe: true)
            .Add(WriteCommandIds.Redo, "CmdRedo", "Redo2", "32", Cmd(Key.Y, "Y"), macShortcut: new WriteShortcut(Key.Z, "Z", Shift: true), describe: true);
        home.Group("GroupClipboard", "4")
            .Add(WriteCommandIds.Paste, "CmdPaste", "ClipboardPaste", "41", Cmd(Key.V, "V"), CommandSize.Large, describe: true)
            .Add(WriteCommandIds.Cut, "CmdCut", "Scissors", "42", Cmd(Key.X, "X"), describe: true)
            .Add(WriteCommandIds.Copy, "CmdCopy", "Copy", "43", Cmd(Key.C, "C"), describe: true)
            .Add(WriteCommandIds.SelectAll, "CmdSelectAll", "TextSelect", "44", Cmd(Key.A, "A"), describe: true);
        home.Group("GroupFont", "5")
            .Add(WriteCommandIds.Font, "CmdFormatFont", "Type", "51")
            .Add(WriteCommandIds.Size, "CmdFormatSize", "ALargeSmall", "52")
            .Add(WriteCommandIds.SizeUp, "CmdFormatSizeUp", "AArrowUp", "53")
            .Add(WriteCommandIds.SizeDown, "CmdFormatSizeDown", "AArrowDown", "54")
            .Add(WriteCommandIds.Bold, "CmdFormatBold", "Bold", "55", Cmd(Key.B, "B"), toggle: true)
            .Add(WriteCommandIds.Italic, "CmdFormatItalic", "Italic", "56", Cmd(Key.I, "I"), toggle: true)
            .Add(WriteCommandIds.Underline, "CmdFormatUnderline", "Underline", "57", Cmd(Key.U, "U"), toggle: true)
            .Add(WriteCommandIds.Strike, "CmdFormatStrike", "Strikethrough", "58", null, toggle: true)
            .Add(WriteCommandIds.Superscript, "CmdFormatSuperscript", "Superscript", "59", null, toggle: true)
            .Add(WriteCommandIds.Subscript, "CmdFormatSubscript", "Subscript", "5A", null, toggle: true)
            .Add(WriteCommandIds.SmallCaps, "CmdFormatSmallCaps", "CaseSensitive", "5B", null, toggle: true)
            .Add(WriteCommandIds.AllCaps, "CmdFormatAllCaps", "CaseUpper", "5C", null, toggle: true)
            .Add(WriteCommandIds.Color, "CmdFormatColor", "Palette", "5D")
            .Add(WriteCommandIds.Highlight, "CmdFormatHighlight", "Highlighter", "5E")
            .Add(WriteCommandIds.ClearDirect, "CmdFormatClearDirect", "RemoveFormatting", "5F");
        home.Group("GroupParagraph", "6")
            .Add(WriteCommandIds.AlignLeft, "CmdFormatAlignLeft", "AlignLeft", "61", Cmd(Key.L, "L"), toggle: true)
            .Add(WriteCommandIds.AlignCenter, "CmdFormatAlignCenter", "AlignCenter", "62", Cmd(Key.E, "E"), toggle: true)
            .Add(WriteCommandIds.AlignRight, "CmdFormatAlignRight", "AlignRight", "63", Cmd(Key.R, "R"), toggle: true)
            .Add(WriteCommandIds.Justify, "CmdFormatJustify", "AlignJustify", "64", Cmd(Key.J, "J"), toggle: true)
            .Add(WriteCommandIds.IndentLess, "CmdFormatIndentLess", "ListIndentDecrease", "65")
            .Add(WriteCommandIds.IndentMore, "CmdFormatIndentMore", "ListIndentIncrease", "66")
            .Add(WriteCommandIds.LineSpacing, "CmdFormatLineSpacing", "ListChevronsUpDown", "67")
            .Add(WriteCommandIds.SpaceBefore, "CmdFormatSpaceBefore", "ArrowUpToLine", "68")
            .Add(WriteCommandIds.SpaceAfter, "CmdFormatSpaceAfter", "ArrowDownToLine", "69")
            .Add(WriteCommandIds.KeepNext, "CmdFormatKeepNext", "Link", "6A", null, toggle: true)
            .Add(WriteCommandIds.KeepLines, "CmdFormatKeepLines", "Pin", "6B", null, toggle: true)
            .Add(WriteCommandIds.PageBreakBefore, "CmdFormatPageBreakBefore", "FileUp", "6C", null, toggle: true)
            .Add(WriteCommandIds.WidowControl, "CmdFormatWidowControl", "ListEnd", "6D", null, toggle: true);
        home.Group("GroupStyles", "7")
            .Add(WriteCommandIds.ParagraphStyle, "CmdFormatParagraphStyle", "Pilcrow", "71")
            .Add(WriteCommandIds.CharacterStyle, "CmdFormatCharacterStyle", "Baseline", "72")
            .Add(WriteCommandIds.UpdateStyle, "CmdFormatUpdateStyle", "RefreshCw", "73");
        home.Group("GroupSearch", "2")
            .Add(WriteCommandIds.Search, "CmdSearch", "Search", "21", new WriteShortcut(Key.Q, "Q", WriteShortcutBase.Alt), CommandSize.Large, macShortcut: Cmd(Key.K, "K"));

        var view = new CatalogTab("TabView", "V");
        view.Group("GroupZoom", "1")
            .Add(WriteCommandIds.ZoomIn, "CmdZoomIn", "ZoomIn", "11", Cmd(Key.OemPlus, "+", anyShift: true, alternates: [Key.Add], repeat: true))
            .Add(WriteCommandIds.ZoomOut, "CmdZoomOut", "ZoomOut", "12", Cmd(Key.OemMinus, "-", anyShift: true, alternates: [Key.Subtract], repeat: true))
            .Add(WriteCommandIds.ActualSize, "CmdActualSize", "Percent", "13", Cmd(Key.D0, "0", alternates: [Key.NumPad0]), toggle: true)
            .Add(WriteCommandIds.FitWidth, "CmdFitWidth", "MoveHorizontal", "14", Cmd(Key.D1, "1", alternates: [Key.NumPad1]), toggle: true)
            .Add(WriteCommandIds.FitPage, "CmdFitPage", "Maximize", "15", Cmd(Key.D2, "2", alternates: [Key.NumPad2]), toggle: true);
        view.Group("GroupViews", "2")
            .Add(WriteCommandIds.PageView, "CmdPageView", "File", "21", toggle: true)
            .Add(WriteCommandIds.WritingView, "CmdWritingView", "PenLine", "22", toggle: true);
        view.Group("GroupPanes", "3")
            .Add(WriteCommandIds.Outline, "CmdOutline", "ListTree", "31", toggle: true)
            .Add(WriteCommandIds.FormattingPane, "CmdFormatting", "PanelRight", "32", toggle: true);

        Tabs = [home, view];
        Commands = Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).ToList();
    }

    /// <summary>The tabs of the ribbon, in order.</summary>
    public static IReadOnlyList<CatalogTab> Tabs { get; }

    /// <summary>Every command, in ribbon order.</summary>
    public static IReadOnlyList<CatalogCommand> Commands { get; }

    /// <summary>The command a key press stands for, or null. The command search is the frame's (it handles its own key) and the keys of undo, redo, the clipboard, select all and the formatting commands are the page view's (a text box has its own Ctrl+C), so none of them is ever returned.</summary>
    public static CatalogCommand? Find(Key key, KeyModifiers modifiers, KeyModifiers command, bool? macOs = null)
    {
        var mac = macOs ?? OperatingSystem.IsMacOS();
        foreach (var candidate in Commands)
        {
            if (candidate.Id != WriteCommandIds.Search && !WriteCommandIds.IsEditing(candidate.Id) && !WriteCommandIds.IsFormatting(candidate.Id) && candidate.ShortcutFor(mac) is { } shortcut && shortcut.Matches(key, modifiers, command))
            {
                return candidate;
            }
        }

        return null;
    }

    private static WriteShortcut Cmd(Key key, string token, bool anyShift = false, Key[]? alternates = null, bool repeat = false) =>
        new(key, token, WriteShortcutBase.Command, anyShift, alternates, repeat);
}
