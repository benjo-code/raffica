using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Core.Forms;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Forms;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Forms;

public sealed class FormKeyboardTests
{
    private static readonly (int Page, string Name)[] TabOrder =
    [
        (0, "Name"), (0, "Bemerkung"), (0, "Kamm"), (0, "Kurz"), (0, "Pflicht"), (0, "Passwort"), (0, "Agb"), (0, "Zustimmung"),
        (0, "Zahlung"), (0, "Zahlung"), (0, "Stufe"), (0, "Stufe"), (0, "Farbe"), (0, "Frei"), (0, "Liste"), (0, "Mehrfach"),
        (1, "Zahlung"), (1, "Name"), (1, "Telefon"), (1, "Ort"),
    ];

    /// <summary>One press of Tab (or Shift+Tab) on the page view; waits for the move when the key took it, since a page may have to be read first.</summary>
    /// <summary>
    /// What the form keys decided about a key press: whether <see cref="FormKeys"/> took it. Not <c>Handled</c> of the finished event, because the
    /// window moves its keyboard focus on for a Tab the form leaves alone and then reports it handled.
    /// </summary>
    private static bool Decide(FormKeys keys, Control view, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        FormEditFixture.Press(view, key, modifiers);
        return keys.TookLastKey;
    }

    private static async Task<bool> TabAsync(FormEditFixture f, bool shift = false)
    {
        var before = f.Parts.Focus.Current;
        var handled = Decide(f.Parts.Keys, f.View, Key.Tab, shift ? KeyModifiers.Shift : KeyModifiers.None);
        if (handled)
        {
            await ViewFixture.WaitAsync(() => f.Parts.Focus.Current != before);
        }

        return handled;
    }

    private static (int Page, string Name, int Widget) Where(FormEditFixture f) =>
        f.Parts.Focus.Current is { } key ? (key.Page, key.FieldName, key.WidgetNumber) : (-1, string.Empty, -1);

    // The widget of a field on a page that is the n-th of its name there (a radio group has several).
    private static (int Page, string Name, int Widget) Expected(int index) =>
        (TabOrder[index].Page, TabOrder[index].Name, TabOrder.Take(index).Count(t => t == TabOrder[index]));

