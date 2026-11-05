using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Raffica.Shell.Localization;
using Xunit;

namespace Raffica.Pdf.Tests.Commands;

public sealed class CommandCatalogTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de");

    [Fact]
    public void EveryCommandHasANameInBothLanguages()
    {
        var keys = PdfCommandCatalog.Commands.Select(c => c.NameKey)
            .Concat(PdfCommandCatalog.Tabs.Select(t => t.NameKey))
            .Concat(PdfCommandCatalog.Tabs.SelectMany(t => t.Groups).Select(g => g.LabelKey))
            .ToList();
        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(PdfStrings.GetIn(key, English)), key + " (en)");
            Assert.False(string.IsNullOrWhiteSpace(PdfStrings.GetIn(key, German)), key + " (de)");
        }

        Assert.Equal("Einstellungen", PdfStrings.GetIn("CmdSettings", German));
        Assert.Equal("Tastenkürzel", PdfStrings.GetIn("CmdHelp", German));
    }

    [Fact]
    public void ShortcutsAreUnique()
    {
        var shortcuts = PdfCommandCatalog.Commands.Where(c => c.Shortcut is not null).Select(c => c.Shortcut!).ToList();
        Assert.Equal(shortcuts.Count, shortcuts.Select(s => s.Text(macOs: false)).Distinct().Count());

        // No two commands answer to the same key with the same modifiers, alternates included.
        var seen = new HashSet<(Avalonia.Input.Key, bool, ShortcutBase)>();
        foreach (var shortcut in shortcuts.SelectMany(s => s.Also is { } also ? new[] { s, also } : [s]))
        {
            foreach (var key in shortcut.Keys)
            {
                Assert.True(seen.Add((key, shortcut.Shift, shortcut.Base)), shortcut.Text(false) + " is taken twice");
            }
        }

        Assert.Equal(PdfCommandCatalog.Commands.Count, PdfCommandCatalog.Commands.Select(c => c.Id).Distinct().Count());
        Assert.Equal(PdfCommandCatalog.Commands.Count, PdfCommandCatalog.Commands.Select(c => c.Tab.Letter + c.Group.Digit + c.FieldCode).Distinct().Count());
    }

    [Fact]
    public void EveryViewerActionHasAShortcutOrAFieldCode()
    {
        Assert.All(PdfCommandCatalog.Commands, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.FieldCode), c.Id);
            Assert.False(string.IsNullOrWhiteSpace(c.Icon), c.Id);
        });

        // The ribbon built from the catalog holds every command once, with the shortcut text of the catalog.
        var ribbon = PdfRibbon.Create(new PdfCommands());
        ribbon.Validate();
        var commands = ribbon.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).ToList();
        Assert.Equal(PdfCommandCatalog.Commands.Select(c => c.Id).Order(), commands.Select(c => c.Id).Order());
        foreach (var catalogued in PdfCommandCatalog.Commands)
        {
            var command = commands.Single(c => c.Id == catalogued.Id);
            Assert.Equal(catalogued.FieldCode, command.FieldCode);
            Assert.Equal(catalogued.Shortcut?.Text(OperatingSystem.IsMacOS()), command.Shortcut);
        }
    }

    [Theory]
    [InlineData("file.open", "Ctrl+O")]
    [InlineData("file.close", "Ctrl+W")]
    [InlineData("tabs.next", "Ctrl+Tab")]
    [InlineData("tabs.previous", "Ctrl+Shift+Tab")]
    [InlineData("file.print", "Ctrl+P")]
    [InlineData("find.search", "Ctrl+F")]
    [InlineData("find.next", "F3")]
    [InlineData("find.previous", "Shift+F3")]
    [InlineData("goto.page", "Ctrl+G")]
    [InlineData("zoom.in", "Ctrl++")]
    [InlineData("zoom.out", "Ctrl+-")]
    [InlineData("zoom.actual", "Ctrl+0")]
    [InlineData("zoom.fitwidth", "Ctrl+1")]
    [InlineData("zoom.fitpage", "Ctrl+2")]
    [InlineData("page.dark", "Ctrl+Shift+D")]
    [InlineData("file.settings", "Ctrl+,")]
    [InlineData("panes.pages", "Ctrl+Shift+1")]
    [InlineData("panes.outline", "Ctrl+Shift+2")]
    [InlineData("text.copy", "Ctrl+C")]
    [InlineData("text.selectall", "Ctrl+A")]
    [InlineData("help.shortcuts", "F1")]
    [InlineData("file.save", "Ctrl+S")]
    [InlineData("file.saveAs", "Ctrl+Shift+S")]
    [InlineData("edit.undo", "Ctrl+Z")]
    [InlineData("edit.redo", "Ctrl+Shift+Z")]
    [InlineData("annotate.mode", "Ctrl+Shift+K")]
    [InlineData("annotate.highlight", "Ctrl+Shift+H")]
    [InlineData("annotate.underline", "Ctrl+Shift+U")]
    [InlineData("annotate.strikeOut", "Ctrl+Shift+X")]
    [InlineData("annotate.select", "Ctrl+Shift+M")]
    public void TheShortcutsFollowTheSpecification(string id, string expected)
    {
        var command = PdfCommandCatalog.Commands.Single(c => c.Id == id);
        Assert.Equal(expected, command.Shortcut!.Text(macOs: false));
    }

    [Fact]
    public void OnMacTheCommandKeyReplacesControlExceptForCtrlTab()
    {
        Assert.Equal("Meta+O", PdfCommandCatalog.Commands.Single(c => c.Id == "file.open").Shortcut!.Text(macOs: true));
        Assert.Equal("Meta+Shift+D", PdfCommandCatalog.Commands.Single(c => c.Id == "page.dark").Shortcut!.Text(macOs: true));
        Assert.Equal("Ctrl+Tab", PdfCommandCatalog.Commands.Single(c => c.Id == "tabs.next").Shortcut!.Text(macOs: true));
        Assert.Equal("F3", PdfCommandCatalog.Commands.Single(c => c.Id == "find.next").Shortcut!.Text(macOs: true));
    }

    [Fact]
    public void ACommandWithoutAnActionIsDisabled()
    {
        var ribbon = PdfRibbon.Create(new PdfCommands(Open: () => { }));
        var commands = ribbon.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).ToList();
        Assert.Equal(["file.open"], commands.Where(c => c.IsEnabled).Select(c => c.Id).ToArray());
    }

    [Fact]
    public void NoDocumentDisablesEverythingButOpen()
    {
        var rules = new PdfCommandRules(() => false, () => 0, () => null);
        var allowed = PdfCommandCatalog.Commands.Where(c => rules.Allows(c.Id)).Select(c => c.Id).Order().ToArray();

        // Help and Settings need no document either (the start view has them behind F1 and Ctrl+Comma); nothing else does.
        Assert.Equal(["file.open", "file.settings", "help.shortcuts"], allowed);
        Assert.Equal(51, PdfCommandCatalog.Commands.Count);
    }

    [Fact]
    public void AnOpenDocumentEnablesTheViewerCommands()
    {
        var rules = new PdfCommandRules(() => true, () => 1, () => new PdfPrintInfo(false, true));
        var blocked = PdfCommandCatalog.Commands.Where(c => !rules.Allows(c.Id)).Select(c => c.Id).ToArray();

        // Moving between tabs needs a second tab; Save needs a dirty tab, Undo and Redo a history, Delete a selection, the form commands a form.
        Assert.Equal(["annotate.delete", "edit.redo", "edit.undo", "file.save", "forms.highlight", "forms.next", "forms.nextRequired", "forms.previous", "tabs.next", "tabs.previous"], blocked.Order(StringComparer.Ordinal).ToArray());
        Assert.True(new PdfCommandRules(() => true, () => 2, () => null).Allows("tabs.next"));
    }

    [Fact]
    public void PrintIsDisabledForALockedDocumentWithAReason()
    {
        var locked = new PdfCommandRules(() => true, () => 1, () => new PdfPrintInfo(true, false));
        Assert.False(locked.Allows("file.print"));
        Assert.Equal("This document does not allow printing.", locked.ReasonFor("file.print"));
        Assert.Null(locked.ReasonFor("zoom.in"));

        var open = new PdfCommandRules(() => true, () => 1, () => new PdfPrintInfo(false, true));
        Assert.True(open.Allows("file.print"));
        Assert.Null(open.ReasonFor("file.print"));

        // Not read yet: print is not refused before the document said so.
        Assert.True(new PdfCommandRules(() => true, () => 1, () => null).Allows("file.print"));

        // In the ribbon: the reason is the description of the disabled command, and a wired Print runs only while allowed.
        var ran = 0;
        var ribbon = PdfRibbon.Create(new PdfCommands(Print: () => ran++), locked);
        var print = ribbon.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == "file.print");
        Assert.False(print.IsEnabled);
        Assert.Equal("This document does not allow printing.", print.CurrentDisabledReason);
        Assert.Equal(0, ran);
        Assert.True(PdfRibbon.Create(new PdfCommands(Print: () => ran++), open).Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == "file.print").IsEnabled);
    }

    [Fact]
    public void ThePrintReasonIsGerman()
    {
        Assert.Equal("Dieses Dokument erlaubt kein Drucken.", PdfStrings.GetIn("PrintLockedReason", German));
    }

    [AvaloniaFact]
    public void HelpCardListsEveryShortcutOfTheCatalog()
    {
        var entries = PdfCommandCatalog.HelpEntries();
        var withShortcut = PdfCommandCatalog.Commands.Where(c => c.Shortcut is not null).ToList();
        Assert.Equal(withShortcut.Count, entries.Count);
        foreach (var command in withShortcut)
        {
            var shortcut = KeyNames.Format(command.Shortcut!.Text(OperatingSystem.IsMacOS()), PdfStrings.Culture);
            Assert.Contains(entries, e => e.Name == command.Name && e.Shortcut == shortcut && e.Group == command.Group.Label);
        }

        var host = new DialogHost { Content = new Border() };
        var window = new Window { Width = 800, Height = 700, Content = host };
        window.Show();
        var closed = 0;
        host.Show(HelpCard.Create(entries, () => closed++));
        ViewFixture.Pump();

        var texts = host.ActiveDialog!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        foreach (var entry in entries)
        {
            Assert.Contains(entry.Name, texts);
            Assert.Contains(entry.Shortcut, texts);
        }

        foreach (var group in entries.Select(e => e.Group).Distinct())
        {
            Assert.Contains(group, texts);
        }

        var close = host.ActiveDialog!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Ok");
        close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, closed);
        window.Close();
    }
}
