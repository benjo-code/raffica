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
using Raffica.Write.Viewing;
using Raffica.WriteApp.Tests.Platform;
using Xunit;

namespace Raffica.WriteApp.Tests.Commands;

/// <summary>The bookmark command and the References tab (milestone 4.5b, Task 4): id, icon, wording, the rules with a reason for every disabled state, the window run path through the card and the start cost.</summary>
public sealed class BookmarkCommandTests
{
    private const string Id = WriteCommandIds.BookmarkInsert;

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

    private static void Select(EditSession session, WriteTab tab, int paragraph, int from, int to) =>
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, P(tab, paragraph), from), new TextPosition(StoryRef.Body, P(tab, paragraph), to)));

    private static void Settle(WriteWindow window)
    {
        window.FormatRefresh?.Flush();
        window.ViewStatesRefresh?.Flush();
        WindowFixture.Pump();
        window.Ribbon.RefreshCommandStates();
        WindowFixture.Pump();
    }

    private sealed class FakeCards : IFieldCards
    {
        public BookmarkRequest? Answer { get; set; }

        public BookmarkContext? Seen { get; private set; }

        public int Asked { get; private set; }

        public Action? WhileOpen { get; set; }

        public Task<BookmarkRequest?> AskBookmarkAsync(BookmarkContext context)
        {
            Asked++;
            Seen = context;
            WhileOpen?.Invoke();
            return Task.FromResult(Answer);
        }
    }

    [Fact]
    public void TheIdIsACatalogEntryOfTheReferencesTabWithNoKeys()
    {
        var command = Of(Id);
        Assert.True(WriteCommandIds.IsBookmark(Id));
        Assert.False(WriteCommandIds.IsBookmark("note.back"));
        Assert.False(WriteCommandIds.TakesPayload(Id));
        Assert.Equal(WriteRibbon.ReferencesLetter, command.Tab.Letter);
        Assert.Equal("R", command.Tab.Letter);
        Assert.Equal("GroupLinks", command.Group.LabelKey);
        Assert.False(command.Toggle);
        Assert.Null(command.ShortcutFor(macOs: false));
        Assert.Null(command.ShortcutFor(macOs: true));
        Assert.Equal(["H", "I", "T", "P", "R", "V"], WriteCommandCatalog.Tabs.Select(t => t.Letter).ToArray());

        var modifiers = new[] { KeyModifiers.None, KeyModifiers.Control, KeyModifiers.Control | KeyModifiers.Alt, KeyModifiers.Control | KeyModifiers.Shift, KeyModifiers.Alt, KeyModifiers.Meta };
        foreach (var key in Enum.GetValues<Key>())
        {
            foreach (var modifier in modifiers)
            {
                Assert.NotEqual(Id, WriteCommandCatalog.Find(key, modifier, KeyModifiers.Control, macOs: false)?.Id);
                Assert.NotEqual(Id, WriteCommandCatalog.Find(key, modifier, KeyModifiers.Meta, macOs: true)?.Id);
            }
        }
    }

    [AvaloniaFact]
    public void TheCommandHasItsOwnIconBothLanguagesAndAMacMenuLine()
    {
        foreach (var group in WriteCommandCatalog.Commands.GroupBy(c => c.Icon).Where(g => g.Count() > 1))
        {
            Assert.Fail($"Icon {group.Key} serves {string.Join(", ", group.Select(c => c.Id))}");
        }

        var keys = XDocument.Load(RepoPaths.Combine("src", "Raffica.Shell", "Styles", "Icons.axaml")).Root!.Elements()
            .Select(e => (string?)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")))
            .Where(k => k is not null && k.StartsWith("Raffica.Icon.", StringComparison.Ordinal)).ToList();
        var command = Of(Id);
        Assert.Equal("Bookmark", command.Icon);
        Assert.Contains("Raffica.Icon.Bookmark", keys);
        Assert.True(Application.Current!.TryGetResource("Raffica.Icon.Bookmark", Application.Current!.ActualThemeVariant, out var value));
        Assert.IsAssignableFrom<Geometry>(value);

        foreach (var language in new[] { "en", "de" })
        {
            var culture = CultureInfo.GetCultureInfo(language);
            Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn(command.NameKey, culture)));
            Assert.NotNull(command.DescriptionKey);
            Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn(command.DescriptionKey!, culture)));
            Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn("TabReferences", culture)));
            Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn("MenuReferences", culture)));
            Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn("GroupLinks", culture)));
        }

        var window = new WriteMacMenuTests.FakeWindow { HasTab = true };
        window.Allowed.Add(Id);
        var menu = new WriteMacMenu(() => window, () => Task.CompletedTask, WriteMenuPart.Window);
        menu.Refresh();
        var references = menu.Menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == WriteStrings.MenuReferences).Menu!.Items.OfType<NativeMenuItem>().ToList();
        var item = Assert.Single(references, i => i.Header == command.Name);
        Assert.Null(item.Gesture);
        Assert.True(item.Command!.CanExecute(null));
        item.Command.Execute(null);
        Assert.Equal("run " + Id, window.Calls[^1]);

        window.Allowed.Clear();
        menu.Refresh();
        Assert.False(item.Command.CanExecute(null));
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
            "TabReferences", "MenuReferences", "GroupLinks", Of(Id).NameKey, Of(Id).DescriptionKey!, "BookmarkTitle", "BookmarkNameLabel", "BookmarkListLabel", "BookmarkNone", "BookmarkAdd", "BookmarkGoTo", "BookmarkDelete",
            "BookmarkProblemEmpty", "BookmarkProblemTooLong", "BookmarkProblemBadStart", "BookmarkProblemBadCharacter", "BookmarkProblemDuplicate", "NoteBookmarkMissing",
        ];
        foreach (var key in fresh)
        {
            Assert.True(german.TryGetValue(key, out var text) && english.ContainsKey(key), key);
            var hit = du.Match(text!);
            Assert.False(hit.Success, $"{key}: \"{text}\" is in the du form ({hit.Value})");
            Assert.DoesNotContain("  ", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryDisabledStateHasAReason()
    {
        var count = 0;
        foreach (var blocked in new EditBlockedReason?[] { null, EditBlockedReason.StillOpening, EditBlockedReason.Saving, EditBlockedReason.Faulted })
        {
            foreach (var block in Enum.GetValues<ObjectBlock>())
            {
                var state = new EditCommandState(blocked, true, true, true);
                var rules = new WriteCommandRules(() => true, () => 1.0, () => true, () => state, objectFacts: _ => new ObjectCommandFacts(block));
                count++;
                if (rules.Allows(Id))
                {
                    Assert.True(rules.ReasonFor(Id) is null, $"{blocked}, {block}");
                }
                else
                {
                    Assert.False(string.IsNullOrWhiteSpace(rules.ReasonFor(Id)), $"{blocked}, {block}");
                }
            }
        }

        Assert.True(count >= 20);
        var none = new WriteCommandRules(() => false, () => 1.0, () => true, () => new EditCommandState(null, false, false, false));
        Assert.Null(none.ReasonFor(Id));
    }

    [Fact]
    public void TheRulesWordEachReason()
    {
        static string? Reason(ObjectBlock block, EditBlockedReason? blocked = null)
        {
            var rules = new WriteCommandRules(() => true, () => 1.0, () => true, () => new EditCommandState(blocked, true, true, true), objectFacts: _ => new ObjectCommandFacts(block));
            return rules.ReasonFor(Id);
        }

        Assert.Null(Reason(ObjectBlock.None));
        Assert.Equal(WriteStrings.ReasonSectionPrefix, Reason(ObjectBlock.PrefixDocument));
        Assert.Equal(WriteStrings.ReasonEditOpening, Reason(ObjectBlock.None, EditBlockedReason.StillOpening));
        Assert.Equal(WriteStrings.EditUnavailable, Reason(ObjectBlock.None, EditBlockedReason.Faulted));
        Assert.Equal(WriteStrings.ReasonEditSaving, Reason(ObjectBlock.None, EditBlockedReason.Saving));
    }

    [AvaloniaFact]
    public async Task TheCardAddsGoesToAndDeletesThroughTheWindowAndUndoTakesThemBack()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, "<w:r><w:t>one two three</w:t></w:r>", "<w:r><w:t>second paragraph</w:t></w:r>");
        var cards = new FakeCards();
        window.FieldCards = cards;
        Select(session, tab, 0, 4, 7);
        Settle(window);
        Assert.True(Command(window, Id).IsEnabled);

        cards.Answer = new BookmarkRequest(BookmarkAction.Add, "Middle");
        Command(window, Id).Run!();
        WindowFixture.Pump();

        Assert.Equal(1, cards.Asked);
        Assert.Empty(cards.Seen!.Names);
        var info = Assert.Single(BookmarkOperations.Visible(tab.Docx.Model));
        Assert.Equal("Middle", info.Name);
        Assert.Equal((4, 7), (info.Range.Start.Offset, info.Range.End.Offset));

        // The card lists it next time and Go to puts the selection on its range.
        Select(session, tab, 1, 2, 2);
        cards.Answer = new BookmarkRequest(BookmarkAction.GoTo, "Middle");
        Command(window, Id).Run!();
        WindowFixture.Pump();
        Assert.Equal(["Middle"], cards.Seen!.Names.ToArray());
        Assert.Same(P(tab, 0), session.Selection!.Value.Start.Paragraph);
        Assert.Equal((4, 7), (session.Selection.Value.Start.Offset, session.Selection.Value.End.Offset));

        cards.Answer = new BookmarkRequest(BookmarkAction.Delete, "Middle");
        Command(window, Id).Run!();
        WindowFixture.Pump();
        Assert.Empty(BookmarkOperations.Visible(tab.Docx.Model));

        Assert.NotNull(session.Editor.History.Undo());
        Assert.Single(BookmarkOperations.Visible(tab.Docx.Model));
        Assert.NotNull(session.Editor.History.Undo());
        Assert.Empty(BookmarkOperations.Visible(tab.Docx.Model));
    }

    [AvaloniaFact]
    public async Task ACardClosedOrAnsweredAfterTheDocumentChangedChangesNothing()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, "<w:r><w:t>one two three</w:t></w:r>");
        var cards = new FakeCards();
        window.FieldCards = cards;
        Select(session, tab, 0, 0, 3);
        Settle(window);

        // Closed: nothing is added.
        cards.Answer = null;
        Command(window, Id).Run!();
        WindowFixture.Pump();
        Assert.Empty(BookmarkOperations.Visible(tab.Docx.Model));

        // The document changes while the card is open (typing behind it): the answer is refused with the note, never applied.
        cards.Answer = new BookmarkRequest(BookmarkAction.Add, "Late");
        cards.WhileOpen = () => Assert.NotNull(session.Editor.InsertText(new TextRange(new TextPosition(StoryRef.Body, P(tab, 0), 0), new TextPosition(StoryRef.Body, P(tab, 0), 0)), "x"));
        Command(window, Id).Run!();
        WindowFixture.Pump();

        Assert.Empty(BookmarkOperations.Visible(tab.Docx.Model));
        Assert.Equal(WriteStrings.NoteCardStale, window.Status.Hint);
    }

    [AvaloniaFact]
    public async Task ADuplicateNameAnAbsentBookmarkAndAPrefixDocumentEachSayWhy()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, "<w:r><w:t>one two three</w:t></w:r>");
        Select(session, tab, 0, 0, 3);
        var view = window.ActiveView!;
        var notes = new List<string>();
        view.StructureNote += notes.Add;
        Assert.True(view.RunBookmarkCommand(new BookmarkRequest(BookmarkAction.Add, "Same")));
        var revision = tab.Docx.Model.Edits.Revision;

        // Taken name: the editor refuses and the note says the command could not be applied.
        Assert.True(view.RunBookmarkCommand(new BookmarkRequest(BookmarkAction.Add, "same")));
        Assert.Equal([WriteStrings.NoteStructureFailed], notes);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);

        notes.Clear();
        Assert.True(view.RunBookmarkCommand(new BookmarkRequest(BookmarkAction.Delete, "Gone")));
        Assert.True(view.RunBookmarkCommand(new BookmarkRequest(BookmarkAction.GoTo, "Gone")));
        Assert.Equal([WriteStrings.NoteBookmarkMissing, WriteStrings.NoteBookmarkMissing], notes);

        notes.Clear();
        view.PrefixProbe = () => true;
        Assert.True(view.RunBookmarkCommand(new BookmarkRequest(BookmarkAction.Add, "Other")));
        Assert.True(view.RunBookmarkCommand(new BookmarkRequest(BookmarkAction.Delete, "Same")));
        Assert.Equal([WriteStrings.ReasonSectionPrefix, WriteStrings.ReasonSectionPrefix], notes);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.Single(BookmarkOperations.Visible(tab.Docx.Model));

        // A request that is not well formed is not taken.
        Assert.False(view.RunBookmarkCommand(new BookmarkRequest(BookmarkAction.Add, "1bad")));
    }

    [AvaloniaFact]
    public async Task StartupCreatesNoBookmarkWork()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        Assert.False(window.FieldCardsExist);
        var (_, tab, session) = await OpenAsync(fixture, "<w:r><w:t>one</w:t></w:r>", "<w:r><w:t>two</w:t></w:r>");
        window = fixture.Window;
        var revision = tab.Docx.Model.Edits.Revision;
        Select(session, tab, 0, 0, 0);
        Settle(window);
        WindowFixture.Pump();

        // Opening, a caret and a refresh of the ribbon build no cards object, no bookmark and no step.
        Assert.False(window.FieldCardsExist);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.Empty(BookmarkOperations.AllNames(tab.Docx.Model));
        Assert.False(session.Editor.History.CanUndo);
        Assert.Equal(["H", "I", "T", "P", "R", "V"], window.Ribbon.Definition!.Tabs.Select(t => t.Letter).ToArray());
        Assert.Equal(0, window.Ribbon.Definition.Tabs.Single(t => t.Letter == WriteRibbon.ReferencesLetter).FullWidthAtLeast);
    }
}
