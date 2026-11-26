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

/// <summary>The cross-reference command of the References tab (milestone 4.5b, Task 9): id, icon, wording, the rules with a reason for every disabled state, the run path through the card, the refusals and the start cost.</summary>
public sealed class CrossReferenceCommandTests
{
    private const string Id = WriteCommandIds.CrossReferenceInsert;

    private const string HeadingStyle = "<w:pPr><w:pStyle w:val=\"Heading1\"/></w:pPr>";

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
        public Func<CrossRefContext, CrossRefRequest?>? Answer { get; set; }

        public CrossRefContext? Seen { get; private set; }

        public int Asked { get; private set; }

        public Action? WhileOpen { get; set; }

        public Task<BookmarkRequest?> AskBookmarkAsync(BookmarkContext context) => Task.FromResult<BookmarkRequest?>(null);

        public Task<CaptionRequest?> AskCaptionAsync(CaptionContext context) => Task.FromResult<CaptionRequest?>(null);

        public Task<CrossRefRequest?> AskCrossReferenceAsync(CrossRefContext context)
        {
            Asked++;
            Seen = context;
            var answer = Answer?.Invoke(context);
            WhileOpen?.Invoke();
            return Task.FromResult(answer);
        }
    }

    [Fact]
    public void TheIdIsACatalogEntryOfTheReferencesTabWithNoKeys()
    {
        var command = Of(Id);
        Assert.True(WriteCommandIds.IsCrossReference(Id));
        Assert.False(WriteCommandIds.IsCrossReference(WriteCommandIds.CaptionInsert));
        Assert.False(WriteCommandIds.IsCaption(Id));
        Assert.False(WriteCommandIds.IsBookmark(Id));
        Assert.False(WriteCommandIds.TakesPayload(Id));
        Assert.Equal(WriteRibbon.ReferencesLetter, command.Tab.Letter);
        Assert.Equal("GroupLinks", command.Group.LabelKey);
        Assert.Equal("3", command.Group.Digit);
        Assert.Equal("32", command.FieldCode);
        Assert.False(command.Toggle);
        Assert.Null(command.ShortcutFor(macOs: false));
        Assert.Null(command.ShortcutFor(macOs: true));

        // It stands beside the bookmark command in the group Links.
        Assert.Equal([WriteCommandIds.BookmarkInsert, Id], command.Group.Commands.Select(c => c.Id).ToArray());

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
        Assert.Equal("ExternalLink", command.Icon);
        Assert.Contains("Raffica.Icon.ExternalLink", keys);
        Assert.True(Application.Current!.TryGetResource("Raffica.Icon.ExternalLink", Application.Current!.ActualThemeVariant, out var value));
        Assert.IsAssignableFrom<Geometry>(value);

        foreach (var language in new[] { "en", "de" })
        {
            var culture = CultureInfo.GetCultureInfo(language);
            Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn(command.NameKey, culture)));
            Assert.NotNull(command.DescriptionKey);
            Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn(command.DescriptionKey!, culture)));
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
            Of(Id).NameKey, Of(Id).DescriptionKey!, "CrossRefTitle", "CrossRefTypeLabel", "CrossRefTypeHeading", "CrossRefTypeCaption", "CrossRefTypeFootnote", "CrossRefTypeEndnote", "CrossRefTypeBookmark", "CrossRefTargetLabel",
            "CrossRefKindLabel", "CrossRefKindLabelAndNumber", "CrossRefKindNumber", "CrossRefKindText", "CrossRefKindPage", "CrossRefNone", "CrossRefOk", "ReasonCrossRefRestricted", "NoteCrossRefGone", "NoteCrossRefInside",
            "NoteCrossRefNoNumber", "NoteCrossRefSeveral", "NoteCrossRefNotOffered", "NoteCrossRefIds", "NoteCrossRefCaptionUnread",
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
        Assert.Equal(WriteStrings.ReasonCrossRefRestricted, Reason(ObjectBlock.CrossRefRestricted));
        Assert.Equal(WriteStrings.ReasonSectionNotInBody, Reason(ObjectBlock.NoPosition));
        Assert.Equal(WriteStrings.ReasonEditOpening, Reason(ObjectBlock.None, EditBlockedReason.StillOpening));
        Assert.Equal(WriteStrings.EditUnavailable, Reason(ObjectBlock.None, EditBlockedReason.Faulted));
        Assert.Equal(WriteStrings.ReasonEditSaving, Reason(ObjectBlock.None, EditBlockedReason.Saving));

        // Each refusal that depends on the place alone maps to a block with words; what the target and the choice allow is a note when the reference is made.
        foreach (var refusal in Enum.GetValues<CrossRefRefusal>())
        {
            var block = ObjectWording.BlockOf(refusal);
            if (refusal is CrossRefRefusal.Prefix or CrossRefRefusal.RestrictedControl or CrossRefRefusal.InsideToc or CrossRefRefusal.NoPosition or CrossRefRefusal.NotInBody)
            {
                Assert.False(string.IsNullOrWhiteSpace(ObjectWording.ReasonText(block)), refusal.ToString());
            }
            else
            {
                Assert.Equal(ObjectBlock.None, block);
            }
        }
    }

    [AvaloniaFact]
    public async Task TheCardInsertsAReferenceThroughTheWindowAndUndoTakesItBack()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, HeadingStyle + "<w:r><w:t>Introduction</w:t></w:r>", "<w:r><w:t>see </w:t></w:r>");
        var cards = new FakeCards();
        window.FieldCards = cards;
        Caret(session, tab, 1, 4);
        Settle(window);
        Assert.True(Command(window, Id).IsEnabled);
        var start = tab.Docx.Model.Body.Blocks.Count;

        cards.Answer = context => new CrossRefRequest(context.TargetsOf(CrossRefTargetKind.Heading)[0], CrossRefKind.Text);
        Command(window, Id).Run!();
        WindowFixture.Pump();

        Assert.Equal(1, cards.Asked);
        Assert.Equal(CrossRefTargetKind.Heading, cards.Seen!.DefaultType);
        Assert.Equal(["Introduction"], cards.Seen.TargetsOf(CrossRefTargetKind.Heading).Select(t => t.Display).ToArray());
        Assert.Empty(cards.Seen.TargetsOf(CrossRefTargetKind.Footnote));
        Assert.Equal(start, tab.Docx.Model.Body.Blocks.Count);
        var reference = P(tab, 1);
        Assert.Equal("see Introduction", TextOf(reference));
        var field = Assert.Single(FieldView.InParagraph(reference));
        Assert.Equal(FieldKind.Ref, field.Support.Kind);
        Assert.True(field.Instruction.Has("h"));

        // The caret stands behind the reference, so typing goes on there.
        Assert.Same(reference, session.Selection!.Value.End.Paragraph);
        Assert.Equal(TextOf(reference).Length, session.Selection.Value.End.Offset);

        Assert.NotNull(session.Editor.History.Undo());
        Assert.Equal("see ", TextOf(P(tab, 1)));
        Assert.DoesNotContain(BookmarkOperations.AllNames(tab.Docx.Model), n => n.StartsWith("_Ref", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task ARefusalSaysWhyAndChangesNothing()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, HeadingStyle + "<w:r><w:t>Introduction</w:t></w:r>", "<w:r><w:t>see </w:t></w:r>");
        var view = window.ActiveView!;
        var notes = new List<string>();
        view.StructureNote += notes.Add;
        var heading = new CrossRefRequest(view.CrossReferenceCardContext().TargetsOf(CrossRefTargetKind.Heading)[0], CrossRefKind.Text);
        var revision = tab.Docx.Model.Edits.Revision;

        // A reference inside the text it shows: the editor refuses and the note names it.
        Caret(session, tab, 0, 5);
        Assert.True(view.RunCrossReferenceCommand(heading));
        Assert.Equal([WriteStrings.NoteCrossRefInside], notes);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);

        // A number of a heading that is not numbered.
        notes.Clear();
        Caret(session, tab, 1, 4);
        Assert.True(view.RunCrossReferenceCommand(new CrossRefRequest(heading.Target, CrossRefKind.NumberOnly)));
        Assert.Equal([WriteStrings.NoteCrossRefNoNumber], notes);

        // A prefix document is refused with its reason.
        notes.Clear();
        view.PrefixProbe = () => true;
        Assert.True(view.RunCrossReferenceCommand(heading));
        Assert.Equal([WriteStrings.ReasonSectionPrefix], notes);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);

        // A request that is not well formed is not taken.
        Assert.False(view.RunCrossReferenceCommand(new CrossRefRequest(heading.Target, CrossRefKind.LabelAndNumber)));
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
    }

    [AvaloniaFact]
    public async Task ACardClosedOrAnsweredAfterTheDocumentChangedChangesNothing()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture, HeadingStyle + "<w:r><w:t>Introduction</w:t></w:r>", "<w:r><w:t>one two three</w:t></w:r>");
        var cards = new FakeCards();
        window.FieldCards = cards;
        Caret(session, tab, 1, 3);
        Settle(window);

        cards.Answer = _ => null;
        Command(window, Id).Run!();
        WindowFixture.Pump();
        Assert.Equal("one two three", TextOf(P(tab, 1)));

        // The document changes while the card is open (typing behind it): the answer is refused with the note, never applied.
        cards.Answer = context => new CrossRefRequest(context.TargetsOf(CrossRefTargetKind.Heading)[0], CrossRefKind.Text);
        cards.WhileOpen = () => Assert.NotNull(session.Editor.InsertText(new TextRange(new TextPosition(StoryRef.Body, P(tab, 1), 0), new TextPosition(StoryRef.Body, P(tab, 1), 0)), "x"));
        Command(window, Id).Run!();
        WindowFixture.Pump();

        Assert.Equal("xone two three", TextOf(P(tab, 1)));
        Assert.Equal(WriteStrings.NoteCardStale, window.Status.Hint);
    }

    [AvaloniaFact]
    public async Task StartupCreatesNoCrossReferenceWork()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        Assert.False(window.FieldCardsExist);
        Assert.False(window.FieldFactsExist);
        var (_, tab, session) = await OpenAsync(fixture, HeadingStyle + "<w:r><w:t>one</w:t></w:r>", "<w:r><w:t>two</w:t></w:r>");
        window = fixture.Window;
        var revision = tab.Docx.Model.Edits.Revision;
        Caret(session, tab, 1, 0);
        Settle(window);
        WindowFixture.Pump();

        // Opening and a caret build no cards object, no bookmark and no step; the facts are read only when the command is asked, and then once per place and command.
        Assert.False(window.FieldCardsExist);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.False(session.Editor.History.CanUndo);
        Assert.DoesNotContain(BookmarkOperations.AllNames(tab.Docx.Model), n => n.StartsWith("_Ref", StringComparison.Ordinal));
        Assert.True(Command(window, Id).IsEnabled);
        var reads = window.FieldFactReads;
        Settle(window);
        Assert.Equal(reads, window.FieldFactReads);
    }
}
