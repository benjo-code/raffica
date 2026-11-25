using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Raffica.Shell.Frame;
using Raffica.Write;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Model;
using Raffica.Write.Platform.MacOS;
using Raffica.Write.Settings;
using Raffica.Write.Viewing;
using Raffica.WriteApp.Tests.Platform;
using Raffica.WriteApp.Tests.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Commands;

/// <summary>
/// The table commands (milestone 4.5a, Task 8): ids, icons, wording, the rules with a reason for every disabled state, the window run path with its one history step each, the grid of Insert table and the start cost.
/// The document is <see cref="ObjectDocx"/>: three paragraphs of text and picture, then a table of three rows by three columns (block 3) with <c>c00</c> to <c>c22</c>, then a paragraph.
/// </summary>
public sealed class TableCommandTests
{
    private static readonly string[] s_ids =
    [
        "table.insert", "table.rowAbove", "table.rowBelow", "table.colLeft", "table.colRight", "table.deleteRows", "table.deleteColumns", "table.delete", "table.merge", "table.split",
        "table.borders", "table.shading", "table.headerRow", "table.properties", "table.distributeColumns", "table.rowUp", "table.rowDown", "table.selectRow", "table.selectColumn", "table.selectTable",
    ];

    private static CatalogCommand Of(string id) => WriteCommandCatalog.Commands.Single(c => c.Id == id);

