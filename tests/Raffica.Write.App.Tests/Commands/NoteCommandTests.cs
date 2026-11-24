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
using Xunit;

namespace Raffica.WriteApp.Tests.Commands;

/// <summary>The footnote, endnote and back commands (milestone 4.5a, Task 3): ids, icons, wording, the rules with a reason for every disabled state, the window run path, the writing view and the start cost.</summary>
public sealed class NoteCommandTests
{
    private static readonly string[] s_ids = ["note.footnote", "note.endnote", "note.back"];

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
        await WindowFixture.WaitAsync(() => window.ActiveView is { IsLayoutComplete: true });
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
    public void TheIdsAreCatalogEntriesOfTheInsertTabWithNoKeys()
    {
        Assert.All(s_ids, id => Assert.Single(WriteCommandCatalog.Commands, c => c.Id == id));
        Assert.All(s_ids, id => Assert.True(WriteCommandIds.IsNote(id), id));
        Assert.All(s_ids, id => Assert.False(WriteCommandIds.TakesPayload(id), id));
        Assert.All(s_ids, id => Assert.Equal(WriteRibbon.InsertLetter, Of(id).Tab.Letter));
        Assert.All(s_ids, id => Assert.False(Of(id).Toggle, id));
        Assert.All(s_ids, id => Assert.Null(Of(id).ShortcutFor(macOs: false)));
        Assert.All(s_ids, id => Assert.Null(Of(id).ShortcutFor(macOs: true)));
        Assert.False(WriteCommandIds.IsNote("format.bold"));
        Assert.False(WriteCommandIds.IsSection("note.back"));

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
    public void EveryNoteCommandHasItsOwnIconBothLanguagesAndAMacMenuLine()
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
            }

            Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn("TabInsert", culture)));
            Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn("MenuInsert", culture)));
            Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn("GroupNotes", culture)));
        }

        var window = new WriteMacMenuTests.FakeWindow { HasTab = true };
        window.Allowed.UnionWith(s_ids);
        var menu = new WriteMacMenu(() => window, () => Task.CompletedTask, WriteMenuPart.Window);
        menu.Refresh();
        var insert = menu.Menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == WriteStrings.MenuInsert).Menu!.Items.OfType<NativeMenuItem>().ToList();
        foreach (var id in s_ids)
        {
            var item = Assert.Single(insert, i => i.Header == Of(id).Name);
            Assert.Null(item.Gesture);
            Assert.True(item.Command!.CanExecute(null), id);
            item.Command.Execute(null);
            Assert.Equal("run " + id, window.Calls[^1]);
        }

        window.Allowed.Clear();
        menu.Refresh();
        Assert.All(insert.Where(item => item.Command is not null), item => Assert.False(item.Command!.CanExecute(null)));
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
            "TabInsert", "MenuInsert", "GroupNotes", .. s_ids.Select(id => Of(id).NameKey), .. s_ids.Select(id => Of(id).DescriptionKey!),
            "ReasonNoteWritingView", "ReasonNoteNotInBody", "ReasonNoteInNote", "ReasonNoteRestricted", "ReasonNoteNotInNote", "NoteNoteNotFound",
        ];
        foreach (var key in fresh)
        {
            Assert.True(german.TryGetValue(key, out var text) && english.ContainsKey(key), key);
            var hit = du.Match(text!);
            Assert.False(hit.Success, $"{key}: \"{text}\" is in the du form ({hit.Value})");
            Assert.DoesNotContain("  ", text, StringComparison.Ordinal);
        }
    }

    private static IEnumerable<ObjectCommandFacts> Facts() => Enum.GetValues<ObjectBlock>().Select(block => new ObjectCommandFacts(block));

    [Fact]
    public void EveryDisabledStateHasAReason()
    {
        var count = 0;
        foreach (var blocked in new EditBlockedReason?[] { null, EditBlockedReason.StillOpening, EditBlockedReason.Saving, EditBlockedReason.Faulted })
        {
            foreach (var facts in Facts())
            {
                var state = new EditCommandState(blocked, true, true, true);
                var rules = new WriteCommandRules(() => true, () => 1.0, () => true, () => state, objectFacts: _ => facts);
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

        Assert.True(count >= 80);

        var none = new WriteCommandRules(() => false, () => 1.0, () => true, () => new EditCommandState(null, false, false, false));
        Assert.All(s_ids, id => Assert.Null(none.ReasonFor(id)));
    }

    [Fact]
    public void TheRulesWordEachReason()
    {
        static string? Reason(string id, ObjectBlock block, EditBlockedReason? blocked = null)
        {
            var rules = new WriteCommandRules(() => true, () => 1.0, () => true, () => new EditCommandState(blocked, true, true, true), objectFacts: _ => new ObjectCommandFacts(block));
            return rules.ReasonFor(id);
        }

        Assert.All(s_ids, id => Assert.Null(Reason(id, ObjectBlock.None)));
        Assert.Equal(WriteStrings.ReasonNoteWritingView, Reason(WriteCommandIds.NoteFootnote, ObjectBlock.WritingView));
        Assert.Equal(WriteStrings.ReasonSectionPrefix, Reason(WriteCommandIds.NoteEndnote, ObjectBlock.PrefixDocument));
        Assert.Equal(WriteStrings.ReasonNoteNotInBody, Reason(WriteCommandIds.NoteFootnote, ObjectBlock.HeaderFooterOrComment));
        Assert.Equal(WriteStrings.ReasonNoteInNote, Reason(WriteCommandIds.NoteEndnote, ObjectBlock.InNote));
        Assert.Equal(WriteStrings.ReasonNoteRestricted, Reason(WriteCommandIds.NoteFootnote, ObjectBlock.RestrictedControl));
        Assert.Equal(WriteStrings.ReasonSectionNotInBody, Reason(WriteCommandIds.NoteFootnote, ObjectBlock.NoPosition));
        Assert.Equal(WriteStrings.ReasonNoteNotInNote, Reason(WriteCommandIds.NoteBack, ObjectBlock.NotInNote));
        Assert.Equal(WriteStrings.ReasonEditOpening, Reason(WriteCommandIds.NoteFootnote, ObjectBlock.None, EditBlockedReason.StillOpening));
        Assert.Equal(WriteStrings.EditUnavailable, Reason(WriteCommandIds.NoteBack, ObjectBlock.None, EditBlockedReason.Faulted));
        Assert.Equal(WriteStrings.ReasonEditSaving, Reason(WriteCommandIds.NoteEndnote, ObjectBlock.None, EditBlockedReason.Saving));
    }

    [AvaloniaFact]
    public async Task TheWritingViewRefusesNotesWithItsReason()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, "<w:r><w:t>one</w:t></w:r>", "<w:r><w:t>two</w:t></w:r>");
        Caret(session, tab, 0, 3);
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.NoteFootnote).IsEnabled);

        window.SetView(WriteView.Writing);
        Settle(window);
        var revision = tab.Docx.Model.Edits.Revision;
        foreach (var id in new[] { WriteCommandIds.NoteFootnote, WriteCommandIds.NoteEndnote })
        {
            Assert.False(Command(window, id).IsEnabled, id);
            Assert.Equal(WriteStrings.ReasonNoteWritingView, Command(window, id).CurrentDisabledReason);
            Assert.False(window.RunCommand(id));
        }

        // The view itself refuses too (the macOS menu and a stale click reach it), with the same words and no change.
        var notes = new List<string>();
        window.ActiveView!.StructureNote += notes.Add;
        Assert.True(window.ActiveView.RunNoteCommand(WriteCommandIds.NoteFootnote));
        Assert.Equal([WriteStrings.ReasonNoteWritingView], notes);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.Null(tab.Docx.Model.Footnotes);

        window.SetView(WriteView.Page);
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.NoteFootnote).IsEnabled);
    }

    [AvaloniaFact]
    public async Task TheCommandRunsFromTheRibbonAndUndoTakesBothTheReferenceAndTheNoteAway()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, "<w:r><w:t>one two</w:t></w:r>");
        Caret(session, tab, 0, 3);
        Settle(window);
        Assert.False(Command(window, WriteCommandIds.NoteBack).IsEnabled);
        Assert.Equal(WriteStrings.ReasonNoteNotInNote, Command(window, WriteCommandIds.NoteBack).CurrentDisabledReason);

        Command(window, WriteCommandIds.NoteFootnote).Run!();
        WindowFixture.Pump();

        var note = Assert.Single(tab.Docx.Model.Footnotes!.Notes, n => n.Kind == NoteKind.Normal);
        Assert.Equal(StoryKind.Note, session.Caret!.Value.Story.Kind);
        Assert.Same(note.Content.Blocks[0], session.Caret.Value.Paragraph);
        Settle(window);

        // In the note the commands for new notes are refused with their reason and Back is offered.
        Assert.False(Command(window, WriteCommandIds.NoteFootnote).IsEnabled);
        Assert.Equal(WriteStrings.ReasonNoteInNote, Command(window, WriteCommandIds.NoteFootnote).CurrentDisabledReason);
        Assert.True(Command(window, WriteCommandIds.NoteBack).IsEnabled);

        Command(window, WriteCommandIds.NoteBack).Run!();
        WindowFixture.Pump();
        Assert.Equal(StoryKind.Body, session.Caret!.Value.Story.Kind);
        Settle(window);
        Assert.False(Command(window, WriteCommandIds.NoteBack).IsEnabled);

        Assert.NotNull(session.Editor.History.Undo());
        Assert.DoesNotContain(tab.Docx.Model.Footnotes!.Notes, n => n.Kind == NoteKind.Normal);
    }

    [AvaloniaFact]
    public async Task StartupCreatesNoObjectWork()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        Assert.False(window.ObjectFactsExist);
        var (_, tab, session) = await OpenAsync(fixture, "<w:r><w:t>one</w:t></w:r>", "<w:r><w:t>two</w:t></w:r>");
        window = fixture.Window;
        var revision = tab.Docx.Model.Edits.Revision;
        Caret(session, tab, 0);
        Settle(window);
        WindowFixture.Pump();

        // The Home band is the active tab: no fact of an object command is read and nothing of a notes part, a style or a step was made.
        Assert.False(window.ObjectFactsExist);
        Assert.Equal(0, window.ObjectFactReads);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.Null(tab.Docx.Model.Footnotes);
        Assert.Null(tab.Docx.Model.Endnotes);
        Assert.Equal(0, session.Editor.History.UndoCount);

        // The first question makes the source and reads once; asking again for the same selection and revision reads nothing.
        Assert.True(Command(window, WriteCommandIds.NoteFootnote).IsEnabled);
        Assert.True(window.ObjectFactsExist);
        var reads = window.ObjectFactReads;
        Assert.True(reads >= 1);
        _ = Command(window, WriteCommandIds.NoteEndnote).IsEnabled;
        Assert.Equal(reads, window.ObjectFactReads);
    }
}
