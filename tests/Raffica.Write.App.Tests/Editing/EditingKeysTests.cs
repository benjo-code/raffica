using Avalonia.Input;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Xunit;

namespace Raffica.WriteApp.Tests.Editing;

/// <summary>The table of editing keys (Task 16 of milestone 4.3a): key, modifiers and system to action, and the rule of which key events are text.</summary>
public sealed class EditingKeysTests
{
    private const KeyModifiers None = KeyModifiers.None;
    private const KeyModifiers Shift = KeyModifiers.Shift;
    private const KeyModifiers Ctrl = KeyModifiers.Control;
    private const KeyModifiers Alt = KeyModifiers.Alt;
    private const KeyModifiers Cmd = KeyModifiers.Meta;

    private static EditAction M(CaretMove move, bool extend = false) => EditAction.Moving(move, extend);

    private static EditAction D(DeleteUnit unit, bool forward) => EditAction.Deleting(unit, forward);

    private static readonly (Key Key, KeyModifiers Modifiers, bool MacOs, EditAction? Expected)[] s_table =
    [
        // Windows and Linux.
        (Key.Left, None, false, M(CaretMove.Left)),
        (Key.Right, Shift, false, M(CaretMove.Right, true)),
        (Key.Left, Ctrl, false, M(CaretMove.WordLeft)),
        (Key.Right, Ctrl | Shift, false, M(CaretMove.WordRight, true)),
        (Key.Up, None, false, M(CaretMove.Up)),
        (Key.Down, Shift, false, M(CaretMove.Down, true)),
        (Key.Up, Ctrl, false, M(CaretMove.ParagraphStart)),
        (Key.Down, Ctrl, false, M(CaretMove.ParagraphEnd)),
        (Key.Home, None, false, M(CaretMove.LineStart)),
        (Key.End, Shift, false, M(CaretMove.LineEnd, true)),
        (Key.Home, Ctrl, false, M(CaretMove.DocumentStart)),
        (Key.End, Ctrl | Shift, false, M(CaretMove.DocumentEnd, true)),
        (Key.PageUp, None, false, M(CaretMove.PageUp)),
        (Key.PageDown, Shift, false, M(CaretMove.PageDown, true)),
        (Key.PageDown, Ctrl, false, null),
        (Key.Back, None, false, D(DeleteUnit.Cluster, false)),
        (Key.Delete, None, false, D(DeleteUnit.Cluster, true)),
        (Key.Back, Ctrl, false, D(DeleteUnit.Word, false)),
        (Key.Delete, Ctrl, false, D(DeleteUnit.Word, true)),
        (Key.Enter, None, false, new EditAction(EditActionKind.Enter)),
        (Key.Enter, Shift, false, null),
        (Key.Enter, Ctrl, false, null),
        (Key.Tab, None, false, new EditAction(EditActionKind.Tab)),
        (Key.Tab, Shift, false, new EditAction(EditActionKind.Nothing)),
        (Key.Tab, Ctrl, false, null),
        (Key.A, Ctrl, false, new EditAction(EditActionKind.SelectAll)),
        (Key.A, None, false, null),
        (Key.Z, Ctrl, false, EditAction.Run(EditingKeys.UndoCommand)),
        (Key.Y, Ctrl, false, EditAction.Run(EditingKeys.RedoCommand)),
        (Key.Z, Ctrl | Shift, false, EditAction.Run(EditingKeys.RedoCommand)),
        (Key.C, Ctrl, false, EditAction.Run(EditingKeys.CopyCommand)),
        (Key.X, Ctrl, false, EditAction.Run(EditingKeys.CutCommand)),
        (Key.V, Ctrl, false, EditAction.Run(EditingKeys.PasteCommand)),
        (Key.Left, Alt, false, null),
        (Key.Q, Alt, false, null),
        (Key.Z, Cmd, false, null),
        (Key.Escape, None, false, null),

        // macOS: ⌘ is the command key, Option makes words, ⌘ with a side arrow goes to the line ends.
        (Key.Z, Cmd, true, EditAction.Run(EditingKeys.UndoCommand)),
        (Key.Z, Cmd | Shift, true, EditAction.Run(EditingKeys.RedoCommand)),
        (Key.Y, Cmd, true, null),
        (Key.Z, Ctrl, true, null),
        (Key.A, Cmd, true, new EditAction(EditActionKind.SelectAll)),
        (Key.C, Cmd, true, EditAction.Run(EditingKeys.CopyCommand)),
        (Key.V, Cmd, true, EditAction.Run(EditingKeys.PasteCommand)),
        (Key.Left, Alt, true, M(CaretMove.WordLeft)),
        (Key.Right, Alt | Shift, true, M(CaretMove.WordRight, true)),
        (Key.Left, Cmd, true, M(CaretMove.LineStart)),
        (Key.Right, Cmd | Shift, true, M(CaretMove.LineEnd, true)),
        (Key.Up, Cmd, true, M(CaretMove.DocumentStart)),
        (Key.Down, Cmd, true, M(CaretMove.DocumentEnd)),
        (Key.Up, Alt, true, M(CaretMove.ParagraphStart)),
        (Key.Down, Alt, true, M(CaretMove.ParagraphEnd)),
        (Key.Home, None, true, M(CaretMove.DocumentStart)),
        (Key.End, Shift, true, M(CaretMove.DocumentEnd, true)),
        (Key.Back, Alt, true, D(DeleteUnit.Word, false)),
        (Key.Delete, Alt, true, D(DeleteUnit.Word, true)),
        (Key.Back, Cmd, true, null),
        (Key.Left, Ctrl, true, null),
        (Key.Left, Alt | Cmd, true, null),
    ];

