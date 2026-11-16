using Avalonia.Input;
using Raffica.Write.Commands;
using Raffica.Write.Layout;

namespace Raffica.Write.Editing;

/// <summary>What a key does to the text: the kind of <see cref="EditAction"/>.</summary>
internal enum EditActionKind
{
    /// <summary>A caret move (<see cref="EditAction.Move"/>), extending the selection with <see cref="EditAction.Extend"/>.</summary>
    Move,

    /// <summary>Backspace or Delete (<see cref="EditAction.Unit"/>, <see cref="EditAction.Forward"/>).</summary>
    Delete,

    /// <summary>Enter: replaces the selection with a paragraph break.</summary>
    Enter,

    /// <summary>Tab: a tab character is text in Write.</summary>
    Tab,

    /// <summary>A key that is claimed and does nothing (Shift+Tab in 4.3a: it must not move the focus out of the page).</summary>
    Nothing,

    /// <summary>Select the whole story of the caret.</summary>
    SelectAll,

    /// <summary>A command of the window (<see cref="EditAction.CommandId"/>): undo, redo and the clipboard.</summary>
    Command,
}

/// <summary>One thing a key does; made by <see cref="EditingKeys.For"/>.</summary>
internal readonly record struct EditAction(
    EditActionKind Kind,
    CaretMove Move = default,
    bool Extend = false,
    DeleteUnit Unit = DeleteUnit.Cluster,
    bool Forward = false,
    string? CommandId = null)
{
    /// <summary>Whether a key held down repeats the action (typing, deleting, moving, Enter and Tab do; a one-shot action such as undo, select all or a clipboard command runs once per press).</summary>
    public bool AllowsRepeat => Kind is EditActionKind.Move or EditActionKind.Delete or EditActionKind.Enter or EditActionKind.Tab or EditActionKind.Nothing;

    public static EditAction Moving(CaretMove move, bool extend) => new(EditActionKind.Move, move, extend);

    public static EditAction Deleting(DeleteUnit unit, bool forward) => new(EditActionKind.Delete, Unit: unit, Forward: forward);

    public static EditAction Run(string commandId) => new(EditActionKind.Command, CommandId: commandId);
}

/// <summary>
/// The keys of the text editor: which key, with which modifiers, does what, per system. Text never comes from here: a typed character arrives through the text input of the
/// view (so dead keys, composed characters and AltGr just work) and <see cref="IsTextInput(KeyModifiers, string?, bool)"/> says which key events are such text, so the key
/// handler leaves them alone (a handled key down would suppress the text input).
/// </summary>
/// <remarks>
/// Windows and Linux: Ctrl with Left and Right moves by words, Ctrl with Up and Down by paragraphs, Home and End go to the line ends, Ctrl+Home and Ctrl+End to the document
/// ends, Backspace and Delete with Ctrl take a word, Ctrl+A selects all, Ctrl+Z undoes, Ctrl+Y and Ctrl+Shift+Z redo. macOS: Option with Left and Right moves by words, ⌘ with
/// Left and Right to the line ends, ⌘ with Up and Down to the document ends, Option with Up and Down by paragraphs, Home and End to the document ends, Option with Backspace and
/// Delete takes a word, ⌘A, ⌘Z and ⇧⌘Z. Shift adds the selection to every move. None of them is one of the shortcuts macOS reserves (the Write tests check the table against that
/// list). Undo, redo and the clipboard keys are command ids (<see cref="UndoCommand"/> and the others, defined with the commands of the clipboard and the history) that the window runs.
/// Escape is not here (the view and the window have their own uses), and neither is Alt+Q (the command search).
/// </remarks>
internal static class EditingKeys
{
    /// <summary>The id of the undo command.</summary>
    public const string UndoCommand = "edit.undo";

    /// <summary>The id of the redo command.</summary>
    public const string RedoCommand = "edit.redo";

    /// <summary>The id of the cut command.</summary>
    public const string CutCommand = "edit.cut";

    /// <summary>The id of the copy command.</summary>
    public const string CopyCommand = "edit.copy";

    /// <summary>The id of the paste command.</summary>
    public const string PasteCommand = "edit.paste";

