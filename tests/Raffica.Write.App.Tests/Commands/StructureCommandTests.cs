using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Raffica.Write;
using Raffica.Write.Commands;
using Raffica.Write.Editing;
using Raffica.Write.Platform.MacOS;
using Raffica.WriteApp.Tests.Platform;
using Xunit;

namespace Raffica.WriteApp.Tests.Commands;

/// <summary>The commands of the structure milestone (4.4a): ids, icons, wording, the rules with a reason for every disabled state, the keys and the macOS menu.</summary>
public sealed class StructureCommandTests
{
    private static readonly string[] s_ids =
    [
        "structure.numberHeadings", "list.bullets", "list.numbers", "list.restart", "list.continue", "outline.moveUp", "outline.moveDown", "outline.promote", "outline.demote",
    ];

    private static readonly string[] s_toggles = [WriteCommandIds.NumberHeadings, WriteCommandIds.Bullets, WriteCommandIds.Numbers];

    private static CatalogCommand Of(string id) => WriteCommandCatalog.Commands.Single(c => c.Id == id);

    [Fact]
    public void TheIdsAreTheOnesTheSpecNames()
    {
        string[] named = [WriteCommandIds.NumberHeadings, WriteCommandIds.Bullets, WriteCommandIds.Numbers, WriteCommandIds.RestartNumbering, WriteCommandIds.ContinueNumbering, WriteCommandIds.MoveUp, WriteCommandIds.MoveDown, WriteCommandIds.Promote, WriteCommandIds.Demote];
        Assert.Equal(s_ids, named);
        Assert.All(s_ids, id => Assert.Single(WriteCommandCatalog.Commands, c => c.Id == id));
        Assert.All(s_toggles, id => Assert.True(Of(id).Toggle, id));
        Assert.All(s_ids.Except(s_toggles), id => Assert.False(Of(id).Toggle, id));
        Assert.All(s_ids, id => Assert.False(Of(id).TakesPayload, id));
    }

