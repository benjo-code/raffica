using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
/// The picture commands (milestone 4.5a, Task 12): ids, icons, wording, the rules with a reason for every disabled state, the window run path with its one history step each, the file and the cards of Insert
/// picture, and the start cost. The document is <see cref="FloatingDocx"/>: a floating picture in block 1, a picture in line with the text in block 4 and a table in block 6.
/// </summary>
public sealed class PictureCommandTests
{
    private static readonly string[] s_ids = ["picture.insert", "picture.properties", "picture.wrap", "picture.reset", "picture.delete"];
    private static readonly string[] s_needSelection = ["picture.properties", "picture.wrap", "picture.reset", "picture.delete"];

    private static CatalogCommand Of(string id) => WriteCommandCatalog.Commands.Single(c => c.Id == id);

    private static CommandDefinition Command(WriteWindow window, string id) =>
        window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id);

    private static async Task<(WriteWindow Window, WriteTab Tab, EditSession Session)> OpenAsync(WindowFixture fixture)
    {
        var path = FloatingDocx.Write(fixture.Folder);
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

    private static void CaretIn(EditSession session, WriteTab tab, int block)
    {
        var at = new TextPosition(StoryRef.Body, P(tab, block), 0);
        session.SetSelection(new TextRange(at, at));
    }

    // Selects the picture at the start of a paragraph (the inline one is block 4, the floating one block 1).
    private static void Select(EditSession session, WriteTab tab, int block)
    {
        var id = session.Editor.PictureAt(new TextPosition(StoryRef.Body, P(tab, block), 0));
        Assert.NotNull(id);
        Assert.True(session.SetObjectSelection(new PictureSelection(id)));
    }

    private static PictureInfo InfoOf(WriteTab tab, int block) => PictureView.Of(P(tab, block).Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<Picture>().Single())!;

    private static int PicturesIn(WriteTab tab) => tab.Docx.Model.Body.Blocks.OfType<Paragraph>().Sum(p => p.Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<Picture>().Count());

    private static void Settle(WriteWindow window)
    {
        window.FormatRefresh?.Flush();
        window.ViewStatesRefresh?.Flush();
        WindowFixture.Pump();
        window.Ribbon.RefreshCommandStates();
        WindowFixture.Pump();
    }

    // ---- ids, keys, wording --------------------------------------------------------------------------------------------------

    [Fact]
    public void TheIdsAreCatalogEntriesOfTheInsertTabWithNoKeys()
    {
        Assert.All(s_ids, id => Assert.Single(WriteCommandCatalog.Commands, c => c.Id == id));
        Assert.All(s_ids, id => Assert.True(WriteCommandIds.IsPicture(id), id));
        Assert.Equal(["picture.wrap"], s_ids.Where(WriteCommandIds.TakesPayload).ToArray());
        Assert.All(s_ids, id => Assert.False(Of(id).Toggle));
        Assert.All(s_ids, id => Assert.Null(Of(id).ShortcutFor(macOs: false)));
        Assert.All(s_ids, id => Assert.Null(Of(id).ShortcutFor(macOs: true)));
        Assert.All(s_ids, id => Assert.Equal(WriteRibbon.InsertLetter, Of(id).Tab.Letter));
        Assert.All(s_ids, id => Assert.Equal("2", Of(id).Group.Digit));
        Assert.Equal(CommandSize.Large, Of("picture.insert").Size);
        Assert.False(WriteCommandIds.IsPicture("table.insert"));
        Assert.False(WriteCommandIds.IsTable("picture.insert"));
        Assert.False(WriteCommandIds.IsNote("picture.insert"));

        // The Insert tab: the table group (1), the picture group (2) and the notes (3); the field codes are unique and none begins another.
        var insert = WriteCommandCatalog.Tabs.Single(t => t.Letter == WriteRibbon.InsertLetter);
        Assert.Equal(["1", "2", "3"], insert.Groups.Select(g => g.Digit).ToArray());
        var codes = insert.Groups.SelectMany(g => g.Commands.Select(c => c.FieldCode)).ToList();
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
    public void EveryPictureCommandHasItsOwnIconBothLanguagesAndAMacMenuLine()
    {
        foreach (var group in WriteCommandCatalog.Commands.GroupBy(c => c.Icon).Where(g => g.Count() > 1))
        {
            Assert.Fail($"Icon {group.Key} serves {string.Join(", ", group.Select(c => c.Id))}");
        }

        var icons = XDocument.Load(RepoPaths.Combine("src", "Raffica.Shell", "Styles", "Icons.axaml")).Root!.Elements()
            .ToDictionary(e => (string)e.Attribute(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))!, e => e.Value.Trim());
        var drawn = s_ids.Select(id => icons["Raffica.Icon." + Of(id).Icon]).ToList();
        Assert.Equal(s_ids.Length, drawn.Distinct().Count());
        foreach (var id in s_ids)
        {
            var command = Of(id);
            Assert.True(Application.Current!.TryGetResource("Raffica.Icon." + command.Icon, Application.Current!.ActualThemeVariant, out var value), id);
            var geometry = Assert.IsAssignableFrom<Geometry>(value);
            Assert.True(geometry.Bounds.Width > 4 && geometry.Bounds.Height > 4, id);
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

            Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn("GroupPicture", culture)));
        }

        var window = new WriteMacMenuTests.FakeWindow { HasTab = true };
        window.Allowed.UnionWith(s_ids);
        var menu = new WriteMacMenu(() => window, () => Task.CompletedTask, WriteMenuPart.Window);
        menu.Refresh();
        var insert = menu.Menu.Items.OfType<NativeMenuItem>().Single(i => i.Header == WriteStrings.MenuInsert).Menu!.Items.OfType<NativeMenuItem>().ToList();
        foreach (var id in s_ids.Where(id => id != "picture.wrap"))
        {
            var item = Assert.Single(insert, i => i.Header == Of(id).Name);
            Assert.Null(item.Gesture);
            Assert.True(item.Command!.CanExecute(null), id);
            item.Command.Execute(null);
            Assert.Equal("run " + id, window.Calls[^1]);
        }

        var wrap = Assert.Single(insert, i => i.Header == Of("picture.wrap").Name);
        Assert.Equal(PicturePayloads.Wraps.Count, wrap.Menu!.Items.Count);
        wrap.Menu.Items.OfType<NativeMenuItem>().First().Command!.Execute(null);
        Assert.Equal("run picture.wrap inline", window.Calls[^1]);

        window.Allowed.Clear();
        menu.Refresh();
        Assert.All(insert.Where(item => item.Command is not null && s_ids.Any(id => Of(id).Name == (string?)item.Header)), item => Assert.False(item.Command!.CanExecute(null)));
    }

    [Fact]
    public void TheTooMuchMediaReasonSaysThatSavingAndOpeningAgainFreesRoom()
    {
        // The cap counts the pictures added since the file was opened (also those already saved), so a save and a reopen is the way out.
        Assert.Contains("open it again", WriteStrings.GetIn(PictureWording.KeyOf(PictureRefusal.TooMuchMedia), CultureInfo.GetCultureInfo("en")), StringComparison.Ordinal);
        Assert.Contains("erneut", WriteStrings.GetIn(PictureWording.KeyOf(PictureRefusal.TooMuchMedia), CultureInfo.GetCultureInfo("de")), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryRefusalOfTheEditorHasWordsInBothLanguagesAndNoWordingNamesAFileBytesOrADescription()
    {
        foreach (var language in new[] { "en", "de" })
        {
            var culture = CultureInfo.GetCultureInfo(language);
            foreach (var refusal in Enum.GetValues<PictureRefusal>())
            {
                var text = WriteStrings.GetIn(PictureWording.KeyOf(refusal), culture);
                Assert.False(string.IsNullOrWhiteSpace(text), $"{refusal} in {language}");
                Assert.DoesNotContain("{", text, StringComparison.Ordinal);
                Assert.DoesNotContain("  ", text, StringComparison.Ordinal);
                Assert.True(text.Length < 140, $"{refusal} in {language}: {text}");
            }

            foreach (var key in new[] { "ReasonPictureNoSelection", "ReasonPicturePrefixDocument", "NotePictureFailed", "NotePictureReadFailed", "NotePictureNotCopied", "NotePasteLeftOutPictures", "ReasonNoteIdsUsedUp", "NoteCellsDeleteKey", "PictureWrapInline", "PictureWrapSquare", "PictureWrapTopAndBottom", "PictureWrapInFront", "PictureWrapBehind" })
            {
                Assert.False(string.IsNullOrWhiteSpace(WriteStrings.GetIn(key, culture)), key);
            }
        }

        Assert.All(Enum.GetValues<PictureRefusal>(), r => Assert.False(string.IsNullOrWhiteSpace(PictureWording.ReasonText(r))));
        Assert.All(Enum.GetValues<PictureBlock>().Where(b => b != PictureBlock.None), b => Assert.False(string.IsNullOrWhiteSpace(PictureWording.ReasonText(new PictureCommandFacts(b, PictureRefusal.NotAnImage))), b.ToString()));
        Assert.Null(PictureWording.ReasonText(PictureCommandFacts.Permissive));
    }

    // ---- rules ---------------------------------------------------------------------------------------------------------------

    private static IEnumerable<PictureCommandFacts> Facts() =>
        Enum.GetValues<PictureBlock>().Where(b => b != PictureBlock.Refused).Select(b => new PictureCommandFacts(b))
            .Concat(Enum.GetValues<PictureRefusal>().Select(r => new PictureCommandFacts(PictureBlock.Refused, r)));

    [Fact]
    public void EveryDisabledStateHasAReason()
    {
        var count = 0;
        foreach (var blocked in new EditBlockedReason?[] { null, EditBlockedReason.StillOpening, EditBlockedReason.Saving, EditBlockedReason.Faulted })
        {
            foreach (var facts in Facts())
            {
                var state = new EditCommandState(blocked, true, true, true);
                var rules = new WriteCommandRules(() => true, () => 1.0, () => true, () => state, pictureFacts: _ => facts);
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

        Assert.True(count >= 300);

        var none = new WriteCommandRules(() => false, () => 1.0, () => true, () => new EditCommandState(null, false, false, false));
        Assert.All(s_ids, id => Assert.Null(none.ReasonFor(id)));
    }

    [Fact]
    public void TheRulesWordEachReason()
    {
        static string? Reason(string id, PictureCommandFacts facts, EditBlockedReason? blocked = null)
        {
            var rules = new WriteCommandRules(() => true, () => 1.0, () => true, () => new EditCommandState(blocked, true, true, true), pictureFacts: _ => facts);
            return rules.ReasonFor(id);
        }

        Assert.All(s_ids, id => Assert.Null(Reason(id, PictureCommandFacts.Permissive)));
        Assert.Equal(WriteStrings.ReasonPictureNoSelection, Reason(WriteCommandIds.PictureDelete, new PictureCommandFacts(PictureBlock.NoSelection)));
        Assert.Equal(WriteStrings.ReasonPicturePrefixDocument, Reason(WriteCommandIds.PictureInsert, new PictureCommandFacts(PictureBlock.PrefixDocument)));
        Assert.Equal(WriteStrings.ReasonPictureRotated, Reason(WriteCommandIds.PictureReset, new PictureCommandFacts(PictureBlock.Refused, PictureRefusal.Rotated)));
        Assert.Equal(WriteStrings.ReasonPictureFloatInCell, Reason(WriteCommandIds.PictureWrap, new PictureCommandFacts(PictureBlock.Refused, PictureRefusal.FloatInCell)));
        Assert.Equal(WriteStrings.ReasonPictureHeaderOrFooter, Reason(WriteCommandIds.PictureInsert, new PictureCommandFacts(PictureBlock.Refused, PictureRefusal.HeaderOrFooter)));
        Assert.Equal(WriteStrings.ReasonEditOpening, Reason(WriteCommandIds.PictureDelete, PictureCommandFacts.Permissive, EditBlockedReason.StillOpening));
        Assert.Equal(WriteStrings.EditUnavailable, Reason(WriteCommandIds.PictureInsert, PictureCommandFacts.Permissive, EditBlockedReason.Faulted));
        Assert.Equal(WriteStrings.ReasonEditSaving, Reason(WriteCommandIds.PictureInsert, PictureCommandFacts.Permissive, EditBlockedReason.Saving));
    }

    // ---- the window run path ----------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task WithNoPictureSelectedOnlyInsertIsOfferedAndTheOthersSayWhy()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        CaretIn(session, tab, 0);
        Settle(window);

        Assert.True(Command(window, WriteCommandIds.PictureInsert).IsEnabled);
        foreach (var id in s_needSelection)
        {
            Assert.False(Command(window, id).IsEnabled, id);
            Assert.Equal(WriteStrings.ReasonPictureNoSelection, Command(window, id).CurrentDisabledReason);
            Assert.False(window.RunCommand(id));
        }

        // A picture in line with the text: every command is on; a picture that already has its original size has nothing to reset (the reason says so) and the wrap picker is on.
        Select(session, tab, 4);
        Settle(window);
        foreach (var id in new[] { "picture.insert", "picture.properties", "picture.wrap", "picture.reset", "picture.delete" })
        {
            Assert.True(Command(window, id).IsEnabled, id + ": " + Command(window, id).CurrentDisabledReason);
        }

        // In the writing view no picture is selected (objects belong to the page view), so the commands that need one say so; Insert still works.
        window.SetView(WriteView.Writing);
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.PictureInsert).IsEnabled);
        Assert.False(Command(window, WriteCommandIds.PictureDelete).IsEnabled);
        Assert.Equal(WriteStrings.ReasonPictureNoSelection, Command(window, WriteCommandIds.PictureDelete).CurrentDisabledReason);
        window.SetView(WriteView.Page);
    }

    [AvaloniaFact]
    public async Task ASelectedPictureRunsItsCommandsEachAsOneUndoStep()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        Select(session, tab, 4);
        Settle(window);
        var drawing = session.SelectedDrawing;
        var selected = Assert.IsType<PictureSelection>(session.ObjectSelection).Id;
        var steps = session.Editor.History.UndoCount;
        var revision = tab.Docx.Model.Edits.Revision;

        // A payload that is no wrap of the list, a missing one and a value of another command change nothing.
        Assert.False(window.RunCommand(WriteCommandIds.PictureWrap, "sparkles"));
        Assert.False(window.RunCommand(WriteCommandIds.PictureWrap, null));
        Assert.False(window.RunCommand(WriteCommandIds.PictureWrap, 7));
        Assert.False(window.ActiveView!.RunPictureCommand(WriteCommandIds.PictureDelete, "now"));
        Assert.False(window.ActiveView.RunPictureCommand(WriteCommandIds.PictureInsert, null));
        Assert.False(window.ActiveView.RunPictureCommand(WriteCommandIds.PictureInsert, new PictureFile([], string.Empty)));
        Assert.False(window.ActiveView.RunPictureCommand(WriteCommandIds.PictureProperties, new PictureProperties()));
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);

        // Wrap: in line to square (the picture is a floating one now), kept selected.
        Assert.True(window.RunCommand(WriteCommandIds.PictureWrap, "square"));
        WindowFixture.Pump();
        Assert.Equal(PictureWrap.Square, InfoOf(tab, 4).Wrap);
        Assert.Equal(steps + 1, session.Editor.History.UndoCount);
        Assert.Same(drawing, session.SelectedDrawing);
        Assert.Equal(selected, Assert.IsType<PictureSelection>(session.ObjectSelection).Id);

        // Original size: the pixels of the picture at 96 DPI (8 by 8 pixels).
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.PictureReset).IsEnabled);
        Command(window, WriteCommandIds.PictureReset).Run!();
        WindowFixture.Pump();
        Assert.Equal(new EmuSize(8 * 9525, 8 * 9525), InfoOf(tab, 4).ExtentEmu);
        Assert.Equal(steps + 2, session.Editor.History.UndoCount);
        Assert.Same(drawing, session.SelectedDrawing);
        Assert.Equal(selected, Assert.IsType<PictureSelection>(session.ObjectSelection).Id);

        // Now it is at its original size: the command is off and says why.
        Settle(window);
        Assert.False(Command(window, WriteCommandIds.PictureReset).IsEnabled);
        Assert.Equal(WriteStrings.ReasonPictureNothingToChange, Command(window, WriteCommandIds.PictureReset).CurrentDisabledReason);

        // Delete: the picture goes, the selection with it, one step; undo brings the picture back.
        Command(window, WriteCommandIds.PictureDelete).Run!();
        WindowFixture.Pump();
        Assert.Equal(1, PicturesIn(tab));
        Assert.Null(session.ObjectSelection);
        Assert.Equal(steps + 3, session.Editor.History.UndoCount);
        Assert.NotNull(session.Editor.History.Undo());
        Assert.Equal(2, PicturesIn(tab));
        Assert.NotNull(session.Editor.History.Undo());
        Assert.NotNull(session.Editor.History.Undo());
        Assert.Equal(new EmuSize(1828800, 914400), InfoOf(tab, 4).ExtentEmu);
        Assert.Equal(PictureWrap.Inline, InfoOf(tab, 4).Wrap);
        Assert.Equal(steps, session.Editor.History.UndoCount);
    }


    [AvaloniaFact]
    public async Task AKeyboardOnlyRouteOpensThePropertiesAndChangesTheAltText()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        var cards = new FakePictureCards { Properties = new PictureProperties(AltText: "Typed with the keyboard") };
        window.PictureCards = cards;

        // No pointer: the text selection is the one character of the picture in block 4 (Shift and an arrow next to it makes exactly this).
        var paragraph = P(tab, 4);
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, paragraph, 0), new TextPosition(StoryRef.Body, paragraph, 1)));
        Settle(window);
        Assert.Null(session.ObjectSelection);
        foreach (var id in s_needSelection)
        {
            Assert.True(Command(window, id).IsEnabled, id + ": " + Command(window, id).CurrentDisabledReason);
        }

        var steps = session.Editor.History.UndoCount;
        Command(window, WriteCommandIds.PictureProperties).Run!();
        await WindowFixture.WaitAsync(() => cards.PropertiesAsked == 1);
        await WindowFixture.WaitAsync(() => session.Editor.History.UndoCount == steps + 1);

        Assert.Equal("An inline picture", cards.LastContext!.AltText);
        Assert.Equal("Typed with the keyboard", InfoOf(tab, 4).AltText);

        // The wrap picker and Delete work the same way (the picture floats now: the one floating picture of its paragraph is the selection's target).
        Assert.True(window.RunCommand(WriteCommandIds.PictureWrap, "square"));
        WindowFixture.Pump();
        Assert.Equal(PictureWrap.Square, InfoOf(tab, 4).Wrap);
        Settle(window);
        Assert.True(window.RunCommand(WriteCommandIds.PictureDelete));
        WindowFixture.Pump();
        Assert.Equal(1, PicturesIn(tab));

        // A caret, or a selection of other text, picks no picture, and the reason says how to select one.
        var other = P(tab, 0);
        session.SetSelection(new TextRange(new TextPosition(StoryRef.Body, other, 0), new TextPosition(StoryRef.Body, other, 1)));
        Settle(window);
        Assert.False(Command(window, WriteCommandIds.PictureProperties).IsEnabled);
        Assert.Equal(WriteStrings.ReasonPictureNoSelection, Command(window, WriteCommandIds.PictureProperties).CurrentDisabledReason);
    }

    [AvaloniaFact]
    public async Task TheCopyAndCutKeysWithAPictureSelectedSayThatAPictureIsNotCopied()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        Select(session, tab, 4);
        Settle(window);
        var revision = tab.Docx.Model.Edits.Revision;

        foreach (var key in new[] { PhysicalKey.C, PhysicalKey.X })
        {
            window.Status.Hint = null;
            window.ActiveView!.Focus();
            window.KeyPressQwerty(key, RawInputModifiers.Control);
            window.KeyReleaseQwerty(key, RawInputModifiers.Control);
            WindowFixture.Pump();
            Assert.Equal(WriteStrings.NotePictureNotCopied, window.Status.Hint);
        }

        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.Equal(2, PicturesIn(tab));
    }
    [AvaloniaFact]
    public async Task ThePropertiesCardIsOneHistoryStepAndReadsThePictureWhenItOpens()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        Select(session, tab, 4);
        Settle(window);
        var drawing = session.SelectedDrawing;
        var selectedId = Assert.IsType<PictureSelection>(session.ObjectSelection).Id;
        var cards = new FakePictureCards { Properties = new PictureProperties(WidthEmu: 1_270_000, HeightEmu: 635_000, AltText: "A new description", Wrap: PictureWrap.TopAndBottom) };
        window.PictureCards = cards;
        var steps = session.Editor.History.UndoCount;

        Command(window, WriteCommandIds.PictureProperties).Run!();
        await WindowFixture.WaitAsync(() => cards.PropertiesAsked == 1);
        await WindowFixture.WaitAsync(() => session.Editor.History.UndoCount == steps + 1);

        var context = cards.LastContext!;
        Assert.Equal((2880, 1440), (context.WidthTwips, context.HeightTwips));
        Assert.Equal(("An inline picture", "inline", false, true), (context.AltText, context.WrapId, context.Rotated, context.CanFloat));
        Assert.Equal(window.ActiveView!.MarginsInInches(), context.Inches);
        var info = InfoOf(tab, 4);
        Assert.Equal(new EmuSize(1_270_000, 635_000), info.ExtentEmu);
        Assert.Equal("A new description", info.AltText);
        Assert.Equal(PictureWrap.TopAndBottom, info.Wrap);
        Assert.Same(drawing, session.SelectedDrawing);
        Assert.Equal(selectedId, Assert.IsType<PictureSelection>(session.ObjectSelection).Id);

        // One undo takes all three parts back.
        Assert.NotNull(session.Editor.History.Undo());
        var back = InfoOf(tab, 4);
        Assert.Equal((new EmuSize(1828800, 914400), "An inline picture", PictureWrap.Inline), (back.ExtentEmu, back.AltText, back.Wrap));

        // A cancelled card changes nothing.
        Select(session, tab, 4);
        cards.Properties = null;
        var revision = tab.Docx.Model.Edits.Revision;
        Command(window, WriteCommandIds.PictureProperties).Run!();
        await WindowFixture.WaitAsync(() => cards.PropertiesAsked == 2);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
    }

    // ---- Insert picture --------------------------------------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task CancelOnThePickerAndOnTheCardChangesNothing()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        CaretIn(session, tab, 0);
        Settle(window);
        var png = Path.Combine(fixture.Folder, "small.png");
        File.WriteAllBytes(png, FloatingDocx.Png());
        var cards = new FakePictureCards { Insert = null };
        window.PictureCards = cards;
        var revision = tab.Docx.Model.Edits.Revision;
        var pictures = PicturesIn(tab);

        // Cancel on the file dialog: no card, no change.
        var asked = 0;
        window.PictureFilePicker = () =>
        {
            asked++;
            return Task.FromResult<string?>(null);
        };
        Command(window, WriteCommandIds.PictureInsert).Run!();
        await WindowFixture.WaitAsync(() => asked == 1);
        WindowFixture.Pump();
        Assert.Equal(0, cards.InsertAsked);

        // Cancel on the card: the file was read, nothing is inserted.
        window.PictureFilePicker = () =>
        {
            asked++;
            return Task.FromResult<string?>(png);
        };
        Command(window, WriteCommandIds.PictureInsert).Run!();
        await WindowFixture.WaitAsync(() => cards.InsertAsked == 1);
        WindowFixture.Pump();
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.Equal(pictures, PicturesIn(tab));
        Assert.Equal(0, session.Editor.History.UndoCount);
    }

    [AvaloniaFact]
    public async Task AnEmptyAltTextIsAllowedAndEditableLater()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        CaretIn(session, tab, 0);
        Settle(window);
        var png = Path.Combine(fixture.Folder, "small.png");
        File.WriteAllBytes(png, FloatingDocx.Png(16));
        var cards = new FakePictureCards { Insert = string.Empty };
        window.PictureCards = cards;
        window.PictureFilePicker = () => Task.FromResult<string?>(png);
        var pictures = PicturesIn(tab);

        Command(window, WriteCommandIds.PictureInsert).Run!();
        await WindowFixture.WaitAsync(() => PicturesIn(tab) == pictures + 1);

        // One step, in line with the text, no description, and the picture is not selected (the caret stands behind it).
        Assert.Equal(1, session.Editor.History.UndoCount);
        var inserted = P(tab, 0).Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<Picture>().Single();
        var info = PictureView.Of(inserted)!;
        Assert.Equal(PictureWrap.Inline, info.Wrap);
        Assert.True(string.IsNullOrEmpty(info.AltText));
        Assert.Equal(new EmuSize(16 * 9525, 16 * 9525), info.ExtentEmu);
        Assert.Null(session.ObjectSelection);

        // The description is written later through Picture properties.
        Select(session, tab, 0);
        cards.Properties = new PictureProperties(AltText: "Written later");
        Settle(window);
        Command(window, WriteCommandIds.PictureProperties).Run!();
        await WindowFixture.WaitAsync(() => session.Editor.History.UndoCount == 2);
        Assert.Equal("Written later", PictureView.Of(P(tab, 0).Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<Picture>().Single())!.AltText);

        // And an insert with a description writes it at once.
        CaretIn(session, tab, 2);
        cards.Insert = "At once";
        Command(window, WriteCommandIds.PictureInsert).Run!();
        await WindowFixture.WaitAsync(() => PicturesIn(tab) == pictures + 2);
        Assert.Equal("At once", PictureView.Of(P(tab, 2).Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<Picture>().Single())!.AltText);
    }

    [Fact]
    public void AFileIsReadOnlyWithinTheLimitAndTheSizeIsCheckedBeforeAnyByteIsRead()
    {
        var folder = Directory.CreateTempSubdirectory("raffica-write-picture-read-").FullName;
        try
        {
            var small = Path.Combine(folder, "small.png");
            var bytes = FloatingDocx.Png();
            File.WriteAllBytes(small, bytes);
            var (read, readRefusal) = WriteWindow.ReadPictureFile(small);
            Assert.Equal(bytes, read);
            Assert.Null(readRefusal);
            Assert.Equal((false, false), Gone(Path.Combine(folder, "missing.png")));
            Assert.Equal((false, false), Gone(folder));

            // An empty file is no picture (refused before the card), and neither is a directory.
            var empty = Path.Combine(folder, "empty.png");
            File.WriteAllBytes(empty, []);
            var (emptyBytes, emptyRefusal) = WriteWindow.ReadPictureFile(empty);
            Assert.Null(emptyBytes);
            Assert.Equal(PictureRefusal.NotAnImage, emptyRefusal);

            // One byte above the limit: refused from the size alone (a file that is not readable by anyone could not be read at all).
            var big = Path.Combine(folder, "big.png");
            using (var stream = new FileStream(big, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.SetLength(PictureRules.MaxBytes + 1);
            }

            var (none, refusal) = WriteWindow.ReadPictureFile(big);
            Assert.Null(none);
            Assert.Equal(PictureRefusal.TooLarge, refusal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // No bytes and no refusal: a file that is not there or cannot be read.
    private static (bool Bytes, bool Refusal) Gone(string path)
    {
        var (bytes, refusal) = WriteWindow.ReadPictureFile(path);
        return (bytes is not null, refusal is not null);
    }

    [AvaloniaFact]
    public async Task AnEmptyFileAndAPickWithNoLocalPathAreRefusedBeforeTheCard()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        CaretIn(session, tab, 0);
        Settle(window);
        var cards = new FakePictureCards { Insert = "never" };
        window.PictureCards = cards;
        var revision = tab.Docx.Model.Edits.Revision;

        var empty = Path.Combine(fixture.Folder, "SECRET-NAME-empty.png");
        File.WriteAllBytes(empty, []);
        window.PictureFilePicker = () => Task.FromResult<string?>(empty);
        Command(window, WriteCommandIds.PictureInsert).Run!();
        await WindowFixture.WaitAsync(() => window.Status.Hint == PictureWording.ReasonText(PictureRefusal.NotAnImage));
        Assert.DoesNotContain("SECRET", window.Status.Hint ?? string.Empty, StringComparison.Ordinal);

        // A directory is no file: "could not be read".
        window.PictureFilePicker = () => Task.FromResult<string?>(fixture.Folder);
        Command(window, WriteCommandIds.PictureInsert).Run!();
        await WindowFixture.WaitAsync(() => window.Status.Hint == WriteStrings.NotePictureReadFailed);

        // A pick that has no local path (the picker answers an empty path): a note, not silence.
        window.Status.Hint = null;
        window.PictureFilePicker = () => Task.FromResult<string?>(string.Empty);
        Command(window, WriteCommandIds.PictureInsert).Run!();
        await WindowFixture.WaitAsync(() => window.Status.Hint == WriteStrings.NotePictureReadFailed);

        Assert.Equal(0, cards.InsertAsked);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
    }

    [AvaloniaFact]
    public async Task ASecondInsertWhileTheFirstIsOpenDoesNothingAndAfterCancelInsertWorksAgain()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        CaretIn(session, tab, 0);
        Settle(window);
        var png = Path.Combine(fixture.Folder, "small.png");
        File.WriteAllBytes(png, FloatingDocx.Png());
        var picks = 0;
        window.PictureFilePicker = () =>
        {
            picks++;
            return Task.FromResult<string?>(png);
        };
        var gate = new TaskCompletionSource<string?>();
        var cards = new FakePictureCards { Gate = gate };
        window.PictureCards = cards;
        var pictures = PicturesIn(tab);

        Command(window, WriteCommandIds.PictureInsert).Run!();
        await WindowFixture.WaitAsync(() => cards.InsertAsked == 1);

        // The card is open: a second Insert opens no second picker and no second card.
        Command(window, WriteCommandIds.PictureInsert).Run!();
        WindowFixture.Pump();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        WindowFixture.Pump();
        Assert.Equal((1, 1), (picks, cards.InsertAsked));

        gate.SetResult(null);
        await WindowFixture.WaitAsync(() => window.PictureFlowIdle);
        Assert.Equal(pictures, PicturesIn(tab));

        // After the cancel the flow is free again.
        cards.Gate = null;
        cards.Insert = "again";
        Command(window, WriteCommandIds.PictureInsert).Run!();
        await WindowFixture.WaitAsync(() => PicturesIn(tab) == pictures + 1);
        Assert.Equal((2, 2), (picks, cards.InsertAsked));
    }

    [AvaloniaFact]
    public async Task AFileThatIsTooLargeOrUnreadableIsRefusedInWordsThatNameNeitherFileNorBytes()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        CaretIn(session, tab, 0);
        Settle(window);
        var cards = new FakePictureCards { Insert = "never" };
        window.PictureCards = cards;
        var revision = tab.Docx.Model.Edits.Revision;

        var big = Path.Combine(fixture.Folder, "SECRET-NAME-big.png");
        using (var stream = new FileStream(big, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(PictureRules.MaxBytes + 1);
        }

        window.PictureFilePicker = () => Task.FromResult<string?>(big);
        Command(window, WriteCommandIds.PictureInsert).Run!();
        await WindowFixture.WaitAsync(() => window.Status.Hint == PictureWording.ReasonText(PictureRefusal.TooLarge));
        Assert.DoesNotContain("SECRET", window.Status.Hint ?? string.Empty, StringComparison.Ordinal);

        window.PictureFilePicker = () => Task.FromResult<string?>(Path.Combine(fixture.Folder, "SECRET-NAME-missing.png"));
        Command(window, WriteCommandIds.PictureInsert).Run!();
        await WindowFixture.WaitAsync(() => window.Status.Hint == WriteStrings.NotePictureReadFailed);
        Assert.DoesNotContain("SECRET", window.Status.Hint ?? string.Empty, StringComparison.Ordinal);

        // Bytes that are no picture: the card is asked, the editor refuses, and the note says so without a word of the file.
        var junk = Path.Combine(fixture.Folder, "junk.png");
        File.WriteAllText(junk, "SECRET-BYTES this is no picture");
        window.PictureFilePicker = () => Task.FromResult<string?>(junk);
        Command(window, WriteCommandIds.PictureInsert).Run!();
        await WindowFixture.WaitAsync(() => window.Status.Hint == PictureWording.ReasonText(PictureRefusal.NotAnImage));
        Assert.DoesNotContain("SECRET", window.Status.Hint ?? string.Empty, StringComparison.Ordinal);

        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.Equal(1, cards.InsertAsked);
    }

    // ---- prefix document, save, start cost, pickers -----------------------------------------------------------------------------

    [AvaloniaFact]
    public async Task EveryPictureCommandIsDisabledWithItsReasonInAPrefixDocumentAndDuringASave()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        Select(session, tab, 4);
        Settle(window);
        Assert.True(Command(window, WriteCommandIds.PictureDelete).IsEnabled);

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
        Assert.True(Command(window, WriteCommandIds.PictureDelete).IsEnabled);

        // A prefix document (the first blocks only): the facts say so without a session, and the view refuses with the same words.
        var thesis = Path.Combine(fixture.Folder, "long.docx");
        File.Copy(StyledDocx.Write(fixture.Folder, "long-source.docx", StyledDocx.Styles, Enumerable.Range(0, 120).Select(i => "<w:r><w:t>paragraph " + i + "</w:t></w:r>").ToArray()), thesis, overwrite: true);
        using var h = new ViewHarness(thesis, 900, 700);
        Assert.True(h.Tab.Docx.IsPrefix);
        foreach (var id in s_ids)
        {
            Assert.Equal(PictureBlock.PrefixDocument, new PictureFactsSource().Read(h.Tab, id).Block);
        }

        var notes = new List<string>();
        h.View.StructureNote += notes.Add;
        var before = h.Tab.Docx.Model.Edits.Revision;
        Assert.True(h.View.RunPictureCommand(WriteCommandIds.PictureInsert, new PictureFile(FloatingDocx.Png(), string.Empty)));
        Assert.Equal([WriteStrings.ReasonPicturePrefixDocument], notes);
        Assert.Equal(before, h.Tab.Docx.Model.Edits.Revision);
    }

    [AvaloniaFact]
    public async Task StartupCreatesNoPictureWork()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        Assert.False(window.PictureFactsExist);
        var (_, tab, session) = await OpenAsync(fixture);
        window = fixture.Window;
        var revision = tab.Docx.Model.Edits.Revision;
        Select(session, tab, 4);
        Settle(window);
        WindowFixture.Pump();

        // The Home band is the active tab: no fact of a picture command is read, no list is asked, nothing changed, no card made.
        Assert.False(window.PictureFactsExist);
        Assert.Equal(0, window.PictureFactReads);
        Assert.Equal(0, window.PicturePickerSet.ListReads);
        Assert.Equal(revision, tab.Docx.Model.Edits.Revision);
        Assert.Equal(0, session.Editor.History.UndoCount);

        // The first question makes the source and reads once per command; the same selection and revision read nothing again.
        Assert.True(Command(window, WriteCommandIds.PictureDelete).IsEnabled);
        Assert.True(window.PictureFactsExist);
        var reads = window.PictureFactReads;
        Assert.True(reads >= 1);
        _ = Command(window, WriteCommandIds.PictureDelete).IsEnabled;
        Assert.Equal(reads, window.PictureFactReads);
        Assert.Equal(0, window.PicturePickerSet.ListReads);
    }

    [AvaloniaFact]
    public async Task TheWrapPickerListsItsFiveWrapsWhenItOpensAndItsChoiceRuns()
    {
        using var fixture = new WindowFixture();
        var (window, tab, session) = await OpenAsync(fixture);
        Select(session, tab, 4);
        Settle(window);
        var picker = Command(window, WriteCommandIds.PictureWrap).Picker!;
        Assert.Equal(0, window.PicturePickerSet.ListReads);

        var items = picker.Items();
        Assert.Equal(["inline", "square", "topAndBottom", "inFront", "behind"], items.Select(i => i.Id).ToArray());
        Assert.Equal([WriteStrings.PictureWrapInline, WriteStrings.PictureWrapSquare, WriteStrings.PictureWrapTopAndBottom, WriteStrings.PictureWrapInFront, WriteStrings.PictureWrapBehind], items.Select(i => i.Label).ToArray());
        Assert.Equal(1, window.PicturePickerSet.ListReads);

        var steps = session.Editor.History.UndoCount;
        picker.Chosen("behind");
        WindowFixture.Pump();
        Assert.Equal(steps + 1, session.Editor.History.UndoCount);
        Assert.Equal(PictureWrap.Behind, InfoOf(tab, 4).Wrap);
    }

    private sealed class FakePictureCards : IPictureCards
    {
        public string? Insert { get; set; }

        // A card that stays open until the test answers it.
        public TaskCompletionSource<string?>? Gate { get; set; }

        public PictureProperties? Properties { get; set; }

        public int InsertAsked { get; private set; }

        public int PropertiesAsked { get; private set; }

        public PicturePropertiesContext? LastContext { get; private set; }

        public Task<string?> AskInsertAsync()
        {
            InsertAsked++;
            return Gate?.Task ?? Task.FromResult(Insert);
        }

        public Task<PictureProperties?> AskPropertiesAsync(PicturePropertiesContext context)
        {
            PropertiesAsked++;
            LastContext = context;
            return Task.FromResult(Properties);
        }
    }
}