    [Fact]
    public void EveryKeyMapsToItsAction()
    {
        var wrong = s_table
            .Where(row => EditingKeys.For(row.Key, row.Modifiers, row.MacOs) != row.Expected)
            .Select(row => $"{row.Modifiers}+{row.Key} (mac {row.MacOs})")
            .ToList();

        Assert.Empty(wrong);
        Assert.True(s_table.Length > 50);
    }

    // The shortcuts macOS keeps for itself (copied from the PDF app's list: Write never references the PDF app).
    internal static readonly (Key Key, KeyModifiers Modifiers)[] s_reservedOnMac =
    [
        (Key.H, Cmd), (Key.H, Alt | Cmd), (Key.M, Cmd), (Key.Q, Cmd), (Key.Q, Shift | Cmd), (Key.Tab, Cmd), (Key.Space, Cmd), (Key.OemTilde, Cmd),
        (Key.F, Ctrl | Cmd), (Key.D3, Shift | Cmd), (Key.D4, Shift | Cmd), (Key.D5, Shift | Cmd),
    ];

    [Fact]
    public void NoMacEntryIsAShortcutMacOsReserves()
    {
        var modifierSets = new List<KeyModifiers>();
        for (var bits = 0; bits < 16; bits++)
        {
            modifierSets.Add((KeyModifiers)bits);
        }

        var clashes = new List<string>();
        foreach (var key in Enum.GetValues<Key>())
        {
            foreach (var modifiers in modifierSets)
            {
                if (EditingKeys.For(key, modifiers, macOs: true) is not null && s_reservedOnMac.Any(r => r.Key == key && r.Modifiers == modifiers))
                {
                    clashes.Add($"{modifiers}+{key}");
                }
            }
        }

        Assert.Empty(clashes);
        // The audit sees something: the table has macOS entries at all.
        Assert.NotNull(EditingKeys.For(Key.Z, Cmd, macOs: true));
    }

    [Fact]
    public void ARepeatingActionIsSaidSoAndAOneShotIsNot()
    {
        foreach (var action in new[] { EditAction.Moving(CaretMove.Left, false), EditAction.Deleting(DeleteUnit.Cluster, false), new EditAction(EditActionKind.Enter), new EditAction(EditActionKind.Tab) })
        {
            Assert.True(action.AllowsRepeat);
        }

        foreach (var action in new[] { new EditAction(EditActionKind.SelectAll), EditAction.Run(EditingKeys.UndoCommand), EditAction.Run(EditingKeys.PasteCommand) })
        {
            Assert.False(action.AllowsRepeat);
        }
    }

    [Theory]
    [InlineData(None, "a", false, true)]
    [InlineData(Shift, "A", false, true)]
    [InlineData(None, "😀", false, true)]
    [InlineData(None, "é", false, true)]
    [InlineData(None, null, false, false)]
    [InlineData(None, "", false, false)]
    [InlineData(None, "\r", false, false)]
    [InlineData(None, "\t", false, false)]
    [InlineData(Ctrl, "a", false, false)]
    [InlineData(Alt, "q", false, false)]
    [InlineData(Ctrl | Alt, "@", false, true)]
    [InlineData(Ctrl | Alt, "q", false, true)]
    [InlineData(Cmd, "v", false, false)]
    [InlineData(Alt, "@", true, true)]
    [InlineData(Cmd, "v", true, false)]
    [InlineData(Ctrl, "a", true, false)]
    [InlineData(Ctrl | Alt | Cmd, "@", true, false)]
    public void WhichKeyEventsAreText(KeyModifiers modifiers, string? text, bool macOs, bool expected) =>
        Assert.Equal(expected, EditingKeys.IsTextInput(modifiers, text, macOs));

    [Theory]
    [InlineData("")]
    [InlineData("")]
    [InlineData("")]
    [InlineData("")]
    public void APrivateUseSymbolIsNeverText(string symbol)
    {
        Assert.False(EditingKeys.IsTextInput(None, symbol, macOs: true));
        Assert.False(EditingKeys.IsTextInput(Key.Left, Alt, symbol, macOs: true));
        Assert.False(EditingKeys.IsTextInput(Key.Back, Alt, symbol, macOs: true));
    }

    [Fact]
    public void AChordThatMapsToAnActionIsNeverTextWhateverItsSymbolIs()
    {
        var wrong = new List<string>();
        foreach (var row in s_table.Where(r => r.Expected is not null))
        {
            foreach (var symbol in new[] { "a", "", "x" })
            {
                if (EditingKeys.IsTextInput(row.Key, row.Modifiers, symbol, row.MacOs))
                {
                    wrong.Add($"{row.Modifiers}+{row.Key} '{symbol}' (mac {row.MacOs})");
                }
            }
        }

        Assert.Empty(wrong);
        // Real text stays text: letters, and AltGr with a letter key.
        Assert.True(EditingKeys.IsTextInput(Key.A, None, "a", macOs: false));
        Assert.True(EditingKeys.IsTextInput(Key.Q, Ctrl | Alt, "@", macOs: false));
        Assert.True(EditingKeys.IsTextInput(Key.L, Alt, "@", macOs: true));
    }

    [Fact]
    public void ATypedKeyEventIsTextAndAnAltGrKeyEventToo()
    {
        var typed = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Q, KeyModifiers = Ctrl | Alt, KeySymbol = "@" };
        Assert.True(EditingKeys.IsTextInput(typed, typed.KeySymbol));
        var command = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Q, KeyModifiers = Alt, KeySymbol = "q" };
        Assert.False(EditingKeys.IsTextInput(command, command.KeySymbol));
    }
}