    private static void Release(FormEditFixture f, Key key)
    {
        f.View.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, Source = f.View });
        ViewFixture.Pump();
    }

    private static FormField WidgetAt(FormEditFixture f, int page, string name, int index) =>
        f.Forms.FieldsOn(page).Where(w => w.Key.FieldName == name).ElementAt(index);

    private static void RunCommand(FormEditFixture f, string id) =>
        f.Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id).Run!.Invoke();

    [AvaloniaFact]
    public async Task TabWalksTheFieldsInAnnotsOrderAndPassesAtTheEnds()
    {
        using var f = await FormEditFixture.OpenAsync();
        f.View.Focus();

        var visited = new List<(int Page, string Name, int Widget)>();
        for (var i = 0; i < TabOrder.Length; i++)
        {
            Assert.True(await TabAsync(f), "Tab " + i + " was not taken");
            visited.Add(Where(f));
        }

        // The locked field is skipped, the second page was read on the way (never skipped), and the order is the one of the page.
        Assert.Equal(Enumerable.Range(0, TabOrder.Length).Select(Expected), visited);

        // The end is a true end: the key passes on (the window moves its keyboard focus on, so there is no trap) and the form focus is cleared,
        // so the next Tab from the page view enters the form again.
        Assert.False(Decide(f.Parts.Keys, f.View, Key.Tab));
        Assert.Null(f.Parts.Focus.Current);
    }

    [AvaloniaFact]
    public async Task TabReadsAnUnreadPageFirstAndThenMovesOnToItsFirstField()
    {
        using var f = await FormEditFixture.OpenAsync();

        // A session that has read page 0 only, with "Mehrfach" as its last field there: the next field lies on a page that is not read yet.
        using var forms = new FormSession(f.Tab, SynchronizationContext.Current!)
        {
            PageReader = async (doc, page, token) =>
            {
                var fields = await doc.GetFormFieldsAsync(page, PdfPriority.Background, token);
                return page == 0 ? [.. fields.Where(w => w.Key.FieldName == "Mehrfach")] : fields;
            },
        };
        await forms.LoadPageAsync(0);
        Assert.False(forms.IsLoaded(1));
        f.Parts.Keys.Dispose();
        f.Interaction.Dispose();
        var focus = new FormFocus();
        using var interaction = new FormInteraction(f.Tab, f.View, f.Window.Tools, forms, focus, f.Window.Dialogs, () => true);
        using var keys = new FormKeys(f.View, f.Window.Tools, forms, focus, interaction, f.Window.Dialogs, () => true);
        focus.Set(forms.FieldsOn(0).Single().Key);
        f.View.Focus();

        // The key is taken at once (nothing waits for the page) and the move follows when the page is read.
        Assert.True(Decide(keys, f.View, Key.Tab));
        await ViewFixture.WaitAsync(() => focus.Current is { Page: 1 });
        Assert.True(forms.IsLoaded(1));
        Assert.Equal(FormNavigation.Next(forms.FieldsOn(1), null, forward: true), focus.Current);
    }

    // A session that has read page 0 only, with "Mehrfach" as its last field there; page 1 is read by the given reader. The parts of the window
    // are replaced by new ones over this session (as in the test above).
    private static async Task<(FormSession Forms, FormFocus Focus, FormInteraction Interaction, FormKeys Keys)> SessionWithUnreadPageAsync(
        FormEditFixture f, Func<PdfDocument, int, CancellationToken, Task<IReadOnlyList<FormField>>> pageOne)
    {
        var forms = new FormSession(f.Tab, SynchronizationContext.Current!)
        {
            PageReader = async (doc, page, token) =>
            {
                if (page == 1)
                {
                    return await pageOne(doc, page, token);
                }

                var fields = await doc.GetFormFieldsAsync(page, PdfPriority.Background, token);
                return [.. fields.Where(w => w.Key.FieldName == "Mehrfach")];
            },
        };
        await forms.LoadPageAsync(0);
        f.Parts.Keys.Dispose();
        f.Interaction.Dispose();
        var focus = new FormFocus();
        var interaction = new FormInteraction(f.Tab, f.View, f.Window.Tools, forms, focus, f.Window.Dialogs, () => true);
        var keys = new FormKeys(f.View, f.Window.Tools, forms, focus, interaction, f.Window.Dialogs, () => true);
        focus.Set(forms.FieldsOn(0).Single().Key);
        f.View.Focus();
        return (forms, focus, interaction, keys);
    }

    [AvaloniaFact]
    public async Task APageThatCannotBeReadNeverTrapsTheKey()
    {
        using var f = await FormEditFixture.OpenAsync();
        var reads = 0;
        var (forms, focus, interaction, keys) = await SessionWithUnreadPageAsync(f, (_, _, _) =>
        {
            Interlocked.Increment(ref reads);
            throw new InvalidDataException("damaged page");
        });
        using (forms)
        using (interaction)
        using (keys)
        {
            var mehrfach = focus.Current;

            // The first Tab is taken (the page has to be read), the read fails and the page counts as having no widgets.
            Assert.True(Decide(keys, f.View, Key.Tab));
            await ViewFixture.WaitAsync(() => forms.IsFailed(1));

            // The walk of that Tab ends on its own once the read is over (it finds nothing to move to); a later Tab must not meet it still
            // running, or its step could move the focus right after a Tab passed on.
            await ViewFixture.WaitAsync(() => keys.PendingMove.IsCompleted);
            Assert.False(forms.IsLoaded(1));
            Assert.Equal(mehrfach, focus.Current);

            // The next Tab passes on (the page is walked over, the form focus is let go); the one after enters the form again, and the one after
            // that passes again: nothing swallows the key for the same page twice, and the page is not read again.
            for (var i = 0; i < 3; i++)
            {
                Assert.False(Decide(keys, f.View, Key.Tab));
                Assert.Null(focus.Current);
                Assert.True(Decide(keys, f.View, Key.Tab));
                Assert.Equal(mehrfach, focus.Current);
            }

            Assert.Equal(1, reads);
            Assert.Null(focus.PageToLoad(forms, forward: true));

            // With no focus the walk over the failed page goes the other way and the end passes the key on as well.
            focus.Set(null);
            Assert.True(Decide(keys, f.View, Key.Tab, KeyModifiers.Shift));
            Assert.Equal(mehrfach, focus.Current);
            Assert.False(Decide(keys, f.View, Key.Tab));
        }
    }

    [AvaloniaFact]
    public async Task APageThatFailedIsReadAgainAfterItWasInvalidated()
    {
        using var f = await FormEditFixture.OpenAsync();
        var fail = true;
        var (forms, _, interaction, keys) = await SessionWithUnreadPageAsync(f, async (doc, page, token) =>
            fail ? throw new InvalidDataException("damaged page") : await doc.GetFormFieldsAsync(page, PdfPriority.Background, token));
        using (forms)
        using (interaction)
        using (keys)
        {
            await forms.LoadPageAsync(1);
            Assert.True(forms.IsFailed(1));

            // The page was invalidated (an undo, a save): the next read is made and clears the mark.
            fail = false;
            await forms.RefreshPagesAsync([1]);
            Assert.True(forms.IsLoaded(1));
            Assert.False(forms.IsFailed(1));
        }
    }

    [AvaloniaFact]
    public async Task AMoveThatEndsAfterTheTabWasClosedDoesNotTouchTheFocus()
    {
        using var f = await FormEditFixture.OpenAsync();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (forms, focus, interaction, keys) = await SessionWithUnreadPageAsync(f, async (doc, page, token) =>
        {
            await release.Task;
            return await doc.GetFormFieldsAsync(page, PdfPriority.Background, token);
        });
        var before = focus.Current;
        using (forms)
        {
            // The key is taken and the read of page 1 is pending.
            Assert.True(Decide(keys, f.View, Key.Tab));

            // The tab closes (the window disposes its parts) before the read ends.
            keys.Dispose();
            interaction.Dispose();
            release.SetResult();
            await ViewFixture.WaitAsync(() => forms.IsLoaded(1));
            await Task.Delay(100);
            ViewFixture.Pump();

            Assert.Equal(before, focus.Current);
            Assert.Equal(FormStep.End, interaction.Mover.Step(forward: true).Step);
        }
    }

    [AvaloniaFact]
    public async Task TheKeysAreDeadWhileFillingIsForbidden()
    {
        using var f = await FormEditFixture.OpenAsync();
        f.Parts.Keys.Dispose();
        using var keys = new FormKeys(f.View, f.Window.Tools, f.Forms, f.Parts.Focus, f.Interaction, f.Window.Dialogs, () => false);
        f.View.Focus();

        // Tab passes on and does not enter the form.
        Assert.False(Decide(keys, f.View, Key.Tab));
        Assert.Null(f.Parts.Focus.Current);

        // With a widget focused, Enter, Space and Esc are not taken either and nothing is toggled or cleared.
        f.Parts.Focus.Set(f.Field("Agb").Key);
        Assert.False(Decide(keys, f.View, Key.Space));
        Assert.False(Decide(keys, f.View, Key.Enter));
        Assert.False(Decide(keys, f.View, Key.Escape));
        await f.SettleAsync();
        Assert.Equal(f.Field("Agb").Key, f.Parts.Focus.Current);
        Assert.False((await f.FromDocumentAsync("Agb")).State.Checked);
        Assert.False(f.Tab.IsDirty);
        Assert.False(f.Interaction.IsEditing);
    }

    [AvaloniaFact]
    public async Task ShiftTabWalksBack()
    {
        using var f = await FormEditFixture.OpenAsync();
        f.View.Focus();

        // The first Shift+Tab enters the last field of the visible page.
        Assert.True(await TabAsync(f, shift: true));
        Assert.Equal((0, "Mehrfach", 0), Where(f));
        Assert.True(await TabAsync(f, shift: true));
        Assert.Equal((0, "Liste", 0), Where(f));

        // Back over a page border (page 1 is read first).
        await f.Forms.LoadPageAsync(1);
        f.Parts.Focus.Set(WidgetAt(f, 1, "Zahlung", 0).Key);
        Assert.True(await TabAsync(f, shift: true));
        Assert.Equal((0, "Mehrfach", 0), Where(f));

        // The start passes the key on.
        f.Parts.Focus.Set(WidgetAt(f, 0, "Name", 0).Key);
        Assert.False(Decide(f.Parts.Keys, f.View, Key.Tab, KeyModifiers.Shift));
        Assert.Null(f.Parts.Focus.Current);
    }

    [AvaloniaFact]
    public async Task EnterAndSpaceActOnTheFocusedField()
    {
        using var f = await FormEditFixture.OpenAsync();
        f.View.Focus();

        f.Parts.Focus.Set(f.Field("Agb").Key);
        Assert.True(FormEditFixture.Press(f.View, Key.Space));
        await f.SettleAsync();
        Assert.True((await f.FromDocumentAsync("Agb")).State.Checked);
        Release(f, Key.Space);

        // A held key is one press: the repeat is swallowed and does not toggle again.
        Assert.True(FormEditFixture.Press(f.View, Key.Enter));
        Assert.True(FormEditFixture.Press(f.View, Key.Enter));
        await f.SettleAsync();
        Assert.False((await f.FromDocumentAsync("Agb")).State.Checked);
        Release(f, Key.Enter);

        // A radio button is chosen.
        f.Parts.Focus.Set(WidgetAt(f, 0, "Zahlung", 1).Key);
        Assert.True(FormEditFixture.Press(f.View, Key.Space));
        await f.SettleAsync();
        Assert.Equal("Karte", (await f.FromDocumentAsync("Zahlung")).State.Text);
        Release(f, Key.Space);

        // A text field opens its editor; Esc drops it and keeps the form focus.
        f.Parts.Focus.Set(f.Field("Name").Key);
        Assert.True(FormEditFixture.Press(f.View, Key.Enter));
        Assert.True(f.Interaction.IsEditing);
        Assert.Equal("Name", f.Interaction.EditedField!.Key.FieldName);
        Release(f, Key.Enter);
        Assert.True(FormEditFixture.Press(f.Interaction.Editor!.Input, Key.Escape));
        Assert.False(f.Interaction.IsEditing);
        Assert.Equal((0, "Name", 0), Where(f));
    }

    [AvaloniaFact]
    public async Task EscClearsTheFormFocusOnlyWhenNoEditorIsOpen()
    {
        using var f = await FormEditFixture.OpenAsync();
        f.View.Focus();

        // Nothing focused: Esc is not ours and nothing happens.
        FormEditFixture.Press(f.View, Key.Escape);
        Assert.Null(f.Parts.Focus.Current);

        f.Parts.Focus.Set(f.Field("Name").Key);
        f.Interaction.Activate(f.Field("Name"));
        ViewFixture.Pump();
        Assert.True(f.Interaction.IsEditing);

        // The editor takes Esc first; the focus is still there.
        FormEditFixture.Press(f.Interaction.Editor!.Input, Key.Escape);
        Assert.False(f.Interaction.IsEditing);
        Assert.Equal((0, "Name", 0), Where(f));

        f.View.Focus();
        Assert.True(FormEditFixture.Press(f.View, Key.Escape));
        Assert.Null(f.Parts.Focus.Current);
    }

    [AvaloniaFact]
    public async Task TabInsideAnEditorWritesTheTextAndMovesOn()
    {
        using var f = await FormEditFixture.OpenAsync();
        var box = (TextBox)f.Open(f.Field("Name"));
        FormEditFixture.SetText(box, "Per Tab");

        Assert.True(FormEditFixture.Press(box, Key.Tab));
        await f.SettleAsync();
        await ViewFixture.WaitAsync(() => Where(f) == (0, "Bemerkung", 0));
        Assert.False(f.Interaction.IsEditing);
        Assert.Equal("Per Tab", (await f.FromDocumentAsync("Name")).State.Text);

        // Shift+Tab inside an editor goes back.
        var again = (TextBox)f.Open(f.Field("Kurz"));
        Assert.True(FormEditFixture.Press(again, Key.Tab, KeyModifiers.Shift));
        await f.SettleAsync();
        await ViewFixture.WaitAsync(() => Where(f) == (0, "Kamm", 0));
    }

    [AvaloniaFact]
    public async Task CompleteSampleFormFilledWithTheKeyboardOnly()
    {
        using var f = await FormEditFixture.OpenAsync();
        f.View.Focus();

        // One walk over the whole form, acting on the kinds with Enter and Space. Every text goes in through its editor and Enter (or Ctrl+Enter).
        var typed = new Dictionary<(int, string, int), string>
        {
            [(0, "Name", 0)] = "Anna Muster",
            [(0, "Pflicht", 0)] = "ausgefuellt",
            [(1, "Telefon", 0)] = "0123 456",
        };
        for (var i = 0; i < TabOrder.Length; i++)
        {
            // Enter in a single-line editor writes the text and moves the focus on, so the walk is already there after it.
            if (Where(f) != Expected(i))
            {
                Assert.True(await TabAsync(f), "Tab " + i);
            }

            var here = Where(f);
            Assert.Equal(Expected(i), here);
            if (typed.TryGetValue(here, out var text))
            {
                Assert.True(FormEditFixture.Press(f.View, Key.Enter));
                var box = Assert.IsType<TextBox>(f.Interaction.Editor!.Input);
                FormEditFixture.SetText(box, text);
                Release(f, Key.Enter);
                Assert.True(FormEditFixture.Press(box, Key.Enter));
                Release(f, Key.Enter);
                var next = Expected(i + 1);
                await ViewFixture.WaitAsync(() => Where(f) == next);
            }
            else if (here is (0, "Bemerkung", 0))
            {
                Assert.True(FormEditFixture.Press(f.View, Key.Enter));
                var box = Assert.IsType<TextBox>(f.Interaction.Editor!.Input);
                FormEditFixture.SetText(box, "Zeile eins");
                Release(f, Key.Enter);
                Assert.True(FormEditFixture.Press(box, Key.Enter, KeyModifiers.Control));
                Release(f, Key.Enter);
            }
            else if (here is (0, "Agb", 0) or (0, "Zustimmung", 0) || f.Parts.Focus.Current == WidgetAt(f, 0, "Zahlung", 1).Key)
            {
                Assert.True(FormEditFixture.Press(f.View, Key.Space));
                Release(f, Key.Space);
            }
            else if (here is (0, "Farbe", 0))
            {
                Assert.True(FormEditFixture.Press(f.View, Key.Enter));
                Release(f, Key.Enter);
                Assert.IsType<ComboBox>(f.Interaction.Editor!.Input).SelectedIndex = 1;
                ViewFixture.Pump();
                f.View.Focus();
            }
            else if (here is (0, "Liste", 0))
            {
                Assert.True(FormEditFixture.Press(f.View, Key.Enter));
                Release(f, Key.Enter);
                Assert.IsType<ListBox>(f.Interaction.Editor!.Input).Selection.Select(2);
                ViewFixture.Pump();
                f.View.Focus();
            }

            await f.SettleAsync();
        }

        Assert.True(f.Tab.IsDirty);
        f.PressKey(PhysicalKey.S, RawInputModifiers.Control);
        await ViewFixture.WaitAsync(() => !f.Tab.IsDirty && !f.Window.Saves.IsSaving);
        await f.ReopenAsync();
        await f.Forms.LoadPageAsync(1);

        Assert.Equal("Anna Muster", f.Field("Name").State.Text);
        Assert.Equal("Zeile eins", f.Field("Bemerkung").State.Text);
        Assert.Equal("ausgefuellt", f.Field("Pflicht").State.Text);
        Assert.True(f.Field("Agb").State.Checked);
        Assert.True(f.Field("Zustimmung").State.Checked);
        Assert.Equal("Karte", f.Field("Zahlung").State.Text);
        Assert.Equal([1], f.Field("Farbe").State.SelectedOptions);
        Assert.Equal([2], f.Field("Liste").State.SelectedOptions);
        Assert.Equal("0123 456", f.Field("Telefon", page: 1).State.Text);
    }

    [AvaloniaFact]
    public async Task NextRequiredScrollsTheFieldIntoView()
    {
        using var f = await FormEditFixture.OpenAsync();
        f.View.SetZoom(4);
        f.View.GoToPage(1);
        ViewFixture.Pump();
        await ViewFixture.WaitAsync(() => f.View.CurrentPage == 1);
        var required = f.Field("Pflicht");
        Assert.True(f.View.ToView(0, required.Bounds) is not { } before || before.Bottom < 0 || before.Top > f.View.Bounds.Height);

        RunCommand(f, PdfCommandIds.FormsNextRequired);
        await ViewFixture.WaitAsync(() => f.Parts.Focus.Current == required.Key);
        ViewFixture.Pump();

        var shown = f.View.ToView(0, required.Bounds);
        Assert.NotNull(shown);
        Assert.True(shown.Value.Top >= 0 && shown.Value.Bottom <= f.View.Bounds.Height, "the field is not in the view: " + shown);
    }

    [AvaloniaFact]
    public async Task NextAndPreviousCommandsMoveTheFocusAndHighlightToggles()
    {
        using var f = await FormEditFixture.OpenAsync();
        RunCommand(f, PdfCommandIds.FormsNext);
        await ViewFixture.WaitAsync(() => Where(f) == (0, "Name", 0));
        RunCommand(f, PdfCommandIds.FormsNext);
        await ViewFixture.WaitAsync(() => Where(f) == (0, "Bemerkung", 0));
        RunCommand(f, PdfCommandIds.FormsPrevious);
        await ViewFixture.WaitAsync(() => Where(f) == (0, "Name", 0));

        var before = f.Forms.Highlight;
        RunCommand(f, PdfCommandIds.FormsHighlight);
        Assert.NotEqual(before, f.Forms.Highlight);
        RunCommand(f, PdfCommandIds.FormsHighlight);
        Assert.Equal(before, f.Forms.Highlight);
    }

    [Fact]
    public void CommandsAreDisabledWithAReasonWithoutAFormOrWhenFillingIsForbidden()
    {
        string[] ids = [PdfCommandIds.FormsNext, PdfCommandIds.FormsPrevious, PdfCommandIds.FormsNextRequired, PdfCommandIds.FormsHighlight];
        var hasForm = false;
        bool? fill = true;
        var required = true;
        var rules = new PdfCommandRules(() => true, () => 1, () => null, hasForm: () => hasForm, fillAllowed: () => fill, hasEmptyRequired: () => required);

        foreach (var id in ids)
        {
            Assert.False(rules.Allows(id), id);
            Assert.Equal(PdfStrings.FormsNoFormReason, rules.ReasonFor(id));
        }

        hasForm = true;
        foreach (var id in ids)
        {
            Assert.True(rules.Allows(id), id);
            Assert.Null(rules.ReasonFor(id));
        }

        // Filling is forbidden by the document: every form command says so.
        fill = false;
        foreach (var id in ids)
        {
            Assert.False(rules.Allows(id), id);
            Assert.Equal(PdfStrings.FormFillForbidden, rules.ReasonFor(id));
        }

        // Not read yet: nothing is refused.
        fill = null;
        Assert.All(ids, id => Assert.True(rules.Allows(id), id));

        // Next required needs a required field that is empty.
        required = false;
        Assert.False(rules.Allows(PdfCommandIds.FormsNextRequired));
        Assert.True(rules.Allows(PdfCommandIds.FormsNext));

        // Without a document there is nothing to tell.
        var none = new PdfCommandRules(() => false, () => 0, () => null);
        Assert.All(ids, id => Assert.False(none.Allows(id)));
        Assert.All(ids, id => Assert.Null(none.ReasonFor(id)));
    }

    [AvaloniaFact]
    public async Task TheRibbonCommandsFollowTheOpenDocument()
    {
        using var windows = WindowFixture.Create();
        await windows.OpenAsync("text-rich.pdf");
        var commands = windows.Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Where(c => c.Id.StartsWith("forms.", StringComparison.Ordinal)).ToList();
        Assert.Equal(4, commands.Count);
        Assert.All(commands, c =>
        {
            Assert.False(c.IsEnabled);
            Assert.Equal(PdfStrings.FormsNoFormReason, c.CurrentDisabledReason);
        });

        await windows.OpenAsync("forms-basic.pdf");
        await ViewFixture.WaitAsync(() => commands.All(c => c.IsEnabled || c.Id == PdfCommandIds.FormsNextRequired));
    }

    [AvaloniaFact]
    public async Task TabKeyIsNotTakenWhileADialogIsOpen()
    {
        using var f = await FormEditFixture.OpenAsync();
        f.View.Focus();
        Action<bool>? close = null;
        var shown = f.Window.Dialogs.ShowAsync<bool>(done =>
        {
            close = done;
            return NoticeCard.Create("Title", "Message", () => done(true));
        });
        ViewFixture.Pump();
        Assert.True(f.Window.Host.IsOpen);

        Assert.False(Decide(f.Parts.Keys, f.View, Key.Tab));
        Assert.Null(f.Parts.Focus.Current);

        close!(true);
        await shown;
        ViewFixture.Pump();
        Assert.True(await TabAsync(f));
        Assert.Equal((0, "Name", 0), Where(f));
    }

    [AvaloniaFact]
    public async Task TheKeysAreNotTakenWhileAnAnnotateToolIsActive()
    {
        using var f = await FormEditFixture.OpenAsync();
        f.View.Focus();
        f.Window.Tools.Choose(Raffica.Pdf.Annotating.AnnotateTool.Select);
        ViewFixture.Pump();
        Assert.False(Decide(f.Parts.Keys, f.View, Key.Tab));
        Assert.Null(f.Parts.Focus.Current);
        f.Window.Tools.End();
    }

    [AvaloniaFact]
    public async Task FocusedFieldHasAnAutomationNameFromTheFieldsAlternateName()
    {
        const string Alternate = "Allgemeine Geschaeftsbedingungen";
        using var f = await FormEditFixture.OpenAsync(prepare: path =>
        {
            var latin1 = Encoding.Latin1;
            var text = latin1.GetString(File.ReadAllBytes(path));
            Assert.Contains("/T (Agb)", text, StringComparison.Ordinal);
            File.WriteAllBytes(path, latin1.GetBytes(text.Replace("/T (Agb)", "/TU (" + Alternate + ") /T (Agb)", StringComparison.Ordinal)));
        });
        var agb = f.Field("Agb");
        Assert.Equal(Alternate, agb.AlternateName);
        var pageName = AutomationProperties.GetName(f.View);
        Assert.False(string.IsNullOrEmpty(pageName));

        f.Parts.Focus.Set(agb.Key);
        Assert.Equal(Alternate + ", " + PdfStrings.FormStateNotChecked, AutomationProperties.GetName(f.View));

        // A field without an alternate name is named by its field name; no focus gives the view its own name back.
        f.Parts.Focus.Set(f.Field("Name").Key);
        Assert.Equal("Name", AutomationProperties.GetName(f.View));
        f.Parts.Focus.Set(null);
        Assert.Equal(pageName, AutomationProperties.GetName(f.View));
    }
}