    [AvaloniaFact]
    public void EveryNewCommandHasItsOwnIconAndBothLanguagesAndAMacMenuLine()
    {
        var all = WriteCommandCatalog.Commands;
        foreach (var group in all.GroupBy(c => c.Icon).Where(g => g.Count() > 1))
        {
            Assert.Fail($"Icon {group.Key} serves {string.Join(", ", group.Select(c => c.Id))}");
        }

        var keys = XDocument.Load(RepoPaths.Combine("src", "Raffica.Shell", "Styles", "Icons.axaml")).Root!.Elements()
            .Select(e => (string?)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")))
            .Where(k => k is not null && k.StartsWith("Raffica.Icon.", StringComparison.Ordinal)).ToHashSet();
        foreach (var id in s_ids)
        {
            var command = Of(id);
            Assert.Contains("Raffica.Icon." + command.Icon, keys);
            Assert.True(Application.Current!.TryGetResource("Raffica.Icon." + command.Icon, Application.Current!.ActualThemeVariant, out var value), id);
            Assert.IsAssignableFrom<Geometry>(value);
        }

        foreach (var language in new[] { "en", "de" })
        {
            var culture = System.Globalization.CultureInfo.GetCultureInfo(language);
            foreach (var id in s_ids)
            {
                var command = Of(id);
                Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn(command.NameKey, culture)), id);
                Assert.DoesNotContain("toggle", WriteStrings.GetIn(command.NameKey, culture), StringComparison.OrdinalIgnoreCase);
                Assert.NotNull(command.DescriptionKey);
                Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn(command.DescriptionKey!, culture)), id);
            }
        }

        // The macOS menu has a line for each command and runs it by id.
        var window = new WriteMacMenuTests.FakeWindow { HasTab = true };
        window.Allowed.UnionWith(s_ids);
        var menu = new WriteMacMenu(() => window, () => Task.CompletedTask, WriteMenuPart.Window);
        menu.Refresh();
        var format = menu.Menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == WriteStrings.MenuFormat).Menu!.Items.OfType<NativeMenuItem>().ToList();
        foreach (var id in s_ids)
        {
            var item = Assert.Single(format, i => i.Header == Of(id).Name);
            Assert.True(item.Command!.CanExecute(null), id);
            item.Command.Execute(null);
            Assert.Equal("run " + id, window.Calls[^1]);
            Assert.Equal(s_toggles.Contains(id) ? MenuItemToggleType.CheckBox : MenuItemToggleType.None, item.ToggleType);
        }
    }

    [Fact]
    public void TheKeysAreAltShiftArrowsOnWindowsAndLinuxAndNoneOnTheMac()
    {
        Assert.Equal("Alt+Shift+Up", Of(WriteCommandIds.MoveUp).ShortcutFor(false)!.Text(false));
        Assert.Equal("Alt+Shift+Down", Of(WriteCommandIds.MoveDown).ShortcutFor(false)!.Text(false));
        Assert.Equal("Alt+Shift+Left", Of(WriteCommandIds.Promote).ShortcutFor(false)!.Text(false));
        Assert.Equal("Alt+Shift+Right", Of(WriteCommandIds.Demote).ShortcutFor(false)!.Text(false));

        // On the Mac the Option and Shift arrows extend the selection by paragraph, and no free key of the audit fits the shortcut model: the commands have no key there.
        foreach (var id in s_ids)
        {
            Assert.Null(Of(id).ShortcutFor(true));
        }

        Assert.All(s_ids.Take(5), id => Assert.Null(Of(id).ShortcutFor(false)));

        // The page view owns the keys, the window never matches them through the catalog (Alt+Shift+Left is no frame key).
        Assert.Null(WriteCommandCatalog.Find(Key.Up, KeyModifiers.Alt | KeyModifiers.Shift, KeyModifiers.Control, macOs: false));
    }

    [Fact]
    public void TheFieldCodesAreUniqueAndTheHomeBandHasTheNewCommands()
    {
        var home = WriteCommandCatalog.Tabs.Single(t => t.Letter == WriteRibbon.HomeLetter);
        foreach (var id in s_ids)
        {
            Assert.Equal(home, Of(id).Tab);
        }

        var codes = home.Groups.SelectMany(g => g.Commands.Select(c => c.FieldCode)).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, a => Assert.DoesNotContain(codes, b => a != b && b.StartsWith(a, StringComparison.Ordinal)));
        WriteRibbon.Create(new Dictionary<string, Action>());
    }

    private static IEnumerable<StructureFacts> Facts()
    {
        var lists = new[]
        {
            new ListFacts(false, null, ListReason.None, false, ListReason.NotInAList, false, ListReason.NotInAList),
            new ListFacts(true, ListKind.Bullet, ListReason.None, true, ListReason.None, true, ListReason.None),
            new ListFacts(true, ListKind.Numbered, ListReason.None, true, ListReason.None, false, ListReason.NothingBefore),
            new ListFacts(true, null, ListReason.None, true, ListReason.None, false, ListReason.AlreadyContinues),
            new ListFacts(false, null, ListReason.SelectionAcrossContainers, false, ListReason.SelectionAcrossContainers, false, ListReason.SelectionAcrossContainers),
        };
        var outline = Enum.GetValues<OutlineReason>();
        foreach (var list in lists)
        {
            foreach (var headings in Enum.GetValues<HeadingNumberingState>())
            {
                foreach (var reason in outline)
                {
                    yield return new StructureFacts(list, headings, reason, reason, reason, reason);
                }
            }
        }
    }

    [Fact]
    public void EveryDisabledStateHasAReason()
    {
        var count = 0;
        foreach (var blocked in new EditBlockedReason?[] { null, EditBlockedReason.StillOpening, EditBlockedReason.Saving, EditBlockedReason.Faulted })
        {
            foreach (var facts in Facts())
            {
                var state = new EditCommandState(blocked, true, true, true);
                var rules = new WriteCommandRules(() => true, () => 1.0, () => true, () => state, structureFacts: _ => facts);
                foreach (var id in s_ids)
                {
                    count++;
                    var text = $"{id}, {blocked}, {facts}";
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
        }

        Assert.True(count > 2000);

        // Without a document the start view shows no commands: nothing is told.
        var none = new WriteCommandRules(() => false, () => 1.0, () => true, () => new EditCommandState(null, false, false, false));
        Assert.All(s_ids, id => Assert.Null(none.ReasonFor(id)));
    }

    [Fact]
    public void TheRulesWordEachReasonAndSayYesWhenAllIsWell()
    {
        static string? Reason(string id, StructureFacts facts, EditBlockedReason? blocked = null)
        {
            var rules = new WriteCommandRules(() => true, () => 1.0, () => true, () => new EditCommandState(blocked, true, true, true), structureFacts: _ => facts);
            return rules.ReasonFor(id);
        }

        var inList = new ListFacts(true, ListKind.Bullet, ListReason.None, true, ListReason.None, true, ListReason.None);
        var free = new StructureFacts(inList, HeadingNumberingState.Off, OutlineReason.None, OutlineReason.None, OutlineReason.None, OutlineReason.None);
        Assert.All(s_ids, id => Assert.Null(Reason(id, free)));

        var outside = free with { List = new ListFacts(false, null, ListReason.None, false, ListReason.NotInAList, false, ListReason.NotInAList) };
        Assert.Equal(WriteStrings.ReasonNotInAList, Reason(WriteCommandIds.RestartNumbering, outside));
        Assert.Equal(WriteStrings.ReasonNotInAList, Reason(WriteCommandIds.ContinueNumbering, outside));
        Assert.Null(Reason(WriteCommandIds.Bullets, outside));

        var nothing = free with { List = inList with { CanContinue = false, ContinueReason = ListReason.NothingBefore } };
        Assert.Equal(WriteStrings.ReasonNothingToContinue, Reason(WriteCommandIds.ContinueNumbering, nothing));
        var already = free with { List = inList with { CanContinue = false, ContinueReason = ListReason.AlreadyContinues } };
        Assert.Equal(WriteStrings.ReasonAlreadyContinues, Reason(WriteCommandIds.ContinueNumbering, already));
        var across = free with { List = new ListFacts(false, null, ListReason.SelectionAcrossContainers, false, ListReason.SelectionAcrossContainers, false, ListReason.SelectionAcrossContainers) };
        Assert.Equal(WriteStrings.ReasonListAcrossContainers, Reason(WriteCommandIds.Bullets, across));
        Assert.Equal(WriteStrings.ReasonListAcrossContainers, Reason(WriteCommandIds.Numbers, across));

        Assert.Equal(WriteStrings.ReasonNoHeadingStyles, Reason(WriteCommandIds.NumberHeadings, free with { Headings = HeadingNumberingState.Unavailable }));
        Assert.Null(Reason(WriteCommandIds.NumberHeadings, free with { Headings = HeadingNumberingState.On }));

        Assert.Equal(WriteStrings.ReasonNoHeadingAtCaret, Reason(WriteCommandIds.MoveUp, free with { MoveUp = OutlineReason.NoHeading }));
        Assert.Equal(WriteStrings.ReasonMoveSectionBreak, Reason(WriteCommandIds.MoveDown, free with { MoveDown = OutlineReason.SectionBreakInside }));
        Assert.Equal(WriteStrings.ReasonMoveInControl, Reason(WriteCommandIds.MoveDown, free with { MoveDown = OutlineReason.InsideContentControl }));
        Assert.Equal(WriteStrings.ReasonMoveNotTopLevel, Reason(WriteCommandIds.MoveUp, free with { MoveUp = OutlineReason.NotAtTopLevel }));
        Assert.Equal(WriteStrings.ReasonMoveAtTop, Reason(WriteCommandIds.MoveUp, free with { MoveUp = OutlineReason.AtTop }));
        Assert.Equal(WriteStrings.ReasonMoveAtEnd, Reason(WriteCommandIds.MoveDown, free with { MoveDown = OutlineReason.AtEnd }));
        Assert.Equal(WriteStrings.ReasonShiftNotHeadingStyle, Reason(WriteCommandIds.Promote, free with { Promote = OutlineReason.NotAHeadingStyle }));
        Assert.Equal(WriteStrings.ReasonPromoteOutOfRange, Reason(WriteCommandIds.Promote, free with { Promote = OutlineReason.OutOfRange }));
        Assert.Equal(WriteStrings.ReasonDemoteOutOfRange, Reason(WriteCommandIds.Demote, free with { Demote = OutlineReason.OutOfRange }));

        // The block comes first; a save disables the commands (the target is read from the model as it is now).
        Assert.Equal(WriteStrings.ReasonEditOpening, Reason(WriteCommandIds.Bullets, free, EditBlockedReason.StillOpening));
        Assert.Equal(WriteStrings.EditUnavailable, Reason(WriteCommandIds.MoveUp, free, EditBlockedReason.Faulted));
        Assert.Equal(WriteStrings.ReasonEditSaving, Reason(WriteCommandIds.Numbers, free, EditBlockedReason.Saving));
    }
}