    private const KeyModifiers Chord = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta | KeyModifiers.Shift;

    /// <summary>The action of a key, or null when the key is not an editing key (the window and the view treat it as before).</summary>
    public static EditAction? For(Key key, KeyModifiers modifiers, bool macOs)
    {
        var shift = (modifiers & KeyModifiers.Shift) != 0;
        var ctrl = (modifiers & KeyModifiers.Control) != 0;
        var alt = (modifiers & KeyModifiers.Alt) != 0;
        var meta = (modifiers & KeyModifiers.Meta) != 0;
        if ((modifiers & ~Chord) != 0)
        {
            return null;
        }

        // The command key of the system and the key that makes words: Ctrl and Ctrl on Windows and Linux; ⌘ and Option on macOS. The other one of Ctrl, Alt and Meta is never part of an editing key.
        bool command;
        bool word;
        if (macOs)
        {
            if (ctrl)
            {
                return null;
            }

            (command, word) = (meta, alt);
        }
        else
        {
            if (alt || meta)
            {
                return null;
            }

            (command, word) = (ctrl, ctrl);
        }

        if (macOs && command && word)
        {
            // ⌥⌘ together is no editing key.
            return null;
        }

        switch (key)
        {
            case Key.Left:
                return Arrow(macOs, command, word, shift, CaretMove.Left, CaretMove.WordLeft, CaretMove.LineStart);
            case Key.Right:
                return Arrow(macOs, command, word, shift, CaretMove.Right, CaretMove.WordRight, CaretMove.LineEnd);
            case Key.Up:
                return VerticalArrow(macOs, command, word, shift, CaretMove.Up, CaretMove.ParagraphStart, CaretMove.DocumentStart);
            case Key.Down:
                return VerticalArrow(macOs, command, word, shift, CaretMove.Down, CaretMove.ParagraphEnd, CaretMove.DocumentEnd);
            case Key.Home:
                return Home(macOs, command, shift, CaretMove.LineStart, CaretMove.DocumentStart);
            case Key.End:
                return Home(macOs, command, shift, CaretMove.LineEnd, CaretMove.DocumentEnd);
            case Key.PageUp:
                return command || word ? null : EditAction.Moving(CaretMove.PageUp, shift);
            case Key.PageDown:
                return command || word ? null : EditAction.Moving(CaretMove.PageDown, shift);
            case Key.Back:
                return DeleteKey(macOs, command, word, shift, forward: false);
            case Key.Delete:
                return DeleteKey(macOs, command, word, shift, forward: true);
            case Key.Enter:
                return command || word || shift ? null : new EditAction(EditActionKind.Enter);
            case Key.Tab:
                if (command || word)
                {
                    return null;
                }

                return shift ? new EditAction(EditActionKind.Nothing) : new EditAction(EditActionKind.Tab);
            case Key.A:
                return command && !shift ? new EditAction(EditActionKind.SelectAll) : null;
            case Key.Z:
                return command ? EditAction.Run(shift ? RedoCommand : UndoCommand) : null;
            case Key.Y:
                return !macOs && command && !shift ? EditAction.Run(RedoCommand) : null;
            case Key.C:
                return command && !shift ? EditAction.Run(CopyCommand) : null;
            case Key.X:
                return command && !shift ? EditAction.Run(CutCommand) : null;
            case Key.V:
                return command && !shift ? EditAction.Run(PasteCommand) : null;
            default:
                return null;
        }
    }

    // Left and Right: one character; the word key: words; on macOS ⌘: the line ends (Windows has no such chord: Ctrl is the word key there).
    private static EditAction? Arrow(bool macOs, bool command, bool word, bool shift, CaretMove step, CaretMove words, CaretMove line)
    {
        if (macOs && command)
        {
            return EditAction.Moving(line, shift);
        }

        return EditAction.Moving(word ? words : step, shift);
    }

    // Up and Down: one line; the word key (Ctrl on Windows and Linux, Option on macOS): paragraphs; on macOS ⌘: the document ends.
    private static EditAction? VerticalArrow(bool macOs, bool command, bool word, bool shift, CaretMove step, CaretMove paragraph, CaretMove document)
    {
        if (macOs && command)
        {
            return EditAction.Moving(document, shift);
        }

        return EditAction.Moving(word ? paragraph : step, shift);
    }

