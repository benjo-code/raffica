using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Raffica.Shell.Frame;
using Raffica.Write;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Platform.MacOS;
using Raffica.WriteApp.Tests.Platform;
using Xunit;

namespace Raffica.WriteApp.Tests.Commands;

/// <summary>The commands of the Layout tab (milestone 4.4b, Task 11): ids, icons, wording, the rules with a reason for every disabled state, the switches, the payload values, the macOS menu and the start cost.</summary>
public sealed class SectionCommandTests
{
    private static readonly string[] s_ids =
    [
        "section.numberFrom", "section.break", "section.remove", "section.firstPage", "section.leftRight", "page.size", "page.margins", "page.orientation", "page.columns", "page.setup", "insert.pageNumber",
    ];

    private static readonly string[] s_payload = [WriteCommandIds.PageSize, WriteCommandIds.PageMargins, WriteCommandIds.PageOrientation, WriteCommandIds.PageColumns, WriteCommandIds.SectionBreak, WriteCommandIds.InsertPageNumber];
    private static readonly string[] s_toggles = [WriteCommandIds.DifferentFirstPage, WriteCommandIds.LeftRightPages];

    private const string Break = "<w:pPr><w:sectPr><w:type w:val=\"nextPage\"/></w:sectPr></w:pPr>";
    private const string BreakWithFirstPage = "<w:pPr><w:sectPr><w:type w:val=\"nextPage\"/><w:titlePg/></w:sectPr></w:pPr>";

    private static CatalogCommand Of(string id) => WriteCommandCatalog.Commands.Single(c => c.Id == id);