    private static CommandDefinition Command(WriteWindow window, string id) =>
        window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id);

    private static async Task<(WriteWindow Window, WriteTab Tab, EditSession Session)> OpenAsync(WindowFixture fixture)
    {
        var path = ObjectDocx.Write(fixture.Folder);
        var window = fixture.Show();
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(path)).Tab;
        tab.Runner.LeaseWaitTimeout = TimeSpan.FromSeconds(60);
        tab.Runner.Start();
        await WindowFixture.WaitAsync(() => tab.Runner.FullModelReady || tab.Runner.IsFaulted);
        Assert.True(tab.Runner.FullModelReady);
        await WindowFixture.WaitAsync(() => window.ActiveView is { IsLayoutComplete: true });
        var session = tab.EnsureEditor();
        _ = session.Selection;
        return (window, tab, session);
    }

    private static Table TableOf(WriteTab tab) => (Table)tab.Docx.Model.Body.Blocks[3];

    private static TableRef Ref() => new(StoryRef.Body, 3);

    private static Paragraph CellParagraph(WriteTab tab, int row, int column) => TableGridMap.Of(TableOf(tab)).At(row, column)!.Cell.Blocks.OfType<Paragraph>().First();

    private static string CellText(WriteTab tab, int row, int column) => string.Concat(CellParagraph(tab, row, column).Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<TextContent>().Select(t => t.Text));

    private static void CaretIn(EditSession session, WriteTab tab, int row, int column)
    {
        var at = new TextPosition(StoryRef.Body, CellParagraph(tab, row, column), 0);
        session.SetSelection(new TextRange(at, at));
    }

    private static void CaretOutside(EditSession session, WriteTab tab)
    {
        var at = new TextPosition(StoryRef.Body, (Paragraph)tab.Docx.Model.Body.Blocks[0], 0);
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

    private static int Rows(WriteTab tab) => TableGridMap.Of(TableOf(tab)).RowCount;

    private static int Columns(WriteTab tab) => TableGridMap.Of(TableOf(tab)).Columns;

    // ---- ids, keys, wording -------------------------------------------------------------------------------------------------

    [Fact]
    public void TheIdsAreCatalogEntriesOfTheInsertAndTableTabsWithNoKeys()
    {
        Assert.All(s_ids, id => Assert.Single(WriteCommandCatalog.Commands, c => c.Id == id));
        Assert.All(s_ids, id => Assert.True(WriteCommandIds.IsTable(id), id));
        Assert.Equal(["table.insert", "table.borders", "table.shading", "table.properties"], s_ids.Where(WriteCommandIds.TakesPayload).ToArray());
        Assert.Equal(["table.headerRow"], s_ids.Where(id => Of(id).Toggle).ToArray());
        Assert.All(s_ids, id => Assert.Null(Of(id).ShortcutFor(macOs: false)));
        Assert.All(s_ids, id => Assert.Null(Of(id).ShortcutFor(macOs: true)));
        Assert.Equal(WriteRibbon.InsertLetter, Of("table.insert").Tab.Letter);
        Assert.All(s_ids.Where(id => id != "table.insert"), id => Assert.Equal(WriteRibbon.TableLetter, Of(id).Tab.Letter));
        Assert.Equal("T", WriteRibbon.TableLetter);
        Assert.Equal(["H", "I", "T", "P", "V"], WriteCommandCatalog.Tabs.Select(t => t.Letter).ToArray());
        Assert.False(WriteCommandIds.IsTable("note.back"));
        Assert.False(WriteCommandIds.IsSection("table.delete"));

        // The Table tab: the field codes are unique and none begins another.
        var codes = WriteCommandCatalog.Tabs.Single(t => t.Letter == WriteRibbon.TableLetter).Groups.SelectMany(g => g.Commands.Select(c => c.FieldCode)).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, a => Assert.DoesNotContain(codes, b => a != b && b.StartsWith(a, StringComparison.Ordinal)));
        WriteRibbon.Create(new Dictionary<string, Action>());

        // No key press is any of them: no Ctrl plus Alt (AltGr is text) and no other key.
        var modifiers = new[] { KeyModifiers.None, KeyModifiers.Control, KeyModifiers.Control | KeyModifiers.Alt, KeyModifiers.Control | KeyModifiers.Shift, KeyModifiers.Alt, KeyModifiers.Meta };
        foreach (var key in Enum.GetValues<Key>())
        {
            foreach (var modifier in modifiers)
            {
                if (WriteCommandCatalog.Find(key, modifier, KeyModifiers.Control, macOs: false) is { } found)
                {
                    Assert.DoesNotContain(found.Id, s_ids);
                }
            }
        }
    }

    [AvaloniaFact]
    public void EveryTableCommandHasItsOwnIconBothLanguagesAndAMacMenuLine()
    {
        foreach (var group in WriteCommandCatalog.Commands.GroupBy(c => c.Icon).Where(g => g.Count() > 1))
        {
            Assert.Fail($"Icon {group.Key} serves {string.Join(", ", group.Select(c => c.Id))}");
        }

        var keys = XDocument.Load(RepoPaths.Combine("src", "Raffica.Shell", "Styles", "Icons.axaml")).Root!.Elements()
            .Select(e => (string?)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")))
            .Where(k => k is not null && k.StartsWith("Raffica.Icon.", StringComparison.Ordinal)).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());

        // No two of the twenty icons draw the same path.
        var paths = XDocument.Load(RepoPaths.Combine("src", "Raffica.Shell", "Styles", "Icons.axaml")).Root!.Elements()
            .ToDictionary(e => (string)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!, e => e.Value.Trim());
        var drawn = s_ids.Select(id => paths["Raffica.Icon." + Of(id).Icon]).ToList();
        Assert.Equal(s_ids.Length, drawn.Distinct().Count());
        foreach (var id in s_ids)
        {
            var command = Of(id);
            Assert.Contains("Raffica.Icon." + command.Icon, keys);
            Assert.True(Application.Current!.TryGetResource("Raffica.Icon." + command.Icon, Application.Current!.ActualThemeVariant, out var value), id);
            var geometry = Assert.IsAssignableFrom<Geometry>(value);
            Assert.True(geometry.Bounds.Width > 0 && geometry.Bounds.Height > 0, id);
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
            }

            foreach (var key in new[] { "TabTable", "MenuTable", "GroupTable", "GroupTableRows", "GroupTableColumns", "GroupTableCells", "GroupTableWhole" })
            {
                Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn(key, culture)), key);
            }
        }

        var window = new WriteMacMenuTests.FakeWindow { HasTab = true };
        window.Allowed.UnionWith(s_ids);
        var menu = new WriteMacMenu(() => window, () => Task.CompletedTask, WriteMenuPart.Window);
        menu.Refresh();
        var table = menu.Menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == WriteStrings.MenuTable).Menu!.Items.OfType<NativeMenuItem>().ToList();
        foreach (var id in s_ids.Where(id => id is not ("table.borders" or "table.shading")))
        {
            var item = Assert.Single(table, i => i.Header == Of(id).Name);
            Assert.Null(item.Gesture);
            Assert.True(item.Command!.CanExecute(null), id);
            item.Command.Execute(null);
            Assert.Equal("run " + id, window.Calls[^1]);
        }

        // Borders and Shading are submenus with their values and Borders has the card as its own item.
        var borders = Assert.Single(table, i => i.Header == Of("table.borders").Name);
        Assert.Equal(TablePayloads.BorderPresets.Count, borders.Menu!.Items.Count);
        var more = Assert.Single(table, i => i.Header == WriteStrings.TableBordersMore);
        more.Command!.Execute(null);
        Assert.Equal("run table.borders", window.Calls[^1]);
        var shading = Assert.Single(table, i => i.Header == Of("table.shading").Name);
        shading.Menu!.Items.OfType<NativeMenuItem>().First().Command!.Execute(null);
        Assert.Equal("run table.shading none", window.Calls[^1]);

        window.Allowed.Clear();
        menu.Refresh();
        Assert.All(table.Where(item => item.Command is not null), item => Assert.False(item.Command!.CanExecute(null)));
    }

    [Fact]
    public void EveryRefusalOfTheEditorHasWordsInBothLanguages()
    {
        foreach (var language in new[] { "en", "de" })
        {
            var culture = CultureInfo.GetCultureInfo(language);
            foreach (var refusal in Enum.GetValues<TableRefusal>())
            {
                Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn(TableWording.KeyOf(refusal), culture)), $"{refusal} in {language}");
            }
        }

        Assert.All(Enum.GetValues<TableRefusal>(), r => Assert.False(string.IsNullOrWhiteSpace(TableWording.ReasonText(r))));
        Assert.All(Enum.GetValues<TableBlock>().Where(b => b != TableBlock.None), b => Assert.False(string.IsNullOrWhiteSpace(TableWording.ReasonText(new TableCommandFacts(b, TableRefusal.NotMerged))), b.ToString()));
        Assert.Null(TableWording.ReasonText(TableCommandFacts.Permissive));
    }

    [Fact]
    public void TheGermanTextIsInTheSieFormAndSaysEveryRefusal()
    {
        var du = new Regex(
            @"\b(du|dich|dir|dein\w*)\b|\b(wähle|gib|öffne|beende|verwirf|wende|lass|klicke|drücke|schließe|speichere|prüfe|versuche|gehe|starte|markiere|zeige|setze|nimm|verwende|warte)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var german = XDocument.Load(RepoPaths.Combine("src", "Raffica.Write", "Resources", "Strings.de.resx")).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? string.Empty);
        var english = XDocument.Load(RepoPaths.Combine("src", "Raffica.Write", "Resources", "Strings.en.resx")).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? string.Empty);
        var fresh = german.Keys.Where(k => k.StartsWith("ReasonTable", StringComparison.Ordinal) || k.StartsWith("Ti", StringComparison.Ordinal) && k.Length > 2 && char.IsUpper(k[2]) || k.StartsWith("Tb", StringComparison.Ordinal) && char.IsUpper(k[2])
            || k.StartsWith("Tp", StringComparison.Ordinal) && char.IsUpper(k[2]) || k.StartsWith("TableBorders", StringComparison.Ordinal) || k.StartsWith("TableGrid", StringComparison.Ordinal)
            || k.StartsWith("Table", StringComparison.Ordinal) || k.StartsWith("DescTable", StringComparison.Ordinal) || k.StartsWith("CmdTable", StringComparison.Ordinal) || k.StartsWith("GroupTable", StringComparison.Ordinal)).ToList();
        Assert.True(fresh.Count >= 90, fresh.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var key in fresh)
        {
            Assert.True(english.ContainsKey(key), key);
            var text = german[key];
            var hit = du.Match(text);
            Assert.False(hit.Success, $"{key}: \"{text}\" is in the du form ({hit.Value})");
            Assert.DoesNotContain("  ", text, StringComparison.Ordinal);
        }
    }

    // ---- rules ---------------------------------------------------------------------------------------------------------------

    private static IEnumerable<TableCommandFacts> Facts() =>
        Enum.GetValues<TableBlock>().Where(b => b != TableBlock.Refused).Select(b => new TableCommandFacts(b))
            .Concat(Enum.GetValues<TableRefusal>().Select(r => new TableCommandFacts(TableBlock.Refused, r)));

    [Fact]
    public void EveryDisabledStateHasAReason()
    {
        var count = 0;
        foreach (var blocked in new EditBlockedReason?[] { null, EditBlockedReason.StillOpening, EditBlockedReason.Saving, EditBlockedReason.Faulted })
        {
            foreach (var facts in Facts())
            {
                var state = new EditCommandState(blocked, true, true, true);
                var rules = new WriteCommandRules(() => true, () => 1.0, () => true, () => state, tableFacts: _ => facts);
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

        Assert.True(count >= 400);

        var none = new WriteCommandRules(() => false, () => 1.0, () => true, () => new EditCommandState(null, false, false, false));
        Assert.All(s_ids, id => Assert.Null(none.ReasonFor(id)));
    }

    [Fact]
    public void TheRulesWordEachReason()
    {
        static string? Reason(string id, TableCommandFacts facts, EditBlockedReason? blocked = null)
        {
            var rules = new WriteCommandRules(() => true, () => 1.0, () => true, () => new EditCommandState(blocked, true, true, true), tableFacts: _ => facts);
            return rules.ReasonFor(id);
        }

        Assert.All(s_ids, id => Assert.Null(Reason(id, TableCommandFacts.Permissive)));
        Assert.Equal(WriteStrings.ReasonTableNotInTable, Reason(WriteCommandIds.TableMerge, new TableCommandFacts(TableBlock.NotInTable)));
        Assert.Equal(WriteStrings.ReasonTablePrefixDocument, Reason(WriteCommandIds.TableInsert, new TableCommandFacts(TableBlock.PrefixDocument)));
        Assert.Equal(WriteStrings.ReasonTableWritingViewWidths, Reason(WriteCommandIds.TableProperties, new TableCommandFacts(TableBlock.WritingViewWidths)));
        Assert.Equal(WriteStrings.ReasonTableOneRow, Reason(WriteCommandIds.TableRowUp, new TableCommandFacts(TableBlock.NeedsOneRow)));
        Assert.Equal(WriteStrings.ReasonTableNothingToMerge, Reason(WriteCommandIds.TableMerge, new TableCommandFacts(TableBlock.Refused, TableRefusal.NothingToMerge)));
        Assert.Equal(WriteStrings.ReasonTableNotMerged, Reason(WriteCommandIds.TableSplit, new TableCommandFacts(TableBlock.Refused, TableRefusal.NotMerged)));
        Assert.Equal(WriteStrings.ReasonTableTooManyColumns, Reason(WriteCommandIds.TableColumnLeft, new TableCommandFacts(TableBlock.Refused, TableRefusal.TooManyColumns)));
        Assert.Equal(WriteStrings.ReasonTableTooManyRows, Reason(WriteCommandIds.TableRowBelow, new TableCommandFacts(TableBlock.Refused, TableRefusal.TooManyRows)));
        Assert.Equal(WriteStrings.ReasonTableUnsupportedStructure, Reason(WriteCommandIds.TableBorders, new TableCommandFacts(TableBlock.Refused, TableRefusal.UnsupportedStructure)));
        Assert.Equal(WriteStrings.ReasonEditOpening, Reason(WriteCommandIds.TableDelete, TableCommandFacts.Permissive, EditBlockedReason.StillOpening));
        Assert.Equal(WriteStrings.EditUnavailable, Reason(WriteCommandIds.TableInsert, TableCommandFacts.Permissive, EditBlockedReason.Faulted));
        Assert.Equal(WriteStrings.ReasonEditSaving, Reason(WriteCommandIds.TableInsert, TableCommandFacts.Permissive, EditBlockedReason.Saving));
    }

    // ---- the window run path --------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task OutsideATableOnlyInsertIsOfferedAndEveryOtherCommandSaysWhy()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        CaretOutside(session, tab);
        Settle(window);

        Assert.True(Command(window, WriteCommandIds.TableInsert).IsEnabled);
        foreach (var id in s_ids.Where(id => id != WriteCommandIds.TableInsert))
        {
            Assert.False(Command(window, id).IsEnabled, id);
            Assert.Equal(WriteStrings.ReasonTableNotInTable, Command(window, id).CurrentDisabledReason);
            Assert.False(window.RunCommand(id));
        }

        CaretIn(session, tab, 1, 1);
        Settle(window);
        foreach (var id in new[] { "table.rowAbove", "table.rowBelow", "table.colLeft", "table.colRight", "table.deleteRows", "table.deleteColumns", "table.delete", "table.borders", "table.shading", "table.headerRow", "table.properties", "table.distributeColumns", "table.rowUp", "table.rowDown", "table.selectRow", "table.selectColumn", "table.selectTable" })
        {
            Assert.True(Command(window, id).IsEnabled, id + ": " + Command(window, id).CurrentDisabledReason);
        }

        // One cell is nothing to merge, and an unmerged cell nothing to split: each says so.
        Assert.False(Command(window, WriteCommandIds.TableMerge).IsEnabled);
        Assert.Equal(WriteStrings.ReasonTableNothingToMerge, Command(window, WriteCommandIds.TableMerge).CurrentDisabledReason);
        Assert.False(Command(window, WriteCommandIds.TableSplit).IsEnabled);
        Assert.Equal(WriteStrings.ReasonTableNotMerged, Command(window, WriteCommandIds.TableSplit).CurrentDisabledReason);
    }

    [AvaloniaFact]
    public async Task TheStructureCommandsRunFromTheRibbonAndEachIsOneUndoStep()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        CaretIn(session, tab, 1, 1);
        Settle(window);
        var steps = session.Editor.History.UndoCount;

        Command(window, WriteCommandIds.TableRowBelow).Run!();
        WindowFixture.Pump();
        Assert.Equal(4, Rows(tab));
        Assert.Equal(steps + 1, session.Editor.History.UndoCount);

        Command(window, WriteCommandIds.TableColumnRight).Run!();
        WindowFixture.Pump();
        Assert.Equal(4, Columns(tab));
        Assert.Equal(steps + 2, session.Editor.History.UndoCount);

        CaretIn(session, tab, 0, 0);
        Settle(window);
        Command(window, WriteCommandIds.TableRowDown).Run!();
        WindowFixture.Pump();
        Assert.Equal("c10", CellText(tab, 0, 0));
        Assert.Equal(steps + 3, session.Editor.History.UndoCount);

        CaretIn(session, tab, 0, 0);
        Settle(window);
        Command(window, WriteCommandIds.TableDeleteColumns).Run!();
        WindowFixture.Pump();
        Assert.Equal(3, Columns(tab));
        Command(window, WriteCommandIds.TableDeleteRows).Run!();
        WindowFixture.Pump();
        Assert.Equal(3, Rows(tab));

        for (var i = 0; i < 5; i++)
        {
            Assert.NotNull(session.Editor.History.Undo());
        }

        Assert.Equal(3, Rows(tab));
        Assert.Equal(3, Columns(tab));
        Assert.Equal("c00", CellText(tab, 0, 0));
        Assert.Equal(steps, session.Editor.History.UndoCount);
    }

    [AvaloniaFact]
    public async Task SelectingCellsMergingAndSplittingKeepTheTextAndFollowTheCellSelection()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        Assert.True(session.SelectCells(Ref(), (0, 0), (0, 1)));
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.TableMerge).IsEnabled);
        Assert.False(Command(window, WriteCommandIds.TableSplit).IsEnabled);

        Command(window, WriteCommandIds.TableMerge).Run!();
        WindowFixture.Pump();
        Assert.Null(session.ObjectSelection);
        Assert.Contains("c00", CellText(tab, 0, 0), StringComparison.Ordinal);

        CaretIn(session, tab, 0, 0);
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.TableSplit).IsEnabled);
        Command(window, WriteCommandIds.TableSplit).Run!();
        WindowFixture.Pump();
        Assert.DoesNotContain(TableGridMap.Of(TableOf(tab)).Cells, c => c.ColumnSpan > 1);

        // The three selecting commands change no document and select the row, the column and the table.
        var revision = tab.Docx.Model.Edits.Revision;
        CaretIn(session, tab, 1, 2);
        Settle(window);
        Command(window, WriteCommandIds.TableSelectRow).Run!();
        Assert.Equal((1, 0), session.CellAnchor);
        Assert.Equal((1, 2), session.CellFocus);
        Command(window, WriteCommandIds.TableSelectColumn).Run!();
        Assert.Equal((0, 2), session.CellAnchor);
        Assert.Equal((2, 2), session.CellFocus);
        Command(window, WriteCommandIds.TableSelectTable).Run!();
        Assert.Equal((0, 0), session.CellAnchor);
        Assert.Equal((2, 2), session.CellFocus);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
    }

    [AvaloniaFact]
    public async Task BordersAndShadingTakeOnlyValuesOfTheirListAndKeepTheSelectedCells()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        Assert.True(session.SelectCells(Ref(), (0, 0), (1, 1)));
        Settle(window);
        var steps = session.Editor.History.UndoCount;
        var revision = tab.Docx.Model.Edits.Revision;

        // A payload outside the list, a colour that is not on it, a spec the card could not make and a missing payload all return false and change nothing.
        Assert.False(window.RunCommand(WriteCommandIds.TableBorders, "sparkles"));
        Assert.False(window.RunCommand(WriteCommandIds.TableBorders, BorderSpec.All(color: 0xFF123456)));
        Assert.False(window.RunCommand(WriteCommandIds.TableBorders, BorderSpec.All(widthEighths: 5)));
        Assert.False(window.RunCommand(WriteCommandIds.TableBorders, null));
        Assert.False(window.RunCommand(WriteCommandIds.TableShading, "123456"));
        Assert.False(window.RunCommand(WriteCommandIds.TableShading, 7));
        Assert.False(window.RunCommand(WriteCommandIds.TableInsert, new TableSize(64, 3)));
        Assert.False(window.RunCommand(WriteCommandIds.TableInsert, new TableSize(3, 501)));
        Assert.False(window.RunCommand(WriteCommandIds.TableInsert, new TableSize(0, 3)));
        Assert.False(window.RunCommand(WriteCommandIds.TableProperties, new TableProperties()));
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);

        Assert.True(window.RunCommand(WriteCommandIds.TableBorders, "box"));
        Assert.True(window.RunCommand(WriteCommandIds.TableShading, "FFFF00"));
        WindowFixture.Pump();
        Assert.Equal(steps + 2, session.Editor.History.UndoCount);
        var shaded = TableGridMap.Of(TableOf(tab)).At(1, 1)!.Cell;
        Assert.Equal("FFFF00", (string?)shaded.Properties.Get(XName.Get("shd", "http://schemas.openxmlformats.org/wordprocessingml/2006/main"))?.Attribute(XName.Get("fill", "http://schemas.openxmlformats.org/wordprocessingml/2006/main")));

        // A formatting command keeps the cells that were selected: the paragraphs are new objects, the place is the same.
        Assert.IsType<CellSelection>(session.ObjectSelection);
        Assert.Equal(((0, 0), (1, 1)), (session.CellAnchor!.Value, session.CellFocus!.Value));
        Assert.True(window.RunCommand(WriteCommandIds.TableShading, "none"));
        Assert.Null(shadedFill(tab));

        static string? shadedFill(WriteTab t) =>
            (string?)TableGridMap.Of(TableOf(t)).At(1, 1)!.Cell.Properties.Get(XName.Get("shd", "http://schemas.openxmlformats.org/wordprocessingml/2006/main"))?.Attribute(XName.Get("fill", "http://schemas.openxmlformats.org/wordprocessingml/2006/main"));
    }

    [AvaloniaFact]
    public async Task TheHeaderRowSwitchShowsItsStateAndTogglesInOneStep()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        CaretIn(session, tab, 1, 0);
        Settle(window);
        Assert.False(Command(window, WriteCommandIds.TableHeaderRow).IsChecked);

        Command(window, WriteCommandIds.TableHeaderRow).Run!();
        WindowFixture.Pump();
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.TableHeaderRow).IsChecked);
        Assert.NotNull(((TableRow)TableOf(tab).Rows[0]).Properties.Get(XName.Get("tblHeader", "http://schemas.openxmlformats.org/wordprocessingml/2006/main")));

        // The first row is a header now: a row cannot move across its border, and the command says so.
        CaretIn(session, tab, 1, 0);
        Settle(window);
        Assert.False(Command(window, WriteCommandIds.TableRowUp).IsEnabled);
        Assert.Equal(WriteStrings.ReasonTableHeaderRowBoundary, Command(window, WriteCommandIds.TableRowUp).CurrentDisabledReason);

        Command(window, WriteCommandIds.TableHeaderRow).Run!();
        WindowFixture.Pump();
        Settle(window);
        Assert.False(Command(window, WriteCommandIds.TableHeaderRow).IsChecked);
    }

    [AvaloniaFact]
    public async Task InsertTableRunsWithTheSizeOfTheGridAndOfTheCard()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        CaretOutside(session, tab);
        Settle(window);
        var blocks = tab.Docx.Model.Body.Blocks.Count(b => b is Table);

        Assert.True(window.RunCommand(WriteCommandIds.TableInsert, new TableSize(4, 2)));
        WindowFixture.Pump();
        Assert.Equal(blocks + 1, tab.Docx.Model.Body.Blocks.Count(b => b is Table));
        var fresh = tab.Docx.Model.Body.Blocks.OfType<Table>().First();
        Assert.Equal((4, 2), (TableGridMap.Of(fresh).Columns, TableGridMap.Of(fresh).RowCount));
        Assert.True(session.Editor.History.CanUndo);
        Assert.NotNull(session.Editor.History.Undo());
        CaretOutside(session, tab);
        Assert.Equal(blocks, tab.Docx.Model.Body.Blocks.Count(b => b is Table));

        // The card for any size: answered with a size it runs the same command; cancelled it changes nothing.
        var cards = new FakeCards { Insert = new TableSize(63, 5) };
        window.TableCards = cards;
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.TableInsert).IsEnabled, Command(window, WriteCommandIds.TableInsert).CurrentDisabledReason);
        Command(window, WriteCommandIds.TableInsert).Run!.Invoke();
        // Insert opens the grid, not the card: the card is what "More…" of the grid opens.
        Assert.Equal(0, cards.InsertAsked);
        window.TableGrid!.MoreButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await WindowFixture.WaitAsync(() => cards.InsertAsked == 1);
        await WindowFixture.WaitAsync(() => tab.Docx.Model.Body.Blocks.Count(b => b is Table) == blocks + 1);
        var wide = tab.Docx.Model.Body.Blocks.OfType<Table>().First();
        Assert.Equal((63, 5), (TableGridMap.Of(wide).Columns, TableGridMap.Of(wide).RowCount));
    }

    [AvaloniaFact]
    public async Task TheWritingViewRefusesTheWidthCommandsWithTheirReasonAndAllowsTheRest()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        CaretIn(session, tab, 1, 1);
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.TableDistributeColumns).IsEnabled);
        Assert.True(Command(window, WriteCommandIds.TableProperties).IsEnabled);

        window.SetView(WriteView.Writing);
        Settle(window);
        var revision = tab.Docx.Model.Edits.Revision;
        foreach (var id in new[] { WriteCommandIds.TableDistributeColumns, WriteCommandIds.TableProperties })
        {
            Assert.False(Command(window, id).IsEnabled, id);
            Assert.Equal(WriteStrings.ReasonTableWritingViewWidths, Command(window, id).CurrentDisabledReason);
            Assert.False(window.RunCommand(id));
        }

        // The view refuses too (a stale click and the macOS menu reach it), with the same words and no change.
        var notes = new List<string>();
        window.ActiveView!.StructureNote += notes.Add;
        Assert.True(window.ActiveView.RunTableCommand(WriteCommandIds.TableDistributeColumns));
        Assert.Equal([WriteStrings.ReasonTableWritingViewWidths], notes);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);

        // Structure and formatting need no drawn width: they run in the writing view.
        Assert.True(Command(window, WriteCommandIds.TableRowBelow).IsEnabled);
        Assert.True(window.RunCommand(WriteCommandIds.TableShading, "FF0000"));
        Command(window, WriteCommandIds.TableRowBelow).Run!();
        WindowFixture.Pump();
        Assert.Equal(4, Rows(tab));

        window.SetView(WriteView.Page);
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.TableDistributeColumns).IsEnabled);
    }

    [AvaloniaFact]
    public async Task TheWidthCommandsFreezeTheWidthsThePageDrawsAndPropertiesAreOneStep()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        CaretIn(session, tab, 1, 1);
        Settle(window);
        var steps = session.Editor.History.UndoCount;

        // The table is autofit: the drawn widths come from the object map of the page and are what Distribute works on.
        var drawn = PageView.DrawnColumnTwips(session.Editor, window.ActiveView!.Runner.PageLayout, Ref());
        Assert.NotNull(drawn);
        Assert.Equal(3, drawn.Count);

        var cards = new FakeCards();
        window.TableCards = cards;
        cards.Properties = new TableProperties(RowHeight: new TableRowHeight(1, 1, 700, Exact: false), HeaderRow: true);
        Command(window, WriteCommandIds.TableProperties).Run!();
        await WindowFixture.WaitAsync(() => cards.PropertiesAsked == 1);
        await WindowFixture.WaitAsync(() => session.Editor.History.UndoCount == steps + 1);
        Assert.Equal(0, cards.LastContext!.Facts.RowHeightTwips);
        Assert.Equal(3, cards.LastContext.Facts.ColumnTwips.Count);
        Assert.Equal((1, 1), (cards.LastContext.Facts.FirstRow, cards.LastContext.Facts.LastRow));
        Assert.NotNull(((TableRow)TableOf(tab).Rows[0]).Properties.Get(XName.Get("tblHeader", "http://schemas.openxmlformats.org/wordprocessingml/2006/main")));
        Assert.NotNull(((TableRow)TableOf(tab).Rows[1]).Properties.Get(XName.Get("trHeight", "http://schemas.openxmlformats.org/wordprocessingml/2006/main")));

        // A cancelled card changes nothing.
        cards.Properties = null;
        Command(window, WriteCommandIds.TableProperties).Run!();
        await WindowFixture.WaitAsync(() => cards.PropertiesAsked == 2);
        Assert.Equal(steps + 1, session.Editor.History.UndoCount);

        Command(window, WriteCommandIds.TableDistributeColumns).Run!();
        WindowFixture.Pump();
        Assert.Equal(steps + 2, session.Editor.History.UndoCount);
        var grid = TableOf(tab).GridColumns.ToArray();
        Assert.Equal(3, grid.Length);
        Assert.True(grid.Max() - grid.Min() <= 2);
        Assert.Equal(drawn.Sum(), grid.Sum());
    }

    [AvaloniaFact]
    public async Task EveryTableCommandIsDisabledWithItsReasonInAPrefixDocumentAndDuringASave()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        CaretIn(session, tab, 1, 1);
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.TableRowBelow).IsEnabled);

        // During a save every table command is disabled with the reason, and a run does nothing.
        var save = session.BeginSave();
        Settle(window);
        var revision = tab.Docx.Model.Edits.Revision;
        foreach (var id in s_ids)
        {
            Assert.False(Command(window, id).IsEnabled, id);
            Assert.Equal(WriteStrings.ReasonEditSaving, Command(window, id).CurrentDisabledReason);
            Assert.False(window.RunCommand(id), id);
        }

        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        save.Dispose();
        WindowFixture.Pump();
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.TableRowBelow).IsEnabled);

        // A prefix document (the first blocks only): the facts say so without a session, and the view refuses with the same words.
        var thesis = Path.Combine(fixture.Folder, "long.docx");
        File.Copy(StyledDocx.Write(fixture.Folder, "long-source.docx", StyledDocx.Styles, Enumerable.Range(0, 120).Select(i => "<w:r><w:t>paragraph " + i + "</w:t></w:r>").ToArray()), thesis, overwrite: true);
        using var h = new ViewHarness(thesis, 900, 700);
        Assert.True(h.Tab.Docx.IsPrefix);
        foreach (var id in s_ids)
        {
            Assert.Equal(TableBlock.PrefixDocument, new TableFactsSource().Read(h.Tab, id, writingView: false).Block);
        }

        var notes = new List<string>();
        h.View.StructureNote += notes.Add;
        var before = h.Tab.Docx.Model.Edits.Revision;
        Assert.True(h.View.RunTableCommand(WriteCommandIds.TableInsert, new TableSize(2, 2)));
        Assert.Equal([WriteStrings.ReasonTablePrefixDocument], notes);
        Assert.Equal(before, h.Tab.Docx.Model.Edits.Revision);
    }

    [AvaloniaFact]
    public async Task TheGridPickerIsBuiltOnFirstOpenOnly()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        Assert.Equal(0, window.TableGridBuilds);
        var (_, tab, session) = await OpenAsync(fixture);
        window = fixture.Window;
        CaretOutside(session, tab);
        Settle(window);
        window.Ribbon.ActiveTab = window.Ribbon.Definition!.Tabs.Single(t => t.Letter == WriteRibbon.InsertLetter);
        Settle(window);
        Assert.Equal(0, window.TableGridBuilds);
        Assert.Null(window.TableGrid);

        window.ShowTableGrid();
        WindowFixture.Pump();
        Assert.Equal(1, window.TableGridBuilds);
        var grid = Assert.IsType<TableGridPicker>(window.TableGrid);
        Assert.Equal((1, 1), (grid.Columns, grid.Rows));

        // A second open resets and reuses the grid.
        grid.Light(5, 5);
        window.ShowTableGrid();
        WindowFixture.Pump();
        Assert.Equal(1, window.TableGridBuilds);
        Assert.Same(grid, window.TableGrid);
    }

    // The window hands the ribbon's tab row and band to the frame, so the RibbonView itself is never in the visual tree: a grid anchored to it was "open" with no root and no size and never showed (found on a real display).
    [AvaloniaFact]
    public async Task TheGridPickerIsOnScreenUnderTheInsertButton()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        window = fixture.Window;
        CaretOutside(session, tab);
        Settle(window);
        window.Ribbon.ActiveTab = window.Ribbon.Definition!.Tabs.Single(t => t.Letter == WriteRibbon.InsertLetter);
        Settle(window);

        window.ShowTableGrid();
        WindowFixture.Pump();

        var grid = Assert.IsType<TableGridPicker>(window.TableGrid);
        Assert.NotNull(TopLevel.GetTopLevel(grid));
        Assert.True(grid.IsLoaded);
        Assert.True(grid.Bounds.Width > 0 && grid.Bounds.Height > 0);
    }

    // Run from the command search or the menu while another tab is shown (the Insert button is not in the band), the grid opens under the band.
    [AvaloniaFact]
    public async Task TheGridPickerIsOnScreenWhenTheInsertBandIsNotShown()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        window = fixture.Window;
        CaretOutside(session, tab);
        Settle(window);
        Assert.Equal(WriteRibbon.HomeLetter, window.Ribbon.ActiveTab!.Letter);

        window.ShowTableGrid();
        WindowFixture.Pump();

        var grid = Assert.IsType<TableGridPicker>(window.TableGrid);
        Assert.NotNull(TopLevel.GetTopLevel(grid));
        Assert.True(grid.IsLoaded);
        Assert.True(grid.Bounds.Width > 0 && grid.Bounds.Height > 0);
    }

    [AvaloniaFact]
    public async Task StartupCreatesNoTableWork()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        Assert.False(window.TableFactsExist);
        Assert.Equal(0, window.TableGridBuilds);
        var (_, tab, session) = await OpenAsync(fixture);
        window = fixture.Window;
        var revision = tab.Docx.Model.Edits.Revision;
        CaretIn(session, tab, 1, 1);
        Settle(window);
        WindowFixture.Pump();

        // The Home band is the active tab: no fact of a table command is read, no grid is made, nothing changed.
        Assert.False(window.TableFactsExist);
        Assert.Equal(0, window.TableFactReads);
        Assert.Equal(0, window.TableGridBuilds);
        Assert.Equal(0, window.TablePickerSet.ListReads);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.Equal(0, session.Editor.History.UndoCount);

        // The first question makes the source and reads once per command; the same selection and revision read nothing again.
        Assert.True(Command(window, WriteCommandIds.TableRowBelow).IsEnabled);
        Assert.True(window.TableFactsExist);
        var reads = window.TableFactReads;
        Assert.True(reads >= 1);
        _ = Command(window, WriteCommandIds.TableRowBelow).IsEnabled;
        Assert.Equal(reads, window.TableFactReads);
        Assert.Equal(0, window.TablePickerSet.ListReads);
    }

    [AvaloniaFact]
    public async Task ThePickersListTheirItemsWhenTheyOpenAndTheBordersChoiceRunsOrOpensTheCard()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        CaretIn(session, tab, 1, 1);
        Settle(window);
        var borders = Command(window, WriteCommandIds.TableBorders).Picker!;
        var shading = Command(window, WriteCommandIds.TableShading).Picker!;
        Assert.Equal(0, window.TablePickerSet.ListReads);

        var items = borders.Items();
        Assert.Equal([.. TablePayloads.BorderPresets, "more"], items.Select(i => i.Id).ToArray());
        Assert.Equal(1, window.TablePickerSet.ListReads);
        var colours = shading.Items();
        Assert.Equal("none", colours[0].Id);
        Assert.Equal(ColorChoices.TextColors.Count + 1, colours.Count);
        Assert.Equal(2, window.TablePickerSet.ListReads);

        var cards = new FakeCards { Borders = BorderSpec.Sides(BorderSides.Bottom, TableBorderStyle.Dashed, 8, 0xFF00B050) };
        window.TableCards = cards;
        var steps = session.Editor.History.UndoCount;
        borders.Chosen("outside");
        WindowFixture.Pump();
        Assert.Equal(steps + 1, session.Editor.History.UndoCount);
        borders.Chosen("more");
        await WindowFixture.WaitAsync(() => cards.BordersAsked == 1);
        await WindowFixture.WaitAsync(() => session.Editor.History.UndoCount == steps + 2);
        shading.Chosen("00B050");
        WindowFixture.Pump();
        Assert.Equal(steps + 3, session.Editor.History.UndoCount);
    }

    private sealed class FakeCards : ITableCards
    {
        public TableSize? Insert { get; set; }

        public BorderSpec? Borders { get; set; }

        public TableProperties? Properties { get; set; }

        public int InsertAsked { get; private set; }

        public int BordersAsked { get; private set; }

        public int PropertiesAsked { get; private set; }

        public TablePropertiesContext? LastContext { get; private set; }

        public Task<TableSize?> AskInsertAsync()
        {
            InsertAsked++;
            return Task.FromResult(Insert);
        }

        public Task<BorderSpec?> AskBordersAsync()
        {
            BordersAsked++;
            return Task.FromResult(Borders);
        }

        public Task<TableProperties?> AskPropertiesAsync(TablePropertiesContext context)
        {
            PropertiesAsked++;
            LastContext = context;
            return Task.FromResult(Properties);
        }
    }
}