    // Home and End: the line ends; with the command key the document ends (Ctrl on Windows and Linux); on macOS the plain keys already go to the document ends.
    private static EditAction? Home(bool macOs, bool command, bool shift, CaretMove line, CaretMove document)
    {
        if (macOs)
        {
            return command ? null : EditAction.Moving(document, shift);
        }

        return EditAction.Moving(command ? document : line, shift);
    }

    // Backspace and Delete: one cluster; with the word key a word. Shift changes nothing. ⌘ has no unit here (a delete to the line start is not implemented).
    private static EditAction? DeleteKey(bool macOs, bool command, bool word, bool shift, bool forward)
    {
        _ = shift;
        if (macOs && command)
        {
            return null;
        }

        return EditAction.Deleting(word ? DeleteUnit.Word : DeleteUnit.Cluster, forward);
    }

    /// <summary>
    /// Whether a key event is a typed character: <paramref name="text"/> (the key's own text, <c>KeyEventArgs.KeySymbol</c>) is a printable string and the modifiers are none, Shift
    /// or AltGr. AltGr arrives as Ctrl and Alt together on Windows and is text, never a command (so <c>Ctrl+Alt+Q</c> typing "@" on a German layout is no Alt+Q); Ctrl, Alt (except
    /// Option on macOS, which types characters) and ⌘ with a letter are commands.
    /// </summary>
    public static bool IsTextInput(KeyEventArgs e, string? text)
    {
        ArgumentNullException.ThrowIfNull(e);
        return IsTextInput(e.Key, e.KeyModifiers, text, OperatingSystem.IsMacOS());
    }

    /// <summary>
    /// The rule of <see cref="IsTextInput(KeyEventArgs, string?)"/> for a given key, modifiers and system. A chord that <see cref="For"/> maps to an action is never text, whatever the key's text is
    /// (on macOS the key text of Option with an arrow or Backspace can be a private-use character).
    /// </summary>
    public static bool IsTextInput(Key key, KeyModifiers modifiers, string? text, bool macOs) =>
        IsTextInput(modifiers, text, macOs) && For(key, modifiers, macOs) is null && FormatCommandFor(key, modifiers, macOs) is null;

    /// <summary>
    /// The formatting command (<see cref="WriteCommandIds.IsFormatting"/>) a key press stands for, or null: the command key (Ctrl, Cmd on macOS) with B, I, U, E, L, R or J, as the catalog says (the page view owns
    /// these keys, the window never matches them). The command key must be the only modifier: AltGr (Ctrl and Alt together on Windows) is text, never a shortcut, and Ctrl on macOS is no command key.
    /// </summary>
    public static string? FormatCommandFor(Key key, KeyModifiers modifiers, bool macOs)
    {
        var command = macOs ? KeyModifiers.Meta : KeyModifiers.Control;
        foreach (var candidate in WriteCommandCatalog.Commands)
        {
            if (WriteCommandIds.IsFormatting(candidate.Id) && candidate.ShortcutFor(macOs) is { } shortcut && shortcut.Matches(key, modifiers, command))
            {
                return candidate.Id;
            }
        }

        return null;
    }

    /// <summary>The rule of <see cref="IsTextInput(KeyEventArgs, string?)"/> for given modifiers and system.</summary>
    public static bool IsTextInput(KeyModifiers modifiers, string? text, bool macOs)
    {
        // A control character, or a private-use character (U+E000 to U+F8FF: what macOS reports as the text of a function or arrow key), is no text.
        if (string.IsNullOrEmpty(text) || char.IsControl(text[0]) || text[0] is >= '' and <= '')
        {
            return false;
        }

        var ctrl = (modifiers & KeyModifiers.Control) != 0;
        var alt = (modifiers & KeyModifiers.Alt) != 0;
        if ((modifiers & KeyModifiers.Meta) != 0)
        {
            return false;
        }

        if (ctrl && alt)
        {
            return true;
        }

        return !ctrl && (!alt || macOs);
    }
}
