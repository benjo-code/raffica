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

/// <summary>The caption command of the References tab (milestone 4.5b, Task 8): id, icon, wording, the rules with a reason for every disabled state, the run path through the card, the place beside a table and the start cost.</summary>
public sealed class CaptionCommandTests
{
    private const string Id = WriteCommandIds.CaptionInsert;

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

    private static string TextOf(Paragraph paragraph) => ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text;

    private static void Caret(EditSession session, WriteTab tab, int paragraph, int offset) =>
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, P(tab, paragraph), offset), new TextPosition(StoryRef.Body, P(tab, paragraph), offset)));

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
        public CaptionRequest? Answer { get; set; }

        public CaptionContext? Seen { get; private set; }

        public int Asked { get; private set; }

        public Action? WhileOpen { get; set; }

        public Task<BookmarkRequest?> AskBookmarkAsync(BookmarkContext context) => Task.FromResult<BookmarkRequest?>(null);

        public Task<CaptionRequest?> AskCaptionAsync(CaptionContext context)
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
        Assert.True(WriteCommandIds.IsCaption(Id));
        Assert.False(WriteCommandIds.IsCaption(WriteCommandIds.BookmarkInsert));
        Assert.False(WriteCommandIds.TakesPayload(Id));
        Assert.Equal(WriteRibbon.ReferencesLetter, command.Tab.Letter);
        Assert.Equal("GroupCaptions", command.Group.LabelKey);
        Assert.Equal("2", command.Group.Digit);
        Assert.Equal("21", command.FieldCode);
        Assert.False(command.Toggle);
        Assert.Null(command.ShortcutFor(macOs: false));
        Assert.Null(command.ShortcutFor(macOs: true));

        // The group comes before Links on the tab (the order of the plan: Table of contents, Captions, Links).
        var groups = command.Tab.Groups.Select(g => g.LabelKey).ToList();
        Assert.True(groups.IndexOf("GroupCaptions") < groups.IndexOf("GroupLinks"));

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
        Assert.Equal("Captions", command.Icon);
        Assert.Contains("Raffica.Icon.Captions", keys);
        Assert.True(Application.Current!.TryGetResource("Raffica.Icon.Captions", Application.Current!.ActualThemeVariant, out var value));
        Assert.IsAssignableFrom<Geometry>(value);

        foreach (var language in new[] { "en", "de" })
        {
            var culture = CultureInfo.GetCultureInfo(language);
            Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn(command.NameKey, culture)));
            Assert.NotNull(command.DescriptionKey);
            Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn(command.DescriptionKey!, culture)));
            Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn("GroupCaptions", culture)));
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
            "GroupCaptions", Of(Id).NameKey, Of(Id).DescriptionKey!, "CaptionTitle", "CaptionLabelLabel", "CaptionNewLabelLabel", "CaptionTextLabel", "CaptionPlaceLabel", "CaptionPlaceAbove", "CaptionPlaceBelow", "CaptionChapter",
            "CaptionChapterUnavailable", "CaptionPreviewLabel", "CaptionOk", "CaptionProblemLabel", "CaptionProblemText", "ReasonCaptionRestricted", "NoteCaptionSectionEnd", "NoteCaptionChapter",
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

        Assert.True(count >= 40);
        var none = new WriteCommandRules(() => false, () => 1.0, () => true, () => new EditCommandState(null, false, false, false));
        Assert.Null(none.ReasonFor(Id));
    }

    [Fact]
    public void TheRulesWordEachReasonAndEveryRefusalOfTheEditorHasABlock()
    {
        static string? Reason(ObjectBlock block, EditBlockedReason? blocked = null)
        {
            var rules = new WriteCommandRules(() => true, () => 1.0, () => true, () => new EditCommandState(blocked, true, true, true), objectFacts: _ => new ObjectCommandFacts(block));
            return rules.ReasonFor(Id);
        }

        Assert.Null(Reason(ObjectBlock.None));
        Assert.Equal(WriteStrings.ReasonSectionPrefix, Reason(ObjectBlock.PrefixDocument));
        Assert.Equal(WriteStrings.NoteTocProtected, Reason(ObjectBlock.InsideToc));
        Assert.Equal(WriteStrings.ReasonCaptionRestricted, Reason(ObjectBlock.CaptionRestricted));
        Assert.Equal(WriteStrings.ReasonSectionNotInBody, Reason(ObjectBlock.NoPosition));
        Assert.Equal(WriteStrings.ReasonEditOpening, Reason(ObjectBlock.None, EditBlockedReason.StillOpening));
        Assert.Equal(WriteStrings.EditUnavailable, Reason(ObjectBlock.None, EditBlockedReason.Faulted));
        Assert.Equal(WriteStrings.ReasonEditSaving, Reason(ObjectBlock.None, EditBlockedReason.Saving));

        // Each refusal that depends on the place alone maps to a block with words; the two that depend on the card's choices are notes.
        foreach (var refusal in Enum.GetValues<CaptionRefusal>())
        {
            var block = ObjectWording.BlockOf(refusal);
            if (refusal is CaptionRefusal.SectionEnd or CaptionRefusal.ChapterNeedsNumberedHeadings)
            {
                Assert.Equal(ObjectBlock.None, block);
            }
            else
            {
                Assert.False(string.IsNullOrWhiteSpace(ObjectWording.ReasonText(block)), refusal.ToString());
            }
        }
    }

    [AvaloniaFact]
    public async Task TheCardInsertsACaptionThroughTheWindowAndUndoTakesItBack()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, "<w:r><w:t>the picture</w:t></w:r>", "<w:r><w:t>second paragraph</w:t></w:r>");
        var cards = new FakeCards();
        window.FieldCards = cards;
        Caret(session, tab, 0, 3);
        Settle(window);
        Assert.True(Command(window, Id).IsEnabled);

        cards.Answer = new CaptionRequest("Figure", ": Results", CaptionPlace.Below, ChapterNumber: false);
        Command(window, Id).Run!();
        WindowFixture.Pump();

        Assert.Equal(1, cards.Asked);
        Assert.Equal("Figure", cards.Seen!.DefaultLabel);
        Assert.Equal(["Figure", "Table", "Equation"], cards.Seen.Labels.ToArray());
        Assert.False(cards.Seen.CanChapterNumber);
        Assert.Equal(3, tab.Docx.Model.Body.Blocks.Count);
        var caption = P(tab, 1);
        Assert.Equal("Figure 1: Results", TextOf(caption));
        Assert.Equal("Caption", caption.Properties.StyleId);

        // The caret stands at the end of the caption, so typing goes on there; a second caption in the document counts on.
        Assert.Same(caption, session.Selection!.Value.End.Paragraph);
        Assert.Equal(TextOf(caption).Length, session.Selection.Value.End.Offset);
        Caret(session, tab, 2, 0);
        cards.Answer = new CaptionRequest("Figure", string.Empty, CaptionPlace.Above, ChapterNumber: false);
        Command(window, Id).Run!();
        WindowFixture.Pump();
        Assert.Equal("Figure 2", TextOf(P(tab, 2)));

        Assert.NotNull(session.Editor.History.Undo());
        Assert.Equal(3, tab.Docx.Model.Body.Blocks.Count);
        Assert.NotNull(session.Editor.History.Undo());
        Assert.Equal(2, tab.Docx.Model.Body.Blocks.Count);
    }

    [AvaloniaFact]
    public async Task ACaptionOnSelectedCellsStandsAboveOrBelowTheTable()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, "<w:r><w:t>before</w:t></w:r>");
        Caret(session, tab, 0, 6);
        Assert.True(window.ActiveView!.RunTableCommand(WriteCommandIds.TableInsert, new TableSize(2, 2)));
        WindowFixture.Pump();
        var tableIndex = tab.Docx.Model.Body.Blocks.ToList().FindIndex(b => b is Table);
        Assert.True(tableIndex >= 0);
        Assert.True(session.SelectTable(new TableRef(StoryRef.Body, tableIndex)));
        var cards = new FakeCards { Answer = new CaptionRequest("Table", ": Totals", CaptionPlace.Above, ChapterNumber: false) };
        window.FieldCards = cards;
        Settle(window);

        Command(window, Id).Run!();
        WindowFixture.Pump();

        // A selected table starts the card on the label Table, and the caption stands above it in the body.
        Assert.Equal("Table", cards.Seen!.DefaultLabel);
        Assert.Equal("Table 1: Totals", TextOf(P(tab, tableIndex)));
        Assert.IsType<Table>(tab.Docx.Model.Body.Blocks[tableIndex + 1]);
    }

    [AvaloniaFact]
    public async Task ARefusalSaysWhyAndChangesNothing()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, "<w:r><w:t>one two three</w:t></w:r>");
        Caret(session, tab, 0, 3);
        var view = window.ActiveView!;
        var notes = new List<string>();
        view.StructureNote += notes.Add;
        var revision = tab.Docx.Model.Edits.Revision;

        // A chapter number in a document with no numbered headings: the editor refuses, the note names what it needs.
        Assert.True(view.RunCaptionCommand(new CaptionRequest("Figure", string.Empty, CaptionPlace.Below, ChapterNumber: true)));
        Assert.Equal([WriteStrings.NoteCaptionChapter], notes);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);

        // A prefix document is refused with its reason.
        notes.Clear();
        view.PrefixProbe = () => true;
        Assert.True(view.RunCaptionCommand(new CaptionRequest("Figure", string.Empty, CaptionPlace.Below, ChapterNumber: false)));
        Assert.Equal([WriteStrings.ReasonSectionPrefix], notes);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);

        // A request that is not well formed is not taken.
        Assert.False(view.RunCaptionCommand(new CaptionRequest("1bad", string.Empty, CaptionPlace.Below, ChapterNumber: false)));
        Assert.False(view.RunCaptionCommand(new CaptionRequest("Figure", "two\nlines", CaptionPlace.Below, ChapterNumber: false)));
    }

    [AvaloniaFact]
    public async Task ACardClosedOrAnsweredAfterTheDocumentChangedChangesNothing()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, "<w:r><w:t>one two three</w:t></w:r>");
        var cards = new FakeCards();
        window.FieldCards = cards;
        Caret(session, tab, 0, 3);
        Settle(window);

        cards.Answer = null;
        Command(window, Id).Run!();
        WindowFixture.Pump();
        Assert.Single(tab.Docx.Model.Body.Blocks);

        // The document changes while the card is open (typing behind it): the answer is refused with the note, never applied.
        cards.Answer = new CaptionRequest("Figure", string.Empty, CaptionPlace.Below, ChapterNumber: false);
        cards.WhileOpen = () => Assert.NotNull(session.Editor.InsertText(new TextRange(new TextPosition(StoryRef.Body, P(tab, 0), 0), new TextPosition(StoryRef.Body, P(tab, 0), 0)), "x"));
        Command(window, Id).Run!();
        WindowFixture.Pump();

        Assert.Single(tab.Docx.Model.Body.Blocks);
        Assert.Equal(WriteStrings.NoteCardStale, window.Status.Hint);
    }

    [AvaloniaFact]
    public async Task StartupCreatesNoCaptionWork()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        Assert.False(window.FieldCardsExist);
        Assert.False(window.FieldFactsExist);
        var (_, tab, session) = await OpenAsync(fixture, "<w:r><w:t>one</w:t></w:r>", "<w:r><w:t>two</w:t></w:r>");
        window = fixture.Window;
        var revision = tab.Docx.Model.Edits.Revision;
        Caret(session, tab, 0, 0);
        Settle(window);
        WindowFixture.Pump();

        // Opening and a caret build no cards object and make no step; the facts are read only when the command is asked (the References tab or the command search), and then once per place.
        Assert.False(window.FieldCardsExist);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.False(session.Editor.History.CanUndo);
        Assert.Null(tab.Docx.Model.Styles!.Find("Caption"));
        Assert.True(Command(window, Id).IsEnabled);
        var reads = window.FieldFactReads;
        Settle(window);
        Assert.Equal(reads, window.FieldFactReads);
        Assert.Equal(0, window.Ribbon.Definition!.Tabs.Single(t => t.Letter == WriteRibbon.ReferencesLetter).FullWidthAtLeast);
    }

    [AvaloniaFact]
    public async Task ALabelOfTheFileIsTakenAndAForeignOneIsNot()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(
            fixture,
            "<w:r><w:t>Équation </w:t></w:r><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> SEQ Équation \\* ARABIC</w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>1</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>");
        var view = window.ActiveView!;
        Caret(session, tab, 0, 0);
        var revision = tab.Docx.Model.Edits.Revision;

        // A label no sequence of the file uses and that is not an ASCII identifier is not taken; the file's own one is.
        Assert.False(view.RunCaptionCommand(new CaptionRequest("Ecuación", string.Empty, CaptionPlace.Below, ChapterNumber: false)));
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.True(view.RunCaptionCommand(new CaptionRequest("Équation", string.Empty, CaptionPlace.Below, ChapterNumber: false)));
        Assert.Equal("Équation 2", TextOf(P(tab, 1)));
    }

    [AvaloniaFact]
    public async Task AStaleRefusalIsNeverWordedWhenTheEditorWasNotAsked()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, "<w:r><w:t>one two three</w:t></w:r>");
        Caret(session, tab, 0, 3);
        var view = window.ActiveView!;
        var notes = new List<string>();
        view.StructureNote += notes.Add;

        // A refusal that leaves its reason in the editor ...
        Assert.True(view.RunCaptionCommand(new CaptionRequest("Figure", string.Empty, CaptionPlace.Below, ChapterNumber: true)));
        Assert.Equal([WriteStrings.NoteCaptionChapter], notes);

        // ... is not repeated for a run that never reached the editor (no selection): the generic note, not the old reason.
        notes.Clear();
        session.SetSelection(null);
        Assert.True(view.RunCaptionCommand(new CaptionRequest("Figure", string.Empty, CaptionPlace.Below, ChapterNumber: false)));
        Assert.DoesNotContain(WriteStrings.NoteCaptionChapter, notes);
    }
}