    private static CommandDefinition Command(WriteWindow window, string id) =>
        window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id);

    private static async Task<(WriteWindow Window, WriteTab Tab, EditSession Session)> OpenAsync(WindowFixture fixture, params string[] paragraphs)
    {
        var path = StyledDocx.Write(fixture.Folder, "doc.docx", StyledDocx.Styles, paragraphs);
        var window = fixture.Show();
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(path)).Tab;
        tab.Runner.LeaseWaitTimeout = TimeSpan.FromSeconds(60);
        tab.Runner.Start();
        await WindowFixture.WaitAsync(() => tab.Runner.FullModelReady || tab.Runner.IsFaulted);
        Assert.True(tab.Runner.FullModelReady);
        var session = tab.EnsureEditor();
        _ = session.Selection;
        return (window, tab, session);
    }

    private static Paragraph P(WriteTab tab, int index) => (Paragraph)tab.Docx.Model.Body.Blocks[index];

    private static void Caret(EditSession session, WriteTab tab, int paragraph, int offset = 0)
    {
        var at = new TextPosition(StoryRef.Body, P(tab, paragraph), offset);
        session.SetSelection(new TextRange(at, at));
    }

    private static void Settle(WriteWindow window)
    {
        window.FormatRefresh?.Flush();
        window.ViewStatesRefresh?.Flush();
        WindowFixture.Pump();
        window.Ribbon.RefreshCommandStates();
        WindowFixture.Pump();
    }

    [Fact]
    public void TheIdsAndTheLayoutTabAreAsSpecified()
    {
        Assert.Equal(s_ids, new[]
        {
            WriteCommandIds.NumberPagesFrom, WriteCommandIds.SectionBreak, WriteCommandIds.RemoveSection, WriteCommandIds.DifferentFirstPage, WriteCommandIds.LeftRightPages,
            WriteCommandIds.PageSize, WriteCommandIds.PageMargins, WriteCommandIds.PageOrientation, WriteCommandIds.PageColumns, WriteCommandIds.PageSetup, WriteCommandIds.InsertPageNumber,
        });
        Assert.All(s_ids, id => Assert.Single(WriteCommandCatalog.Commands, c => c.Id == id));
        Assert.All(s_ids, id => Assert.True(WriteCommandIds.IsSection(id), id));
        Assert.Equal(s_payload.Order(StringComparer.Ordinal), s_ids.Where(WriteCommandIds.TakesPayload).Order(StringComparer.Ordinal));
        Assert.All(s_toggles, id => Assert.True(Of(id).Toggle, id));
        Assert.All(s_ids.Except(s_toggles), id => Assert.False(Of(id).Toggle, id));

        var layout = WriteCommandCatalog.Tabs.Single(t => t.Letter == WriteRibbon.LayoutLetter);
        Assert.Equal("P", layout.Letter);
        Assert.Equal(["GroupPageSetup", "GroupSections", "GroupHeaderFooter"], layout.Groups.Select(g => g.LabelKey).ToArray());
        Assert.Equal(["page.size", "page.margins", "page.orientation", "page.columns", "page.setup"], layout.Groups[0].Commands.Select(c => c.Id).ToArray());
        Assert.Equal(["section.numberFrom", "section.break", "section.remove"], layout.Groups[1].Commands.Select(c => c.Id).ToArray());
        Assert.Equal(["insert.pageNumber", "section.firstPage", "section.leftRight"], layout.Groups[2].Commands.Select(c => c.Id).ToArray());
        Assert.All(s_ids, id => Assert.Equal(layout, Of(id).Tab));

        // The field codes of the tab are unique and none begins another; the key tips of the tabs are unique letters.
        var codes = layout.Groups.SelectMany(g => g.Commands.Select(c => c.FieldCode)).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, a => Assert.DoesNotContain(codes, b => a != b && b.StartsWith(a, StringComparison.Ordinal)));
        var letters = WriteCommandCatalog.Tabs.Select(t => t.Letter).ToList();
        Assert.Equal(letters.Count, letters.Distinct().Count());
        WriteRibbon.Create(new Dictionary<string, Action>());
    }

    [AvaloniaFact]
    public void EveryNewCommandHasItsOwnIconAndBothLanguagesAndAMacMenuLine()
    {
        foreach (var group in WriteCommandCatalog.Commands.GroupBy(c => c.Icon).Where(g => g.Count() > 1))
        {
            Assert.Fail($"Icon {group.Key} serves {string.Join(", ", group.Select(c => c.Id))}");
        }

        var keys = XDocument.Load(RepoPaths.Combine("src", "Raffica.Shell", "Styles", "Icons.axaml")).Root!.Elements()
            .Select(e => (string?)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")))
            .Where(k => k is not null && k.StartsWith("Raffica.Icon.", StringComparison.Ordinal)).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        foreach (var id in s_ids)
        {
            var command = Of(id);
            Assert.Contains("Raffica.Icon." + command.Icon, keys);
            Assert.True(Application.Current!.TryGetResource("Raffica.Icon." + command.Icon, Application.Current!.ActualThemeVariant, out var value), id);
            Assert.IsAssignableFrom<Geometry>(value);
        }

        foreach (var language in new[] { "en", "de" })
        {
            var culture = CultureInfo.GetCultureInfo(language);
            foreach (var id in s_ids)
            {
                var command = Of(id);
                Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn(command.NameKey, culture)), id);
                Assert.NotNull(command.DescriptionKey);
                Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn(command.DescriptionKey!, culture)), id);
                Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn(command.Group.LabelKey, culture)), id);
            }

            Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn("TabLayout", culture)));
            Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn("MenuLayout", culture)));
        }

        // The macOS menu has a line for each command: a plain item runs the command by id, a value command has a submenu whose items run it with their value.
        var window = new WriteMacMenuTests.FakeWindow { HasTab = true };
        window.Allowed.UnionWith(s_ids);
        var menu = new WriteMacMenu(() => window, () => Task.CompletedTask, WriteMenuPart.Window);
        menu.Refresh();
        var layout = menu.Menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == WriteStrings.MenuLayout).Menu!.Items.OfType<NativeMenuItem>().ToList();
        foreach (var id in s_ids)
        {
            var item = Assert.Single(layout, i => i.Header == Of(id).Name);
            if (s_payload.Contains(id))
            {
                Assert.NotNull(item.Menu);
                Assert.Null(item.Gesture);
                var leaf = FirstLeaf(item);
                Assert.True(leaf.Command!.CanExecute(null), id);
                leaf.Command.Execute(null);
                Assert.StartsWith("run " + id + " ", window.Calls[^1]);
                Assert.True(SectionPayloads.IsValid(id, window.Calls[^1][("run " + id + " ").Length..]), window.Calls[^1]);
            }
            else
            {
                Assert.True(item.Command!.CanExecute(null), id);
                Assert.Null(item.Gesture);
                item.Command.Execute(null);
                Assert.Equal("run " + id, window.Calls[^1]);
                Assert.Equal(s_toggles.Contains(id) ? MenuItemToggleType.CheckBox : MenuItemToggleType.None, item.ToggleType);
            }
        }

        // Disabled by the rules: the leaves of a submenu are disabled as well.
        window.Allowed.Clear();
        menu.Refresh();
        foreach (var id in s_payload)
        {
            Assert.False(FirstLeaf(layout.Single(i => i.Header == Of(id).Name)).Command!.CanExecute(null), id);
        }
    }

    private static NativeMenuItem FirstLeaf(NativeMenuItem item) =>
        item.Menu is { } submenu ? FirstLeaf(submenu.Items.OfType<NativeMenuItem>().First()) : item;

    [Fact]
    public void NoShortcutsAreDefined()
    {
        foreach (var id in s_ids)
        {
            Assert.Null(Of(id).ShortcutFor(macOs: false));
            Assert.Null(Of(id).ShortcutFor(macOs: true));
        }

        // The catalog never answers a key for them: no key press is any of these commands.
        var modifiers = new[] { Avalonia.Input.KeyModifiers.None, Avalonia.Input.KeyModifiers.Control, Avalonia.Input.KeyModifiers.Control | Avalonia.Input.KeyModifiers.Shift, Avalonia.Input.KeyModifiers.Alt, Avalonia.Input.KeyModifiers.Meta };
        foreach (var key in Enum.GetValues<Avalonia.Input.Key>())
        {
            foreach (var modifier in modifiers)
            {
                if (WriteCommandCatalog.Find(key, modifier, Avalonia.Input.KeyModifiers.Control, macOs: false) is { } found)
                {
                    Assert.DoesNotContain(found.Id, s_ids);
                }
            }
        }
    }

    [Fact]
    public void TheGermanTextIsInTheSieForm()
    {
        var du = new Regex(
            @"\b(du|dich|dir|dein\w*)\b|\b(wähle|gib|öffne|beende|verwirf|wende|lass|klicke|drücke|schließe|speichere|prüfe|versuche|gehe|starte|markiere|zeige|setze|nimm|verwende|warte)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var german = XDocument.Load(RepoPaths.Combine("src", "Raffica.Write", "Resources", "Strings.de.resx")).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? string.Empty);
        var english = XDocument.Load(RepoPaths.Combine("src", "Raffica.Write", "Resources", "Strings.en.resx")).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? string.Empty);
        string[] fresh =
        [
            "TabLayout", "GroupPageSetup", "GroupSections", "GroupHeaderFooter", "MenuLayout",
            .. s_ids.Select(id => Of(id).NameKey), .. s_ids.Select(id => Of(id).DescriptionKey!),
            "ReasonSectionNotInBody", "ReasonSectionPrefix", "ReasonSectionStartsHere", "ReasonSectionNothingToMerge", "ReasonSectionFirst", "NoteSectionNoChange", "NoteSectionBreakKey", "NoteCardStale",
            "MarginNormal", "MarginNarrow", "MarginModerate", "MarginWide", "OrientationPortrait", "OrientationLandscape", "ColumnsOne", "ColumnsTwo", "ColumnsThree",
            "BreakNextPage", "BreakContinuous", "BreakEvenPage", "BreakOddPage", "PageNumberWordPage", "PageNumberWordOf", "PageNumberPlaceFooter", "PageNumberPlaceHeader",
            "PageNumberAlignLeft", "PageNumberAlignCenter", "PageNumberAlignRight", "PageNumberItemFormat", "PageNumberCountsEveryPage",
            "NumPagesTitle", "NumPagesStartLabel", "NumPagesContinue", "NumPagesPrefixLabel", "NumPagesPrefixHint", "NumPagesPageStartLine", "NumPagesFollowOne", "NumPagesFollowMany", "NumPagesOwnOne", "NumPagesOwnMany", "NumPagesInvalidStart", "NumPagesInvalidPrefix", "NumPagesOk", "PageSetupTitle", "PsSizeLabel", "PsCustom", "PsWidthFormat", "PsHeightFormat", "PsOrientationLabel", "PsMarginsLabel", "PsTopFormat", "PsBottomFormat", "PsLeftFormat", "PsRightFormat", "PsColumnsLabel", "PsColumnCount", "PsColumnSpacingFormat", "PsColumnLine", "PsColumnsUnequal", "PsApplyLabel", "PsApplyThis", "PsApplyWhole", "PsApplyFrom", "PsFromHereNote", "PsInvalidValueFormat", "PsOutOfRangeFormat", "PsNoRoomForText", "PsColumnsDoNotFit", "PsOk", "NoteCardStale", "StatusStoryFormat", "StatusStorySharedOneFormat", "StatusStorySharedManyFormat", "NoteSharedFooter", "NoteSharedHeader",
        ];
        foreach (var key in fresh)
        {
            Assert.True(german.TryGetValue(key, out var text) && english.ContainsKey(key), key);
            var hit = du.Match(text!);
            Assert.False(hit.Success, $"{key}: \"{text}\" is in the du form ({hit.Value})");
            Assert.DoesNotContain("  ", text, StringComparison.Ordinal);
        }
    }

    private static IEnumerable<SectionCommandFacts> Facts()
    {
        foreach (var block in Enum.GetValues<SectionBlock>())
        {
            yield return new SectionCommandFacts(block, 1, 3, false, false, null);
            yield return new SectionCommandFacts(block, 0, 1, true, true, null);
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
                var rules = new WriteCommandRules(() => true, () => 1.0, () => true, () => state, sectionFacts: _ => facts);
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

        Assert.True(count > 400);

        // Without a document the start view shows no commands: nothing is told.
        var none = new WriteCommandRules(() => false, () => 1.0, () => true, () => new EditCommandState(null, false, false, false));
        Assert.All(s_ids, id => Assert.Null(none.ReasonFor(id)));
    }

    [Fact]
    public void TheRulesWordEachReasonAndSayYesWhenAllIsWell()
    {
        static string? Reason(string id, SectionBlock block, EditBlockedReason? blocked = null)
        {
            var rules = new WriteCommandRules(() => true, () => 1.0, () => true, () => new EditCommandState(blocked, true, true, true), sectionFacts: _ => new SectionCommandFacts(block, 1, 3, false, false, null));
            return rules.ReasonFor(id);
        }

        Assert.All(s_ids, id => Assert.Null(Reason(id, SectionBlock.None)));
        Assert.All(s_ids, id => Assert.Equal(WriteStrings.ReasonSectionPrefix, Reason(id, SectionBlock.PrefixDocument)));
        Assert.All(s_ids, id => Assert.Equal(WriteStrings.ReasonSectionNotInBody, Reason(id, SectionBlock.NotInBody)));
        Assert.Equal(WriteStrings.ReasonSectionStartsHere, Reason(WriteCommandIds.SectionBreak, SectionBlock.StartsHere));
        Assert.Equal(WriteStrings.ReasonSectionNothingToMerge, Reason(WriteCommandIds.RemoveSection, SectionBlock.NothingToMerge));
        Assert.Equal(WriteStrings.ReasonSectionFirst, Reason(WriteCommandIds.RemoveSection, SectionBlock.FirstSection));

        // The block comes first; a save disables the commands (they read the caret's section from the model as it is now).
        Assert.Equal(WriteStrings.ReasonEditOpening, Reason(WriteCommandIds.PageSize, SectionBlock.None, EditBlockedReason.StillOpening));
        Assert.Equal(WriteStrings.EditUnavailable, Reason(WriteCommandIds.PageSetup, SectionBlock.None, EditBlockedReason.Faulted));
        Assert.Equal(WriteStrings.ReasonEditSaving, Reason(WriteCommandIds.NumberPagesFrom, SectionBlock.None, EditBlockedReason.Saving));
    }

    [AvaloniaFact]
    public async Task EachCaretPlaceGivesItsDisabledReason()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, Break + "<w:r><w:t>one</w:t></w:r>", "<w:r><w:t>two</w:t></w:r>");

        // The caret in the first section: nothing to remove (two sections: the first one has no marker), no break where a section starts.
        Caret(session, tab, 0);
        Settle(window);
        Assert.False(Command(window, WriteCommandIds.RemoveSection).IsEnabled);
        Assert.Equal(WriteStrings.ReasonSectionFirst, Command(window, WriteCommandIds.RemoveSection).CurrentDisabledReason);
        Assert.False(Command(window, WriteCommandIds.SectionBreak).IsEnabled);
        Assert.Equal(WriteStrings.ReasonSectionStartsHere, Command(window, WriteCommandIds.SectionBreak).CurrentDisabledReason);
        Assert.True(Command(window, WriteCommandIds.PageSize).IsEnabled);

        // The second section: its marker can be removed, and no break goes where it starts (its first block).
        Caret(session, tab, 1);
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.RemoveSection).IsEnabled);
        Assert.False(Command(window, WriteCommandIds.SectionBreak).IsEnabled);
    }

    [AvaloniaFact]
    public async Task OneSectionHasNothingToMerge()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, "<w:r><w:t>one</w:t></w:r>", "<w:r><w:t>two</w:t></w:r>");
        Caret(session, tab, 1);
        Settle(window);
        Assert.False(Command(window, WriteCommandIds.RemoveSection).IsEnabled);
        Assert.Equal(WriteStrings.ReasonSectionNothingToMerge, Command(window, WriteCommandIds.RemoveSection).CurrentDisabledReason);
        Assert.True(Command(window, WriteCommandIds.SectionBreak).IsEnabled);
    }

    [AvaloniaFact]
    public async Task ThePrefixDocumentRefusesEverySectionCommand()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();

        // More blocks than the prefix open reads: the tab starts with a prefix of the file (and no runner started, so it stays one).
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(StyledDocx.Write(fixture.Folder, "long.docx", StyledDocx.Styles, [.. Enumerable.Range(0, 200).Select(i => $"<w:r><w:t>p{i}</w:t></w:r>")]))).Tab;
        Assert.True(tab.Docx.IsPrefix);
        var prefixBlocks = s_ids.Select(id => new SectionFactsSource().Read(tab, id).Block).ToList();
        Settle(window);

        // The facts say prefix for the file's own flag, with or without a session. The window may have started the full open meanwhile (the first sharp page starts it, a loaded machine gets there
        // early), so the window checks run only while the tab is still a prefix: it says it is still opening (the block comes first).
        Assert.All(prefixBlocks, block => Assert.Equal(SectionBlock.PrefixDocument, block));
        if (tab.Docx.IsPrefix && tab.Editor is null)
        {
            foreach (var id in s_ids)
            {
                Assert.False(Command(window, id).IsEnabled, id);
                Assert.Equal(WriteStrings.ReasonEditOpening, Command(window, id).CurrentDisabledReason);
            }

            Assert.False(window.RunCommand(WriteCommandIds.PageSize, "A4"));
            Assert.False(window.RunCommand(WriteCommandIds.RemoveSection));
        }
    }

    [AvaloniaFact]
    public async Task TheToggleCommandsReadTheSectionAtTheCaret()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, BreakWithFirstPage + "<w:r><w:t>one</w:t></w:r>", "<w:r><w:t>two</w:t></w:r>");

        // The first section has a different first page, the second has not: the switch follows the caret.
        Caret(session, tab, 0);
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.DifferentFirstPage).IsChecked);
        Caret(session, tab, 1);
        Settle(window);
        Assert.False(Command(window, WriteCommandIds.DifferentFirstPage).IsChecked);
        Assert.False(Command(window, WriteCommandIds.LeftRightPages).IsChecked);

        // Pressing it flips the section of the caret (and only that one), one history step, and the switch follows.
        Command(window, WriteCommandIds.DifferentFirstPage).Run!();
        WindowFixture.Pump();
        Settle(window);
        var sections = SectionList.Of(tab.Docx.Model);
        Assert.True(sections[0].Properties.GetFlag(Xw + "titlePg"));
        Assert.True(sections[1].Properties.GetFlag(Xw + "titlePg"));
        Assert.True(Command(window, WriteCommandIds.DifferentFirstPage).IsChecked);
        Assert.Equal(1, session.Editor.History.UndoCount);

        // Left and right pages is a document switch: on for the caret's section and every other one.
        Command(window, WriteCommandIds.LeftRightPages).Run!();
        WindowFixture.Pump();
        Settle(window);
        Assert.True(tab.Docx.Model.Settings!.EvenAndOddHeaders);
        Assert.True(Command(window, WriteCommandIds.LeftRightPages).IsChecked);
        Caret(session, tab, 0);
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.LeftRightPages).IsChecked);

        // Undo turns each back.
        session.Editor.History.Undo();
        Settle(window);
        Assert.False(Command(window, WriteCommandIds.LeftRightPages).IsChecked);
    }

    private static readonly XNamespace Xw = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    [AvaloniaFact]
    public async Task BadPayloadsReturnFalseWithoutAChange()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, "<w:r><w:t>one</w:t></w:r>", "<w:r><w:t>two</w:t></w:r>");
        Caret(session, tab, 1);
        Settle(window);
        var revision = tab.Docx.Model.Edits.Revision;
        var bytes = SerializedBody(tab);

        object?[] bad = [null, 5, 1.5, "", " ", "bogus", "a4", "A4 ", "Portrait", "0", "4", "01", "footer", "footer:left", "footer:left:bogus", "side:left:plain", "footer:top:plain", "footer:left:plain:extra", new object(), new[] { "A4" }];
        foreach (var id in s_payload)
        {
            foreach (var payload in bad)
            {
                Assert.False(window.RunCommand(id, payload), $"{id} with {payload ?? "null"}");
            }
        }

        // A command that takes no value never takes one, and a value command asked without a value does not run.
        Assert.False(window.RunCommand(WriteCommandIds.RemoveSection, "x"));
        Assert.False(window.RunCommand(WriteCommandIds.PageSetup, "x"));
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.Equal(0, session.Editor.History.UndoCount);
        Assert.Equal(bytes, SerializedBody(tab));
    }

    private static string SerializedBody(WriteTab tab) =>
        string.Join("|", tab.Docx.Model.Body.Blocks.OfType<Paragraph>().Select(p => p.SectionBreak is null ? "p" : "s")) + "/" + (tab.Docx.Model.Settings?.EvenAndOddHeaders ?? false) + "/" + tab.Docx.Model.HeadersFooters.Count;

    [AvaloniaFact]
    public async Task EveryPayloadCommandChangesTheDocumentInOneUndoStep()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, "<w:r><w:t>one</w:t></w:r>", "<w:r><w:t>two</w:t></w:r>", "<w:r><w:t>three</w:t></w:r>");
        Caret(session, tab, 1, 1);
        Settle(window);

        // One section: the setup commands reach the whole document (the document's final section).
        Assert.True(window.RunCommand(WriteCommandIds.PageSize, "A5"));
        WindowFixture.Pump();
        var setup = SectionPageSetup.Read(SectionList.Of(tab.Docx.Model)[0].Properties);
        Assert.Equal((8391, 11906), (setup.WidthTwips, setup.HeightTwips));
        Assert.Equal(1, session.Editor.History.UndoCount);

        Assert.True(window.RunCommand(WriteCommandIds.PageOrientation, "landscape"));
        WindowFixture.Pump();
        setup = SectionPageSetup.Read(SectionList.Of(tab.Docx.Model)[0].Properties);
        Assert.True(setup.Landscape);
        Assert.Equal((11906, 8391), (setup.WidthTwips, setup.HeightTwips));

        // A value of the paper keeps the orientation of the section.
        Assert.True(window.RunCommand(WriteCommandIds.PageSize, "Letter"));
        WindowFixture.Pump();
        setup = SectionPageSetup.Read(SectionList.Of(tab.Docx.Model)[0].Properties);
        Assert.Equal((15840, 12240), (setup.WidthTwips, setup.HeightTwips));
        Assert.True(setup.Landscape);

        Assert.True(window.RunCommand(WriteCommandIds.PageMargins, "Narrow"));
        WindowFixture.Pump();
        setup = SectionPageSetup.Read(SectionList.Of(tab.Docx.Model)[0].Properties);
        Assert.Equal((720, 720, 720, 720), (setup.TopTwips, setup.BottomTwips, setup.LeftTwips, setup.RightTwips));

        Assert.True(window.RunCommand(WriteCommandIds.PageColumns, "2"));
        WindowFixture.Pump();
        Assert.Equal(2, SectionPageSetup.Read(SectionList.Of(tab.Docx.Model)[0].Properties).Columns);
        Assert.Equal(5, session.Editor.History.UndoCount);

        // A section break splits at the caret's block and is one step; Remove section merges it again.
        Assert.True(window.RunCommand(WriteCommandIds.SectionBreak, "continuous"));
        WindowFixture.Pump();
        Assert.Equal(2, SectionList.Of(tab.Docx.Model).Count);
        Assert.Equal(6, session.Editor.History.UndoCount);
        Caret(session, tab, 2, 0);
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.RemoveSection).IsEnabled);
        Assert.True(window.RunCommand(WriteCommandIds.RemoveSection));
        WindowFixture.Pump();
        Assert.Single(SectionList.Of(tab.Docx.Model));
        Assert.Equal(7, session.Editor.History.UndoCount);

        // Insert page number: a footer with a PAGE field for the caret's section; one step.
        var parts = tab.Docx.Model.HeadersFooters.Count;
        Assert.True(window.RunCommand(WriteCommandIds.InsertPageNumber, "footer:center:pageOfTotal"));
        WindowFixture.Pump();
        Assert.True(tab.Docx.Model.HeadersFooters.Count > parts);
        Assert.Equal(8, session.Editor.History.UndoCount);

        // Number pages from here: a card that answers the default (start at 1, 1 2 3, no prefix); the caret's block starts a section that restarts at 1.
        window.SectionCards = new ScriptedCards { NumberingAnswer = new PageNumbering(1, PageNumberFormat.Arabic, string.Empty) };
        Caret(session, tab, 2, 0);
        Settle(window);
        Command(window, WriteCommandIds.NumberPagesFrom).Run!();
        WindowFixture.Pump();
        var final = SectionList.Of(tab.Docx.Model);
        Assert.Equal(2, final.Count);
        Assert.Equal("1", (string?)final[1].Properties.Get(Xw + "pgNumType")?.Attribute(Xw + "start"));
        Assert.Equal(9, session.Editor.History.UndoCount);
    }

    [AvaloniaFact]
    public async Task PageSetupAsksItsCardWithTheScopeAndRunsTheAnswer()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, "<w:r><w:t>one</w:t></w:r>");
        Caret(session, tab, 0);
        Settle(window);
        var revision = tab.Docx.Model.Edits.Revision;

        // The command is enabled; a card that cancels changes nothing.
        Assert.True(Command(window, WriteCommandIds.PageSetup).IsEnabled);
        var cards = new ScriptedCards();
        window.SectionCards = cards;
        Command(window, WriteCommandIds.PageSetup).Run!();
        WindowFixture.Pump();
        Assert.Equal(1, cards.PageSetupAsked);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);

        // A card that answers is run with its scope.
        cards.PageSetup = request => request with { };
        Command(window, WriteCommandIds.PageSetup).Run!();
        WindowFixture.Pump();
        Assert.Equal(2, cards.PageSetupAsked);
        Assert.Equal(ApplyScope.WholeDocument, cards.LastScope);
        Assert.Equal(3, SectionPageSetup.Read(SectionList.Of(tab.Docx.Model)[0].Properties).Columns);
    }

    private sealed class ScriptedCards : ISectionCards
    {
        public Func<PageSetupRequest, PageSetupRequest>? PageSetup { get; set; }

        public int PageSetupAsked { get; private set; }

        public ApplyScope LastScope { get; private set; }

        public TaskCompletionSource<PageNumbering?>? NumberingGate { get; set; }

        public PageNumbering? NumberingAnswer { get; set; }

        public int NumberingAsked { get; private set; }

        public Task<PageNumbering?> AskNumberingAsync(NumberPagesContext context)
        {
            NumberingAsked++;
            return NumberingGate?.Task ?? Task.FromResult(NumberingAnswer);
        }

        public Task<PageSetupRequest?> AskPageSetupAsync(PageSetupContext context)
        {
            PageSetupAsked++;
            LastScope = context.DefaultScope;
            var request = new PageSetupRequest(new PageSetupChange(context.Current with { Columns = 3 }, PageSetupParts.Columns), context.DefaultScope);
            return Task.FromResult(PageSetup?.Invoke(request));
        }
    }
    [AvaloniaFact]
    public async Task ACancelledNumberingCardChangesNothing()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, "<w:r><w:t>one</w:t></w:r>", "<w:r><w:t>two</w:t></w:r>");
        window.SectionCards = new ScriptedCards();
        Caret(session, tab, 1);
        Settle(window);
        var revision = tab.Docx.Model.Edits.Revision;
        Command(window, WriteCommandIds.NumberPagesFrom).Run!();
        WindowFixture.Pump();
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.Single(SectionList.Of(tab.Docx.Model));
    }

    [AvaloniaFact]
    public async Task StartupCreatesNoSectionWork()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        Assert.False(window.SectionFactsExist);
        var (_, tab, session) = await OpenAsync(fixture, "<w:r><w:t>one</w:t></w:r>", "<w:r><w:t>two</w:t></w:r>");
        window = fixture.Window;
        var revision = tab.Docx.Model.Edits.Revision;
        var parts = tab.Docx.Model.HeadersFooters.Count;
        var settings = tab.Docx.Model.Settings;
        Caret(session, tab, 0);
        Settle(window);
        WindowFixture.Pump();

        // The Home band is the active tab: no fact of a section command is read, no list of a Layout picker is asked and nothing of a section, a header, a footer or the settings part was made.
        Assert.False(window.SectionFactsExist);
        Assert.Equal(0, window.SectionFactReads);
        Assert.Equal(0, window.SectionPickerSet.ListReads);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.Equal(parts, tab.Docx.Model.HeadersFooters.Count);
        Assert.Same(settings, tab.Docx.Model.Settings);
        Assert.Equal(0, session.Editor.History.UndoCount);
        Assert.Null(window.Frame.RightPane);
        Assert.Null(window.SectionsOf(tab));
        Assert.False(window.IsSectionsOpen);

        // The first question makes the source and reads once; asking again for the same selection and revision reads nothing.
        Assert.True(Command(window, WriteCommandIds.PageSize).IsEnabled);
        Assert.True(window.SectionFactsExist);
        var reads = window.SectionFactReads;
        Assert.True(reads >= 1);
        _ = Command(window, WriteCommandIds.PageMargins).IsEnabled;
        _ = Command(window, WriteCommandIds.PageOrientation).IsEnabled;
        Assert.Equal(reads, window.SectionFactReads);
    }

    [AvaloniaFact]
    public async Task ThePickerListsAreAskedWhenTheyOpenAndShowTheCaretsSection()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, "<w:r><w:t>one</w:t></w:r>");
        Caret(session, tab, 0);
        Settle(window);
        var pickers = window.SectionPickerSet;
        Assert.Equal(0, pickers.ListReads);

        foreach (var id in s_payload)
        {
            var picker = Command(window, id).Picker;
            Assert.NotNull(picker);
        }

        Assert.Equal(0, pickers.ListReads);
        var size = Command(window, WriteCommandIds.PageSize).Picker!;
        Assert.Equal(["A4", "Letter", "A3", "A5", "Legal"], size.Items().Select(i => i.Id).ToArray());
        Assert.Equal(1, pickers.ListReads);
        Assert.Equal("Letter", size.SelectedId!());

        Assert.Equal(["1", "2", "3"], Command(window, WriteCommandIds.PageColumns).Picker!.Items().Select(i => i.Id).ToArray());
        Assert.Equal(["portrait", "landscape"], Command(window, WriteCommandIds.PageOrientation).Picker!.Items().Select(i => i.Id).ToArray());
        Assert.Equal(["Normal", "Narrow", "Moderate", "Wide"], Command(window, WriteCommandIds.PageMargins).Picker!.Items().Select(i => i.Id).ToArray());
        Assert.Equal(["nextPage", "continuous", "evenPage", "oddPage"], Command(window, WriteCommandIds.SectionBreak).Picker!.Items().Select(i => i.Id).ToArray());
        var numbers = Command(window, WriteCommandIds.InsertPageNumber).Picker!.Items();
        Assert.Equal(24, numbers.Count);
        Assert.Equal(numbers.Count, numbers.Select(i => i.Id).Distinct().Count());
        Assert.All(numbers, item => Assert.True(SectionPayloads.IsValid(WriteCommandIds.InsertPageNumber, item.Id), item.Id));
        Assert.DoesNotContain(numbers, i => i.Label.Contains(WriteStrings.PageNumberCountsEveryPage, StringComparison.Ordinal));

        // The picker of the orientation follows the section: Letter portrait now, landscape after the command.
        Assert.Equal("portrait", Command(window, WriteCommandIds.PageOrientation).Picker!.SelectedId!());
        Assert.True(window.RunCommand(WriteCommandIds.PageOrientation, "landscape"));
        WindowFixture.Pump();
        Settle(window);
        Assert.Equal("landscape", Command(window, WriteCommandIds.PageOrientation).Picker!.SelectedId!());
        Assert.Equal("Letter", Command(window, WriteCommandIds.PageSize).Picker!.SelectedId!());

        // A picker with no selection of its own shows the name of its command.
        Assert.Equal(Of(WriteCommandIds.SectionBreak).Name, Command(window, WriteCommandIds.SectionBreak).Picker!.SelectedLabel!());
    }

    // A tab as it is right after the open: the whole model is there, but no caret and no session exist yet (the session is made on the first caret or key).
    private static async Task<(WriteWindow Window, WriteTab Tab)> OpenFreshAsync(WindowFixture fixture, params string[] paragraphs)
    {
        var path = StyledDocx.Write(fixture.Folder, "fresh.docx", StyledDocx.Styles, paragraphs);
        var window = fixture.Show();
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(path)).Tab;
        tab.Runner.LeaseWaitTimeout = TimeSpan.FromSeconds(60);
        tab.Runner.Start();
        await WindowFixture.WaitAsync(() => tab.Runner.FullModelReady || tab.Runner.IsFaulted);
        Assert.True(tab.Runner.FullModelReady);
        WindowFixture.Pump();
        Assert.Null(tab.Editor);
        return (window, tab);
    }

    [AvaloniaFact]
    public async Task TheFirstClickOnAFreshDocumentAsksTheNumberingCardAndChangesNothingUntilItAnswers()
    {
        using var fixture = new WindowFixture();
        var (window, tab) = await OpenFreshAsync(fixture, "<w:r><w:t>one</w:t></w:r>", "<w:r><w:t>two</w:t></w:r>");
        var cards = new ScriptedCards { NumberingGate = new TaskCompletionSource<PageNumbering?>() };
        window.SectionCards = cards;
        var revision = tab.Docx.Model.Edits.Revision;

        // The command is enabled (nothing is read yet) and the first click makes the session to describe the place, asks the card and waits: nothing changes and no default numbering runs.
        Assert.True(window.RunCommand(WriteCommandIds.NumberPagesFrom));
        WindowFixture.Pump();
        Assert.Equal(1, cards.NumberingAsked);
        Assert.NotNull(tab.Editor);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.Single(SectionList.Of(tab.Docx.Model));

        // The answer is what runs: here start at 7 with the format i ii iii.
        cards.NumberingGate.SetResult(new PageNumbering(7, PageNumberFormat.LowerRoman, string.Empty));
        await WindowFixture.WaitAsync(() => tab.Docx.Model.Edits.Revision != revision);
        WindowFixture.Pump();
        var numbering = SectionList.Of(tab.Docx.Model)[0].Properties.Get(Xw + "pgNumType");
        Assert.Equal("7", (string?)numbering?.Attribute(Xw + "start"));
        Assert.Equal("lowerRoman", (string?)numbering?.Attribute(Xw + "fmt"));

        // A cancelled card changes nothing.
        var second = new ScriptedCards { NumberingGate = new TaskCompletionSource<PageNumbering?>() };
        window.SectionCards = second;
        var after = tab.Docx.Model.Edits.Revision;
        Assert.True(window.RunCommand(WriteCommandIds.NumberPagesFrom));
        second.NumberingGate.SetResult(null);
        WindowFixture.Pump();
        Assert.Equal(1, second.NumberingAsked);
        Assert.Equal(after, tab.Docx.Model.Edits.Revision);
    }

    [AvaloniaFact]
    public async Task TheFirstClickOnPageSetupOfAFreshDocumentAsksItsCardAndSaysNothingIsWrong()
    {
        using var fixture = new WindowFixture();
        var (window, tab) = await OpenFreshAsync(fixture, "<w:r><w:t>one</w:t></w:r>");
        var cards = new ScriptedCards();
        window.SectionCards = cards;
        var revision = tab.Docx.Model.Edits.Revision;

        // The card answers "cancel" (null): it was asked with the setup of the caret's section, nothing changed, and no "could not be applied" note was shown.
        Assert.True(window.RunCommand(WriteCommandIds.PageSetup));
        WindowFixture.Pump();
        Assert.Equal(1, cards.PageSetupAsked);
        Assert.Equal(ApplyScope.WholeDocument, cards.LastScope);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.NotEqual(WriteStrings.NoteStructureFailed, window.Status.Hint);
    }

    [AvaloniaFact]
    public async Task ACardCommandThatCannotDescribeThePlaceShowsANoteAndNeverRunsADefault()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(StyledDocx.Write(fixture.Folder, "long2.docx", StyledDocx.Styles, [.. Enumerable.Range(0, 200).Select(i => $"<w:r><w:t>p{i}</w:t></w:r>")]))).Tab;
        var cards = new ScriptedCards { NumberingGate = new TaskCompletionSource<PageNumbering?>() };
        window.SectionCards = cards;
        var wasPrefix = tab.Docx.IsPrefix;
        var revision = tab.Docx.Model.Edits.Revision;

        // A prefix document (or one that is still opening) cannot be described: the card is not asked, a note says why and the document is as it was. The full open may have started on a loaded
        // machine, then there is nothing to check here (the view-level test of the prefix pins the facts).
        if (wasPrefix && tab.Editor is null)
        {
            Command(window, WriteCommandIds.NumberPagesFrom).Run!();
            WindowFixture.Pump();
            if (tab.Docx.IsPrefix)
            {
                Assert.Equal(0, cards.NumberingAsked);
                Assert.False(string.IsNullOrEmpty(window.Status.Hint));
                Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
                Assert.Single(SectionList.Of(tab.Docx.Model));
            }
        }
    }
}
