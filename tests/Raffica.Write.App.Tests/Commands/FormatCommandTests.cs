using System.Reflection;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Platform.MacOS;
using Raffica.WriteApp.Tests.Platform;
using Xunit;

namespace Raffica.WriteApp.Tests.Commands;

/// <summary>The commands of the formatting milestone (4.3b): ids, icons, field codes, the rules with a reason for every disabled state and the macOS menu.</summary>
public sealed class FormatCommandTests
{
    private static readonly string[] s_toggles =
    [
        WriteCommandIds.Bold, WriteCommandIds.Italic, WriteCommandIds.Underline, WriteCommandIds.Strike, WriteCommandIds.Superscript, WriteCommandIds.Subscript,
        WriteCommandIds.SmallCaps, WriteCommandIds.AllCaps, WriteCommandIds.KeepNext, WriteCommandIds.KeepLines, WriteCommandIds.PageBreakBefore, WriteCommandIds.WidowControl,
    ];

    private static readonly string[] s_aligns = [WriteCommandIds.AlignLeft, WriteCommandIds.AlignCenter, WriteCommandIds.AlignRight, WriteCommandIds.Justify];

    private static readonly string[] s_plain = [.. s_toggles, .. s_aligns, WriteCommandIds.IndentMore, WriteCommandIds.IndentLess, WriteCommandIds.ClearDirect, WriteCommandIds.UpdateStyle];

    private static readonly string[] s_payload =
    [
        WriteCommandIds.Font, WriteCommandIds.Size, WriteCommandIds.SizeUp, WriteCommandIds.SizeDown, WriteCommandIds.Color, WriteCommandIds.Highlight, WriteCommandIds.SpaceBefore,
        WriteCommandIds.SpaceAfter, WriteCommandIds.LineSpacing, WriteCommandIds.ParagraphStyle, WriteCommandIds.CharacterStyle,
    ];

    // A hand copy of the ids that the milestone spec and the task name (the 21 plain ones with the Formatting pane, then the 11 value commands): the test compares it with the lists above, so a renamed
    // or forgotten id shows up as a difference to the spec text and not only to the code.
    private static readonly string[] s_specIds =
    [
        "format.bold", "format.italic", "format.underline", "format.strike", "format.superscript", "format.subscript", "format.smallCaps", "format.allCaps",
        "format.alignLeft", "format.alignCenter", "format.alignRight", "format.justify", "format.indentMore", "format.indentLess", "format.keepNext", "format.keepLines",
        "format.pageBreakBefore", "format.widowControl", "format.clearDirect", "format.updateStyle", "view.formatting",
        "format.font", "format.size", "format.sizeUp", "format.sizeDown", "format.color", "format.highlight", "format.spaceBefore", "format.spaceAfter", "format.lineSpacing",
        "format.paragraphStyle", "format.characterStyle",
    ];

    private static string[] FormatIds() => [.. s_plain, .. s_payload, WriteCommandIds.FormattingPane];

