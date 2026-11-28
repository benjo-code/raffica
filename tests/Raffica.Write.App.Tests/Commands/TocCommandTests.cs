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
using Raffica.WriteApp.Tests.Platform;
using Xunit;

namespace Raffica.WriteApp.Tests.Commands;

/// <summary>The table of contents commands of the References tab (milestone 4.5b, Task 10): ids, icons, wording, the rules with a reason for every disabled state, the run path through the card and Remove, the refusals and the start cost.</summary>
public sealed class TocCommandTests
{
    private const string Insert = WriteCommandIds.TocInsert;
    private const string Remove = WriteCommandIds.TocRemove;

    private static CatalogCommand Of(string id) => WriteCommandCatalog.Commands.Single(c => c.Id == id);

    private static CommandDefinition Command(WriteWindow window, string id) =>
        window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id);

    private const string Heading = "<w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr>";

    private static async Task<(WriteWindow Window, WriteTab Tab, EditSession Session)> OpenAsync(WindowFixture fixture, params string[] paragraphs)
    {
        var path = StyledDocx.Write(fixture.Folder, "doc.docx", StyledDocx.StylesWithHeading2, paragraphs);
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
        public TocRequest? Answer { get; set; }

        public TocContext? Seen { get; private set; }

        public int Asked { get; private set; }

        public Action? WhileOpen { get; set; }

        public Task<BookmarkRequest?> AskBookmarkAsync(BookmarkContext context) => Task.FromResult<BookmarkRequest?>(null);

        public Task<CaptionRequest?> AskCaptionAsync(CaptionContext context) => Task.FromResult<CaptionRequest?>(null);

        public Task<CrossRefRequest?> AskCrossReferenceAsync(CrossRefContext context) => Task.FromResult<CrossRefRequest?>(null);

        public Task<TocRequest?> AskTocAsync(TocContext context)
        {
            Asked++;
            Seen = context;
            WhileOpen?.Invoke();
            return Task.FromResult(Answer);
        }
    }

    [Fact]
    public void TheIdsAreCatalogEntriesOfTheReferencesTabWithNoKeys()
    {
        foreach (var (id, code) in new[] { (Insert, "11"), (Remove, "12") })
        {
            var command = Of(id);
            Assert.True(WriteCommandIds.IsToc(id));
            Assert.False(WriteCommandIds.IsCaption(id));
            Assert.False(WriteCommandIds.TakesPayload(id));
            Assert.Equal(WriteRibbon.ReferencesLetter, command.Tab.Letter);
            Assert.Equal("GroupToc", command.Group.LabelKey);
            Assert.Equal("1", command.Group.Digit);
            Assert.Equal(code, command.FieldCode);
            Assert.False(command.Toggle);
            Assert.Null(command.ShortcutFor(macOs: false));
            Assert.Null(command.ShortcutFor(macOs: true));
        }

        // The group is the first of the tab: Table of contents, Captions, Links.
        Assert.Equal(["GroupToc", "GroupCaptions", "GroupLinks"], Of(Insert).Tab.Groups.Select(g => g.LabelKey).ToArray());

        var modifiers = new[] { KeyModifiers.None, KeyModifiers.Control, KeyModifiers.Control | KeyModifiers.Alt, KeyModifiers.Control | KeyModifiers.Shift, KeyModifiers.Alt, KeyModifiers.Meta };
        foreach (var key in Enum.GetValues<Key>())
        {
            foreach (var modifier in modifiers)
            {
                Assert.NotEqual(Insert, WriteCommandCatalog.Find(key, modifier, KeyModifiers.Control, macOs: false)?.Id);
                Assert.NotEqual(Remove, WriteCommandCatalog.Find(key, modifier, KeyModifiers.Meta, macOs: true)?.Id);
            }
        }
    }

    [AvaloniaFact]
    public void TheCommandsHaveTheirOwnIconsBothLanguagesAndAMacMenuLine()
    {
        foreach (var group in WriteCommandCatalog.Commands.GroupBy(c => c.Icon).Where(g => g.Count() > 1))
        {
            Assert.Fail($"Icon {group.Key} serves {string.Join(", ", group.Select(c => c.Id))}");
        }

        var keys = XDocument.Load(RepoPaths.Combine("src", "Raffica.Shell", "Styles", "Icons.axaml")).Root!.Elements()
            .Select(e => (string?)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml")))
            .Where(k => k is not null && k.StartsWith("Raffica.Icon.", StringComparison.Ordinal)).ToList();
        Assert.Equal("TableOfContents", Of(Insert).Icon);
        Assert.Equal("ListMinus", Of(Remove).Icon);
        foreach (var id in new[] { Insert, Remove })
        {
            var command = Of(id);
            Assert.Contains("Raffica.Icon." + command.Icon, keys);
            Assert.True(Application.Current!.TryGetResource("Raffica.Icon." + command.Icon, Application.Current!.ActualThemeVariant, out var value));
            Assert.IsAssignableFrom<Geometry>(value);
            foreach (var language in new[] { "en", "de" })
            {
                var culture = CultureInfo.GetCultureInfo(language);
                Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn(command.NameKey, culture)));
                Assert.NotNull(command.DescriptionKey);
                Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn(command.DescriptionKey!, culture)));
                Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn("GroupToc", culture)));
            }
        }

        var window = new WriteMacMenuTests.FakeWindow { HasTab = true };
        window.Allowed.Add(Insert);
        window.Allowed.Add(Remove);
        var menu = new WriteMacMenu(() => window, () => Task.CompletedTask, WriteMenuPart.Window);
        menu.Refresh();
        var references = menu.Menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == WriteStrings.MenuReferences).Menu!.Items.OfType<NativeMenuItem>().ToList();
        foreach (var id in new[] { Insert, Remove })
        {
            var item = Assert.Single(references, i => i.Header == Of(id).Name);
            Assert.Null(item.Gesture);
            Assert.True(item.Command!.CanExecute(null));
            item.Command.Execute(null);
            Assert.Equal("run " + id, window.Calls[^1]);
        }

        window.Allowed.Clear();
        menu.Refresh();
        Assert.All(references.Where(i => i.Header == Of(Insert).Name || i.Header == Of(Remove).Name), i => Assert.False(i.Command!.CanExecute(null)));
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
            "GroupToc", Of(Insert).NameKey, Of(Insert).DescriptionKey!, Of(Remove).NameKey, Of(Remove).DescriptionKey!, "TocTitle", "TocKindLabel", "TocKindContents", "TocKindList", "TocCaptionLabel", "TocFirstLevel", "TocLastLevel",
            "TocPages", "TocRight", "TocLeaderLabel", "TocLeaderDots", "TocLeaderHyphens", "TocLeaderLine", "TocLeaderNone", "TocOk", "TocRemoveButton", "TocProblemLevels", "ReasonTocNone", "NoteTocLabel",
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
        foreach (var id in new[] { Insert, Remove })
        {
            foreach (var blocked in new EditBlockedReason?[] { null, EditBlockedReason.StillOpening, EditBlockedReason.Saving, EditBlockedReason.Faulted })
            {
                foreach (var block in Enum.GetValues<ObjectBlock>())
                {
                    var state = new EditCommandState(blocked, true, true, true);
                    var rules = new WriteCommandRules(() => true, () => 1.0, () => true, () => state, objectFacts: _ => new ObjectCommandFacts(block));
                    count++;
                    if (rules.Allows(id))
                    {
                        Assert.True(rules.ReasonFor(id) is null, $"{id}, {blocked}, {block}");
                    }
                    else
                    {
                        Assert.False(string.IsNullOrWhiteSpace(rules.ReasonFor(id)), $"{id}, {blocked}, {block}");
                    }
                }
            }
        }

        Assert.True(count >= 80);
        var none = new WriteCommandRules(() => false, () => 1.0, () => true, () => new EditCommandState(null, false, false, false));
        Assert.Null(none.ReasonFor(Insert));
    }

    [Fact]
    public void TheRulesWordEachReasonAndEveryRefusalOfTheEditorHasABlock()
    {
        static string? Reason(string id, ObjectBlock block)
        {
            var rules = new WriteCommandRules(() => true, () => 1.0, () => true, () => new EditCommandState(null, true, true, true), objectFacts: _ => new ObjectCommandFacts(block));
            return rules.ReasonFor(id);
        }

        Assert.Null(Reason(Insert, ObjectBlock.None));
        Assert.Equal(WriteStrings.ReasonSectionPrefix, Reason(Insert, ObjectBlock.PrefixDocument));
        Assert.Equal(WriteStrings.NoteTocProtected, Reason(Insert, ObjectBlock.InsideToc));
        Assert.Equal(WriteStrings.ReasonTocNone, Reason(Remove, ObjectBlock.NoToc));
        foreach (var refusal in Enum.GetValues<TocRefusal>())
        {
            var block = ObjectWording.BlockOf(refusal);
            if (refusal is TocRefusal.LevelRange or TocRefusal.LabelNotAllowed)
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
    public async Task TheCardInsertsAListThroughTheWindowAndUndoTakesItBack()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, Heading + "<w:r><w:t>Intro</w:t></w:r>", "<w:r><w:t>body</w:t></w:r>", Heading + "<w:r><w:t>Method</w:t></w:r>", "<w:r><w:t>end</w:t></w:r>");
        var cards = new FakeCards();
        window.FieldCards = cards;
        Caret(session, tab, 1, 0);
        Settle(window);
        Assert.True(Command(window, Insert).IsEnabled);
        Assert.False(Command(window, Remove).IsEnabled);

        cards.Answer = new TocRequest(TocAction.Insert, new TocOptions(1, 3, true, true, TocLeader.Dots, null));
        Command(window, Insert).Run!();
        WindowFixture.Pump();

        Assert.Equal(1, cards.Asked);
        Assert.False(cards.Seen!.CanRemove);
        Assert.Equal(["Figure", "Table", "Equation"], cards.Seen.Labels.ToArray());

        // The list stands above the paragraph of the caret (the position is its start) and lists both headings.
        var site = Assert.Single(TocRefresh.Find(tab.Docx.Model.Body.Blocks));
        Assert.Equal(1, site.First);
        Assert.Equal(1, site.Last - site.First);
        Assert.StartsWith("Intro", TextOf(P(tab, 1)), StringComparison.Ordinal);
        Assert.StartsWith("Method", TextOf(P(tab, 2)), StringComparison.Ordinal);
        Assert.NotNull(tab.Docx.Model.Styles!.Find("TOC1"));

        // The caret ends at the end of the list; Remove is then enabled and takes it away again, and the undo brings it back.
        Settle(window);
        Assert.True(Command(window, Remove).IsEnabled);
        Command(window, Remove).Run!();
        WindowFixture.Pump();
        Assert.Empty(TocRefresh.Find(tab.Docx.Model.Body.Blocks));
        Assert.Equal(["Intro", "body", "Method", "end"], tab.Docx.Model.Body.Blocks.OfType<Paragraph>().Select(TextOf));
        Assert.NotNull(session.Editor.History.Undo());
        Assert.Single(TocRefresh.Find(tab.Docx.Model.Body.Blocks));
        Assert.NotNull(session.Editor.History.Undo());
        Assert.Empty(TocRefresh.Find(tab.Docx.Model.Body.Blocks));
        Assert.Equal(4, tab.Docx.Model.Body.Blocks.Count);
    }

    [AvaloniaFact]
    public async Task TheCardOfferedInsideAListRemovesItWithRemove()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, Heading + "<w:r><w:t>Intro</w:t></w:r>", Heading + "<w:r><w:t>Method</w:t></w:r>", "<w:r><w:t>end</w:t></w:r>");
        var cards = new FakeCards { Answer = new TocRequest(TocAction.Insert, new TocOptions(1, 3, false, true, TocLeader.None, null)) };
        window.FieldCards = cards;
        Caret(session, tab, 2, 0);
        Settle(window);
        Command(window, Insert).Run!();
        WindowFixture.Pump();
        Assert.Single(TocRefresh.Find(tab.Docx.Model.Body.Blocks));
        Assert.Contains("\\n", TocRefresh.Find(tab.Docx.Model.Body.Blocks)[0].Instruction, StringComparison.Ordinal);

        var site = TocRefresh.Find(tab.Docx.Model.Body.Blocks)[0];
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, (Paragraph)site.Container[site.First], 0), new TextPosition(StoryRef.Body, (Paragraph)site.Container[site.First], 0)));
        Settle(window);
        cards.Answer = new TocRequest(TocAction.Remove, null);
        Command(window, Insert).Run!();
        WindowFixture.Pump();

        Assert.True(cards.Seen!.CanRemove);
        Assert.Empty(TocRefresh.Find(tab.Docx.Model.Body.Blocks));
    }

    [AvaloniaFact]
    public async Task ARefusalSaysWhyAndChangesNothing()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, Heading + "<w:r><w:t>Intro</w:t></w:r>", "<w:r><w:t>one two three</w:t></w:r>");
        Caret(session, tab, 1, 3);
        var view = window.ActiveView!;
        var notes = new List<string>();
        view.StructureNote += notes.Add;
        var revision = tab.Docx.Model.Edits.Revision;

        // The first level below the last: the editor refuses, the note names the rule.
        Assert.True(view.RunTocCommand(new TocRequest(TocAction.Insert, new TocOptions(5, 2, true, true, TocLeader.Dots, null))));
        Assert.Equal([WriteStrings.TocProblemLevels], notes);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);

        // Remove where there is no list: the note says so.
        notes.Clear();
        Assert.True(view.RunTocCommand(new TocRequest(TocAction.Remove, null)));
        Assert.Equal([WriteStrings.ReasonTocNone], notes);

        // A prefix document is refused with its reason, and a request that is not well formed is not taken.
        notes.Clear();
        view.PrefixProbe = () => true;
        Assert.True(view.RunTocCommand(new TocRequest(TocAction.Insert, new TocOptions(1, 3, true, true, TocLeader.Dots, null))));
        Assert.Equal([WriteStrings.ReasonSectionPrefix], notes);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.False(view.RunTocCommand(new TocRequest(TocAction.Insert, null)));
    }

    [AvaloniaFact]
    public async Task ACardClosedOrAnsweredAfterTheDocumentChangedChangesNothing()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, Heading + "<w:r><w:t>Intro</w:t></w:r>", "<w:r><w:t>one two three</w:t></w:r>");
        var cards = new FakeCards();
        window.FieldCards = cards;
        Caret(session, tab, 1, 3);
        Settle(window);

        cards.Answer = null;
        Command(window, Insert).Run!();
        WindowFixture.Pump();
        Assert.Equal(2, tab.Docx.Model.Body.Blocks.Count);

        // The document changes while the card is open (typing behind it): the answer is refused with the note, never applied.
        cards.Answer = new TocRequest(TocAction.Insert, new TocOptions(1, 3, true, true, TocLeader.Dots, null));
        cards.WhileOpen = () => Assert.NotNull(session.Editor.InsertText(new TextRange(new TextPosition(StoryRef.Body, P(tab, 1), 0), new TextPosition(StoryRef.Body, P(tab, 1), 0)), "x"));
        Command(window, Insert).Run!();
        WindowFixture.Pump();

        Assert.Equal(2, tab.Docx.Model.Body.Blocks.Count);
        Assert.Equal(WriteStrings.NoteCardStale, window.Status.Hint);
    }

    [AvaloniaFact]
    public async Task StartupCreatesNoTableOfContentsWork()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        Assert.False(window.FieldCardsExist);
        Assert.False(window.FieldFactsExist);
        var (_, tab, session) = await OpenAsync(fixture, Heading + "<w:r><w:t>Intro</w:t></w:r>", "<w:r><w:t>two</w:t></w:r>");
        window = fixture.Window;
        var revision = tab.Docx.Model.Edits.Revision;
        Caret(session, tab, 1, 0);
        Settle(window);
        WindowFixture.Pump();

        // Opening and a caret build no cards object, make no step, no style and no bookmark; the facts are read when the command is asked, once per place.
        Assert.False(window.FieldCardsExist);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.False(session.Editor.History.CanUndo);
        Assert.Null(tab.Docx.Model.Styles!.Find("TOC1"));
        Assert.Empty(BookmarkOperations.AllNames(tab.Docx.Model));
        Assert.True(Command(window, Insert).IsEnabled);
        var reads = window.FieldFactReads;
        Settle(window);
        Assert.Equal(reads, window.FieldFactReads);
        Assert.Equal(0, window.Ribbon.Definition!.Tabs.Single(t => t.Letter == WriteRibbon.ReferencesLetter).FullWidthAtLeast);
    }
}