    private static string[] AllIds() =>
        [.. typeof(WriteCommandIds).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!)];

    [Fact]
    public void TheFormatIdsAreTheOnesTheSpecNames()
    {
        Assert.Equal(s_specIds.Order(StringComparer.Ordinal), FormatIds().Order(StringComparer.Ordinal));
        Assert.All(FormatIds(), id => Assert.Contains(id, AllIds()));
        Assert.All(s_payload, id => Assert.True(WriteCommandCatalog.Commands.Single(c => c.Id == id).TakesPayload, id));
        Assert.All(s_plain, id => Assert.False(WriteCommandCatalog.Commands.Single(c => c.Id == id).TakesPayload, id));
    }

    [AvaloniaFact]
    public void EveryFormatCommandHasItsOwnLucideIcon()
    {
        var all = WriteCommandCatalog.Commands;
        foreach (var group in all.GroupBy(c => c.Icon).Where(g => g.Count() > 1))
        {
            Assert.Fail($"Icon {group.Key} serves {string.Join(", ", group.Select(c => c.Id))}");
        }

        // The key list of the shell is the Lucide set (third-party/lucide); a key that is not there is not an icon.
        var keys = XDocument.Load(RepoPaths.Combine("src", "Raffica.Shell", "Styles", "Icons.axaml")).Root!.Elements()
            .Select(e => (string?)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")))
            .Where(k => k is not null && k.StartsWith("Raffica.Icon.", StringComparison.Ordinal)).ToHashSet();
        foreach (var command in all.Where(c => FormatIds().Contains(c.Id)))
        {
            Assert.Contains("Raffica.Icon." + command.Icon, keys);
            Assert.True(
                Application.Current!.TryGetResource("Raffica.Icon." + command.Icon, Application.Current!.ActualThemeVariant, out var value),
                command.Id + " " + command.Icon);
            Assert.IsAssignableFrom<Geometry>(value);
        }
    }

    [Fact]
    public void EveryFormatCommandNamesItselfInBothLanguagesWithoutTheWordToggle()
    {
        foreach (var language in new[] { "en", "de" })
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo(language);
            foreach (var command in WriteCommandCatalog.Commands.Where(c => FormatIds().Contains(c.Id)))
            {
                var name = WriteStrings.GetIn(command.NameKey, culture);
                Assert.False(string.IsNullOrWhiteSpace(name), command.Id);
                Assert.DoesNotContain("toggle", name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void FieldCodesAreUnique()
    {
        foreach (var tab in WriteCommandCatalog.Tabs)
        {
            var codes = tab.Groups.SelectMany(g => g.Commands.Select(c => c.FieldCode)).ToList();
            Assert.Equal(codes.Count, codes.Distinct().Count());
            foreach (var group in tab.Groups)
            {
                Assert.All(group.Commands, c => Assert.StartsWith(group.Digit, c.FieldCode, StringComparison.Ordinal));
            }

            // No code begins another one, and every code is a digit and one more character.
            Assert.All(codes, a => Assert.DoesNotContain(codes, b => a != b && b.StartsWith(a, StringComparison.Ordinal)));
            Assert.All(codes, c => Assert.Equal(2, c.Length));
        }

        var home = WriteCommandCatalog.Tabs.Single(t => t.Letter == WriteRibbon.HomeLetter);
        Assert.Equal("5", home.Groups.Single(g => g.LabelKey == "GroupFont").Digit);
        Assert.Equal("6", home.Groups.Single(g => g.LabelKey == "GroupParagraph").Digit);
        Assert.Equal("7", home.Groups.Single(g => g.LabelKey == "GroupStyles").Digit);
        var view = WriteCommandCatalog.Tabs.Single(t => t.Letter == WriteRibbon.ViewLetter);
        Assert.Contains(WriteCommandIds.FormattingPane, view.Groups.Single(g => g.LabelKey == "GroupPanes").Commands.Select(c => c.Id));

        // The ribbon built from the catalog is valid whatever is wired.
        WriteRibbon.Create(new Dictionary<string, Action>());
    }

    [Fact]
    public void TheFormatShortcutsAreTheSpecsAndTheWindowNeverMatchesThem()
    {
        static string? Text(string id, bool mac) => WriteCommandCatalog.Commands.Single(c => c.Id == id).ShortcutFor(mac)?.Text(mac);
        Assert.Equal("Ctrl+B", Text(WriteCommandIds.Bold, false));
        Assert.Equal("Ctrl+I", Text(WriteCommandIds.Italic, false));
        Assert.Equal("Ctrl+U", Text(WriteCommandIds.Underline, false));
        Assert.Equal("Ctrl+E", Text(WriteCommandIds.AlignCenter, false));
        Assert.Equal("Ctrl+L", Text(WriteCommandIds.AlignLeft, false));
        Assert.Equal("Ctrl+R", Text(WriteCommandIds.AlignRight, false));
        Assert.Equal("Ctrl+J", Text(WriteCommandIds.Justify, false));
        Assert.Equal("Meta+B", Text(WriteCommandIds.Bold, true));
        Assert.All(s_payload.Append(WriteCommandIds.UpdateStyle).Append(WriteCommandIds.ClearDirect), id => Assert.Null(Text(id, false)));

        // The page view owns these keys (as the clipboard keys): a text box in a pane keeps its own Ctrl+B.
        Assert.Null(WriteCommandCatalog.Find(Key.B, KeyModifiers.Control, KeyModifiers.Control, macOs: false));
        Assert.True(WriteCommandIds.IsFormatting(WriteCommandIds.Bold));
        Assert.False(WriteCommandIds.IsFormatting(WriteCommandIds.FormattingPane));
        Assert.False(WriteCommandIds.IsFormatting(WriteCommandIds.Undo));
    }

    private static IEnumerable<EditCommandState> States()
    {
        foreach (var blocked in new EditBlockedReason?[] { null, EditBlockedReason.StillOpening, EditBlockedReason.Saving, EditBlockedReason.Faulted })
        {
            foreach (var selection in new[] { false, true })
            {
                foreach (var styles in new[] { false, true })
                {
                    foreach (var defined in new[] { false, true })
                    {
                        foreach (var mixed in new[] { false, true })
                        {
                            foreach (var overrides in new[] { false, true })
                            {
                                foreach (var update in new[] { false, true })
                                {
                                    yield return new EditCommandState(blocked, selection, true, true, styles, defined, mixed, overrides, update);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void EveryFormatCommandHasAReasonInEveryDisabledState()
    {
        var count = 0;
        foreach (var state in States())
        {
            var rules = new WriteCommandRules(() => true, () => 1.0, () => true, () => state);
            foreach (var id in FormatIds())
            {
                count++;
                var text = $"{id}, {state}";
                if (rules.Allows(id))
                {
                    Assert.True(rules.ReasonFor(id) is null, text);
                }
                else
                {
                    Assert.False(string.IsNullOrWhiteSpace(rules.ReasonFor(id)), text);
                }
            }
        }

        Assert.True(count > 4000);

        // Without a document the start view shows no commands: nothing is told.
        var none = new WriteCommandRules(() => false, () => 1.0, () => true, () => new EditCommandState(null, false, false, false));
        Assert.All(FormatIds(), id => Assert.Null(none.ReasonFor(id)));
    }

    [Fact]
    public void TheBlockComesFirstAndFormattingFollowsTheEditCommands()
    {
        static string? Reason(EditCommandState state, string id) => new WriteCommandRules(() => true, () => 1.0, () => true, () => state).ReasonFor(id);
        Assert.Equal(WriteStrings.ReasonEditOpening, Reason(new EditCommandState(EditBlockedReason.StillOpening, false, false, false), WriteCommandIds.Bold));
        Assert.Equal(WriteStrings.EditUnavailable, Reason(new EditCommandState(EditBlockedReason.Faulted, false, false, false), WriteCommandIds.Justify));

        // A save queues formatting like typing: the commands stay on.
        var saving = new WriteCommandRules(() => true, () => 1.0, () => true, () => new EditCommandState(EditBlockedReason.Saving, true, true, true));
        Assert.All(s_plain.Concat(s_payload), id => Assert.True(saving.Allows(id), id));

        // The view of the pane needs a document only.
        var faulted = new WriteCommandRules(() => true, () => 1.0, () => true, () => new EditCommandState(EditBlockedReason.Faulted, false, false, false));
        Assert.True(faulted.Allows(WriteCommandIds.FormattingPane));
    }

    [Fact]
    public void UpdateStyleIsDisabledWithoutADefinedStyleAndWhenMixed()
    {
        static string? Reason(EditCommandState state, out bool allowed)
        {
            var rules = new WriteCommandRules(() => true, () => 1.0, () => true, () => state);
            allowed = rules.Allows(WriteCommandIds.UpdateStyle);
            return rules.ReasonFor(WriteCommandIds.UpdateStyle);
        }

        var healthy = new EditCommandState(null, true, true, true);
        Assert.Null(Reason(healthy, out var allowed));
        Assert.True(allowed);

        Assert.Equal(WriteStrings.ReasonStyleNotDefined, Reason(healthy with { StyleDefined = false }, out allowed));
        Assert.False(allowed);
        Assert.Equal(WriteStrings.ReasonStyleNotDefined, Reason(healthy with { HasStylesPart = false }, out allowed));
        Assert.False(allowed);
        Assert.Equal(WriteStrings.ReasonStyleMixed, Reason(healthy with { StyleMixed = true }, out allowed));
        Assert.False(allowed);
        Assert.Equal(WriteStrings.ReasonStyleMixed, Reason(healthy with { StyleMixed = true, StyleDefined = false }, out _));
        Assert.Equal(WriteStrings.ReasonNothingToUpdate, Reason(healthy with { CanUpdateStyle = false }, out allowed));
        Assert.False(allowed);

        // The picker items need styles to choose from; clearing needs formatting to clear.
        var noStyles = new WriteCommandRules(() => true, () => 1.0, () => true, () => healthy with { HasStylesPart = false });
        Assert.Equal(WriteStrings.ReasonNoStyles, noStyles.ReasonFor(WriteCommandIds.ParagraphStyle));
        Assert.Equal(WriteStrings.ReasonNoStyles, noStyles.ReasonFor(WriteCommandIds.CharacterStyle));
        Assert.True(noStyles.Allows(WriteCommandIds.Bold));
        var clean = new WriteCommandRules(() => true, () => 1.0, () => true, () => healthy with { HasOverrides = false });
        Assert.Equal(WriteStrings.ReasonNoFormattingToClear, clean.ReasonFor(WriteCommandIds.ClearDirect));
        Assert.True(clean.Allows(WriteCommandIds.Bold));
    }

    [AvaloniaFact]
    public void EveryFormatMacMenuItemExists()
    {
        var window = new WriteMacMenuTests.FakeWindow { HasTab = true };
        window.Allowed.UnionWith(FormatIds());
        var menu = new WriteMacMenu(() => window, () => Task.CompletedTask, WriteMenuPart.Window);
        menu.Refresh();

        var top = menu.Menu.Items.OfType<NativeMenuItem>().ToList();
        Assert.Equal(["File", "Edit", "Format", "Insert", "Table", "Layout", "View", "Window"], top.Select(i => i.Header!).ToArray());
        var format = top.Single(i => i.Header == WriteStrings.MenuFormat).Menu!.Items.OfType<NativeMenuItem>().ToList();
        var view = top.Single(i => i.Header == WriteStrings.TabView).Menu!.Items.OfType<NativeMenuItem>().ToList();

        // Every command without a payload is in the Format menu (the payload ones are the ribbon's pickers); the pane is in the View menu.
        foreach (var id in s_plain)
        {
            var command = WriteCommandCatalog.Commands.Single(c => c.Id == id);
            var item = Assert.Single(format, i => i.Header == command.Name);
            Assert.True(item.Command!.CanExecute(null), id);
            item.Command.Execute(null);
            Assert.Equal("run " + id, window.Calls[^1]);
            if (command.ShortcutFor(macOs: true) is { } shortcut)
            {
                Assert.Equal(KeyGesture.Parse(shortcut.Text(macOs: true)), item.Gesture);
            }
        }

        var pane = Assert.Single(view, i => i.Header == WriteStrings.CmdFormatting);
        pane.Command!.Execute(null);
        Assert.Equal("run view.formatting", window.Calls[^1]);
        Assert.Equal(MenuItemToggleType.CheckBox, pane.ToggleType);
        Assert.Equal(MenuItemToggleType.Radio, Assert.Single(format, i => i.Header == WriteStrings.CmdFormatAlignLeft).ToggleType);

        // A command the rules do not allow is disabled in the menu.
        window.Allowed.Remove(WriteCommandIds.Bold);
        window.Raise();
        Assert.False(Assert.Single(format, i => i.Header == WriteStrings.CmdFormatBold).Command!.CanExecute(null));
    }
}
