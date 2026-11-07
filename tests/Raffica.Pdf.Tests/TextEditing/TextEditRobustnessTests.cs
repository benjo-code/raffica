using System.Globalization;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Core.TextEdit;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.TextEditing;
using Xunit;

namespace Raffica.Pdf.Tests.TextEditing;

/// <summary>Held keys, a commit in flight, odd characters, a moving view and the ways out of the mode.</summary>
public sealed class TextEditRobustnessTests
{
    private static void KeyUp(Avalonia.Interactivity.Interactive source, Key key)
    {
        source.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, Source = source });
        ViewFixture.Pump();
    }

    private static bool KeyDown(Avalonia.Interactivity.Interactive source, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers, Source = source };
        source.RaiseEvent(e);
        ViewFixture.Pump();
        return e.Handled;
    }

    private static async Task<RunFieldEditor> OpenAsync(TextEditRig rig, string line)
    {
        var run = await rig.RunAsync(line);
        var field = rig.OpenField(run);
        await ViewFixture.WaitAsync(() => rig.FieldHasFocus);
        return field;
    }

    [AvaloniaFact]
    public async Task AHeldEnterOpensOnceAndWritesOnce()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var run = await rig.RunAsync("Quarterly report 2026");
        rig.View.Focus();
        rig.Session.Focus(run);

        // Held on the page: the first press opens the field, the repeats are used up (they neither reopen nor reach the field that took the focus).
        Assert.True(KeyDown(rig.View, Key.Enter));
        await ViewFixture.WaitAsync(() => rig.Interaction.Field is not null && rig.FieldHasFocus);
        var field = rig.Interaction.Field!;
        TextEditRig.Type(field, "Quarterly report 2027");
        Assert.True(KeyDown(field.Field, Key.Enter), "a repeat of the opening press is used up");
        await Task.Delay(100, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Assert.True(rig.Session.HasDraft, "the repeat did not write the line");
        KeyUp(field.Field, Key.Enter);

        // Held in the field: one write, however long the key is held; the closed field is not opened again by the repeats reaching the page.
        Assert.True(KeyDown(field.Field, Key.Enter));
        var completion = rig.Interaction.Completion;
        for (var i = 0; i < 5; i++)
        {
            KeyDown(rig.Interaction.Field?.Field ?? (Avalonia.Interactivity.Interactive)rig.View, Key.Enter);
        }

        await completion;
        await rig.SettleAsync();
        KeyUp(rig.View, Key.Enter);
        Assert.False(rig.Session.HasDraft);
        Assert.Null(rig.Interaction.Field);
        Assert.Contains("Quarterly report 2027", await rig.PageTextAsync(), StringComparison.Ordinal);

        // One step: a single undo brings the old line back.
        await rig.Tab.Annotations.UndoAsync();
        await rig.SettleAsync();
        Assert.Contains("Quarterly report 2026", await rig.PageTextAsync(), StringComparison.Ordinal);
        Assert.False(rig.Tab.Annotations.CanUndo);
    }

    [AvaloniaFact]
    public async Task TheFieldOfARotatedPageHasTheSizeAndPlaceOfItsOutline()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-rotated.pdf");
        await rig.EnterModeAsync();
        var scale = rig.View.PageBounds(0)!.Value.Width / rig.View.SizeOfPage(0)!.Value.Width;
        foreach (var run in rig.Session.RunsOf(0))
        {
            rig.Session.Discard();
            Assert.True(rig.Session.OpenDraft(run));
            ViewFixture.Pump();
            var outline = RunOutlines.RectOf(rig.View, run)!.Value;
            var place = rig.View.FieldRect;
            Assert.True(place.Contains(outline.Center), $"{run.Text}: field {place} does not cover the outline {outline}");
            Assert.True(place.Width >= outline.Width && place.Height >= outline.Height - 0.5, $"{run.Text}: field {place} smaller than {outline}");
            Assert.True(Math.Abs(place.Left - (outline.Left - 2)) < 1.5, $"{run.Text}: left {place.Left} vs {outline.Left}");
            var font = rig.Interaction.Field!.Field.FontSize;
            Assert.True(font > 0 && font <= outline.Height * 4 + 0.5 && font >= outline.Height / 4 - 0.5, $"{run.Text}: font {font}");
            if (run.FontSize * scale >= outline.Height / 4 && run.FontSize * scale <= outline.Height * 4)
            {
                Assert.Equal(run.FontSize * scale, font, 1);
            }
        }
    }

    [AvaloniaFact]
    public async Task TypingDuringACommitIsKeptAndTheFieldIsReadOnlyMeanwhile()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var field = await OpenAsync(rig, "Quarterly report 2026");
        TextEditRig.Type(field, "Quarterly report 2027");
        var hold = new TaskCompletionSource();
        rig.Session.BeforeWrite = () => hold.Task;

        var commit = rig.Session.CommitAsync();
        ViewFixture.Pump();
        Assert.True(rig.Session.IsCommitting);
        Assert.True(field.Field.IsReadOnly, "no key can change the text while the write waits");

        // A text that gets in anyway (a paste that raced the read-only flag) is not lost: the line is written as it was committed, the new text stays in the field.
        field.Field.Text = "Quarterly report 2028";
        ViewFixture.Pump();
        rig.Session.BeforeWrite = null;
        hold.SetResult();
        var result = await commit;
        await rig.SettleAsync();
        Assert.Equal(TextEditCommitKind.Applied, result.Kind);
        Assert.Contains("Quarterly report 2027", await rig.PageTextAsync(), StringComparison.Ordinal);
        Assert.True(rig.Session.HasDraft);
        Assert.Equal("Quarterly report 2028", rig.Session.DraftText);
        Assert.Equal("Quarterly report 2027", rig.Session.DraftRun!.Text.TrimEnd());
        Assert.True(rig.Tab.Annotations.HasUnresolved);
        Assert.False(field.Field.IsReadOnly);

        // The second text is written on top of the first: two steps, the newest text on the page.
        Assert.Equal(TextEditCommitKind.Applied, (await rig.Session.CommitAsync()).Kind);
        await rig.SettleAsync();
        Assert.Contains("Quarterly report 2028", await rig.PageTextAsync(), StringComparison.Ordinal);
        Assert.False(rig.Session.HasDraft);
    }

    [AvaloniaFact]
    public async Task DiscardOrEscWhileTheWriteWaitsPreventsTheWrite()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var field = await OpenAsync(rig, "Quarterly report 2026");
        TextEditRig.Type(field, "Quarterly report 2027");
        var hold = new TaskCompletionSource();
        rig.Session.BeforeWrite = () => hold.Task;
        var commit = rig.Session.CommitAsync();
        ViewFixture.Pump();

        // Esc in the field (the real key) drops the draft while the write waits.
        rig.Press(PhysicalKey.Escape);
        Assert.False(rig.Session.HasDraft);
        rig.Session.BeforeWrite = null;
        hold.SetResult();
        var result = await commit;
        await rig.SettleAsync();
        Assert.Equal(TextEditCommitKind.NoDraft, result.Kind);
        Assert.Contains("Quarterly report 2026", await rig.PageTextAsync(), StringComparison.Ordinal);
        Assert.False(rig.Tab.Annotations.Editor.IsDirty);
        Assert.False(rig.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task LeavingTheModeWhileACommitRunsWaitsForItAndLosesNothing()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var field = await OpenAsync(rig, "Quarterly report 2026");
        TextEditRig.Type(field, "Quarterly report 2027");
        var hold = new TaskCompletionSource();
        rig.Session.BeforeWrite = () => hold.Task;
        var commit = rig.Session.CommitAsync();
        var leaving = rig.Window.FinishTextEditAsync();
        ViewFixture.Pump();
        Assert.False(leaving.IsCompleted);
        Assert.True(rig.Session.IsActive);

        rig.Session.BeforeWrite = null;
        hold.SetResult();
        await commit;
        await leaving;
        await rig.SettleAsync();
        Assert.False(rig.Session.IsActive);
        Assert.Null(rig.Window.TextEditOf(rig.Tab));
        Assert.Contains("Quarterly report 2027", await rig.PageTextAsync(), StringComparison.Ordinal);
        Assert.True(rig.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task DoneWritesAValidDraftFirstAndKeepsTheModeForARefusedOne()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();

        // A refused draft: the mode stays on with the field and the reason.
        var field = await OpenAsync(rig, "Quarterly report 2026");
        TextEditRig.Type(field, "Quarterly \u2192 2026");
        rig.Command("text.editDone").Run!();
        await Task.Delay(300, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Assert.True(rig.Session.IsActive);
        Assert.True(rig.Session.HasDraft);
        Assert.Contains("\u2192", rig.Session.Note, StringComparison.Ordinal);
        Assert.Contains("Quarterly report 2026", await rig.PageTextAsync(), StringComparison.Ordinal);

        // A valid draft: written first, then the mode ends.
        TextEditRig.Type(field, "Quarterly report 2027");
        rig.Command("text.editDone").Run!();
        await ViewFixture.WaitAsync(() => !rig.Session.IsActive);
        await rig.SettleAsync();
        Assert.Contains("Quarterly report 2027", await rig.PageTextAsync(), StringComparison.Ordinal);
        Assert.False(rig.Session.HasDraft);
        Assert.Null(rig.Window.TextEditOf(rig.Tab));
    }

    [AvaloniaFact]
    public async Task ZoomAndScrollWithTheFieldOpenKeepTheTextAndTheFieldFollows()
    {
        using var rig = await TextEditRig.OpenAsync("text-dense.pdf");
        await rig.EnterModeAsync();
        var run = rig.Session.RunsOf(0).OrderBy(r => r.Bounds.Top).First();
        rig.Session.Focus(run);
        var field = rig.OpenField(run);
        TextEditRig.Type(field, "typed on page one");

        // Zoom: the field is laid out again over the outline, the text is still there.
        rig.View.ZoomIn();
        ViewFixture.Pump();
        await Task.Delay(300, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        var outline = RunOutlines.RectOf(rig.View, run)!.Value;
        Assert.True(rig.View.FieldRect.Width >= outline.Width);
        Assert.True(Math.Abs(rig.View.FieldRect.Left - (outline.Left - 2)) < 2);
        Assert.Equal("typed on page one", field.Field.Text);

        // Scroll far away and back: the draft and its text stay (the field hides while its page is out of view and comes back).
        rig.View.GoToPage(60);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Assert.True(rig.Session.HasDraft);
        Assert.Equal("typed on page one", rig.Session.DraftText);
        Assert.Equal("typed on page one", field.Field.Text);
        rig.View.GoToPage(0);
        await Task.Delay(300, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Assert.True(rig.Session.HasDraft);
        Assert.Equal("typed on page one", field.Field.Text);
        Assert.True(field.IsVisible);
        Assert.True(rig.Tab.Annotations.HasUnresolved, "the tab still counts the draft as unsaved");
    }

    [AvaloniaFact]
    public async Task EmptyTextThroughEnterInTheRealWindowRemovesTheLineAndUndoBringsItBack()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var field = await OpenAsync(rig, "Quarterly report 2026");
        TextEditRig.Type(field, string.Empty);
        await Task.Delay(200, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Assert.Equal(PdfStrings.TextEditNoteEmpty, rig.Session.Note);

        rig.Press(PhysicalKey.Enter);
        await rig.Interaction.Completion;
        await rig.SettleAsync();
        Assert.DoesNotContain("Quarterly", await rig.PageTextAsync(), StringComparison.Ordinal);
        Assert.Null(rig.Interaction.Field);

        await rig.Tab.Annotations.UndoAsync();
        await rig.SettleAsync();
        Assert.Contains("Quarterly report 2026", await rig.PageTextAsync(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ASecondEditOfTheSameLineAfterTheReReadWorksAndUndoesStepByStep()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var field = await OpenAsync(rig, "Quarterly report 2026");
        TextEditRig.Type(field, "Quarterly report 2027");
        rig.Press(PhysicalKey.Enter);
        await rig.Interaction.Completion;
        await rig.SettleAsync();
        await ViewFixture.WaitAsync(() => rig.Session.IsLoaded(0));

        field = await OpenAsync(rig, "Quarterly report 2027");
        TextEditRig.Type(field, "Quarterly report 2028");
        rig.Press(PhysicalKey.Enter);
        await rig.Interaction.Completion;
        await rig.SettleAsync();
        Assert.Contains("Quarterly report 2028", await rig.PageTextAsync(), StringComparison.Ordinal);

        await rig.Tab.Annotations.UndoAsync();
        await rig.SettleAsync();
        Assert.Contains("Quarterly report 2027", await rig.PageTextAsync(), StringComparison.Ordinal);
        await rig.Tab.Annotations.UndoAsync();
        await rig.SettleAsync();
        Assert.Contains("Quarterly report 2026", await rig.PageTextAsync(), StringComparison.Ordinal);
    }

    // The press captures the line before the write; when the write of the first line is through before the release (a slow runner, the focus loss
    // writes it at the press), the release must open the field on the line as it stands after the re-read, never on the index of the press.
    [AvaloniaFact]
    public async Task AReleaseAfterTheWriteIsThroughOpensTheFieldOnTheMovedLine()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-subset.pdf");
        await rig.EnterModeAsync();
        var first = await rig.RunAsync("Invoice total: 1,250 EUR");
        var second = await rig.RunAsync("Kontoauszug März");
        var field = rig.OpenField(first);
        await ViewFixture.WaitAsync(() => rig.FieldHasFocus);
        TextEditRig.Type(field, "Invoice total: 1,259 EUR");

        // The press takes the focus from the field, and that writes the first line. The write is held, so the order is fixed: the press has
        // captured the line, then the write goes through, then the release comes.
        var hold = new TaskCompletionSource();
        rig.Session.BeforeWrite = () => hold.Task;
        var at = rig.WindowPointOf(second);
        rig.Window.MouseDown(at, MouseButton.Left);
        await ViewFixture.WaitAsync(() => rig.Session.IsCommitting);
        rig.Session.BeforeWrite = null;
        hold.SetResult();
        await rig.Interaction.Completion;
        await rig.SettleAsync();
        Assert.False(rig.Session.HasDraft);
        Assert.Contains("Invoice total: 1,259 EUR", await rig.PageTextAsync(), StringComparison.Ordinal);
        rig.Window.MouseUp(at, MouseButton.Left);

        await ViewFixture.WaitAsync(() => rig.Session.HasDraft);
        await rig.SettleAsync();
        var moved = (await rig.DocumentRunsAsync()).Single(r => r.Text.TrimEnd() == "Kontoauszug März");
        Assert.NotEqual(second.ObjectIndex, moved.ObjectIndex);
        Assert.Equal(moved.ObjectIndex, rig.Session.DraftRun!.ObjectIndex);
    }

    // A quick click: the release comes while the write of the first line (started by the focus loss at the press) is still running. The field still opens
    // on the second line, found again after the write, and the first line is written once.
    [AvaloniaFact]
    public async Task AReleaseWhileTheWriteIsRunningOpensTheFieldOnTheMovedLineAfterIt()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-subset.pdf");
        await rig.EnterModeAsync();
        var first = await rig.RunAsync("Invoice total: 1,250 EUR");
        var second = await rig.RunAsync("Kontoauszug März");
        var field = rig.OpenField(first);
        await ViewFixture.WaitAsync(() => rig.FieldHasFocus);
        TextEditRig.Type(field, "Invoice total: 1,259 EUR");

        var hold = new TaskCompletionSource();
        rig.Session.BeforeWrite = () => hold.Task;
        var at = rig.WindowPointOf(second);
        rig.Window.MouseDown(at, MouseButton.Left);
        await ViewFixture.WaitAsync(() => rig.Session.IsCommitting);
        rig.Window.MouseUp(at, MouseButton.Left);
        ViewFixture.Pump();
        Assert.True(rig.Session.IsCommitting, "the write is still held");

        rig.Session.BeforeWrite = null;
        hold.SetResult();
        await ViewFixture.WaitAsync(() => !rig.Session.IsCommitting);
        await rig.SettleAsync();
        var moved = (await rig.DocumentRunsAsync()).Single(r => r.Text.TrimEnd() == "Kontoauszug März");
        Assert.NotEqual(second.ObjectIndex, moved.ObjectIndex);
        Assert.Contains("Invoice total: 1,259 EUR", await rig.PageTextAsync(), StringComparison.Ordinal);

        // The field opens on the second line as found after the write; it is not left on the first line and not lost.
        await ViewFixture.WaitAsync(() => rig.Interaction.Field is not null && rig.Session.DraftRun?.ObjectIndex == moved.ObjectIndex);
        Assert.Equal("Kontoauszug März", rig.Session.DraftRun!.Text.TrimEnd());
        Assert.False(rig.Session.IsCommitting);
    }

    // A read that was started before the runs were dropped again ends without storing anything. A caller that waited for it (the field that opens on the
    // line after a write) must wait for the read of the current generation, not go on with no lines and give up silently.
    [AvaloniaFact]
    public async Task ALoadThatOutlivedAnInvalidationWaitsForTheCurrentRead()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var gates = new[] { new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var reads = 0;
        rig.Session.ReadRuns = async (document, page, token) =>
        {
            var n = Interlocked.Increment(ref reads) - 1;
            if (n < gates.Length)
            {
                await gates[n].Task;
            }

            return await document.GetTextRunsAsync(page, token);
        };

        rig.Session.InvalidateRuns();
        var stale = rig.Session.LoadPageAsync(0);
        await ViewFixture.WaitAsync(() => Volatile.Read(ref reads) >= 1);
        rig.Session.InvalidateRuns();
        var current = rig.Session.LoadPageAsync(0);
        await ViewFixture.WaitAsync(() => Volatile.Read(ref reads) >= 2);

        gates[0].SetResult();
        await Task.WhenAny(stale, Task.Delay(300, TestContext.Current.CancellationToken));
        ViewFixture.Pump();
        Assert.False(rig.Session.IsLoaded(0), "the read of the old generation stores nothing");
        Assert.False(stale.IsCompleted, "the caller waits for the read of the current generation");

        gates[1].SetResult();
        await stale;
        await current;
        Assert.True(rig.Session.IsLoaded(0));
    }

    // The press found no line because the lines of the page were being read again after a write (the focus loss at the press had written the first line);
    // the line under the point is looked up when the release comes, once they are back.
    [AvaloniaFact]
    public async Task APressWhileTheLinesAreReadAgainStillOpensTheLineAtTheRelease()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var run = await rig.RunAsync("Dear customer, thank you");
        var at = rig.WindowPointOf(run);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Session.ReadRuns = async (document, page, token) =>
        {
            await gate.Task;
            return await document.GetTextRunsAsync(page, token);
        };
        rig.Session.InvalidateRuns();
        Assert.False(rig.Session.IsLoaded(0));

        rig.Window.MouseDown(at, MouseButton.Left);
        gate.SetResult();
        await ViewFixture.WaitAsync(() => rig.Session.IsLoaded(0));
        rig.Window.MouseUp(at, MouseButton.Left);

        await ViewFixture.WaitAsync(() => rig.Interaction.Field is not null);
        Assert.Equal(run.Text, rig.Session.DraftText);
    }

    [AvaloniaFact]
    public async Task SwitchingFromOneLineToAnotherAfterTheFirstWasSplitFindsTheSecondAgain()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-subset.pdf");
        await rig.EnterModeAsync();
        var first = await rig.RunAsync("Invoice total: 1,250 EUR");
        var second = await rig.RunAsync("Kontoauszug M\u00E4rz");
        var field = rig.OpenField(first);
        await ViewFixture.WaitAsync(() => rig.FieldHasFocus);
        TextEditRig.Type(field, "Invoice total: 1,259 EUR");

        // The change of the first line splits it into three objects: the second line stands at another index afterwards. A click on it (the real pointer)
        // writes the first line and opens the field on the second as it is found after the re-read.
        var at = rig.WindowPointOf(second);
        rig.Window.MouseDown(at, MouseButton.Left);
        rig.Window.MouseUp(at, MouseButton.Left);
        await ViewFixture.WaitAsync(() => rig.Interaction.Field is not null && rig.Session.DraftRun?.Text.TrimEnd() == "Kontoauszug M\u00E4rz");
        await rig.SettleAsync();
        var moved = (await rig.DocumentRunsAsync()).Single(r => r.Text.TrimEnd() == "Kontoauszug M\u00E4rz");
        Assert.NotEqual(second.ObjectIndex, moved.ObjectIndex);
        Assert.Equal(moved.ObjectIndex, rig.Session.DraftRun!.ObjectIndex);
        Assert.Contains("Invoice total: 1,259 EUR", await rig.PageTextAsync(), StringComparison.Ordinal);

        TextEditRig.Type(rig.Interaction.Field!, "Kontoauszug Mai");
        var result = await rig.Session.CommitAsync();
        Assert.Equal(TextEditCommitKind.Applied, result.Kind);
        await rig.SettleAsync();
        Assert.Contains("Kontoauszug Mai", await rig.PageTextAsync(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task TheFieldTakesAtMostOneCharacterOverTheLimitSoTheRefusalStillShows()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var field = await OpenAsync(rig, "Quarterly report 2026");
        Assert.Equal(TextEditLimits.MaxCharsPerRun + 1, field.Field.MaxLength);

        field.Field.Text = string.Empty;
        rig.Window.KeyTextInput(new string('x', TextEditLimits.MaxCharsPerRun + 50));
        ViewFixture.Pump();
        Assert.True(field.Field.Text!.Length <= TextEditLimits.MaxCharsPerRun + 1, field.Field.Text.Length.ToString(CultureInfo.InvariantCulture));
        Assert.True(field.Field.Text.Length >= TextEditLimits.MaxCharsPerRun);
    }

    [AvaloniaFact]
    public async Task EscBelongsToAnOpenCompositionAndInvisibleCharactersAreNamedByCode()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var field = await OpenAsync(rig, "Quarterly report 2026");
        var presenter = field.Field.GetVisualDescendants().OfType<TextPresenter>().First();
        TextEditRig.Type(field, "Quarterly report 2027");

        presenter.PreeditText = "\u3042";
        Assert.False(KeyDown(field.Field, Key.Escape), "Esc cancels the composition, not the draft");
        Assert.True(rig.Session.HasDraft);
        presenter.PreeditText = null;
        Assert.True(KeyDown(field.Field, Key.Escape));
        Assert.False(rig.Session.HasDraft);

        // A character that cannot be seen is named by its code point: a zero width space and joiner, a bidi override, a word joiner, a line separator.
        foreach (var (typed, code) in new[] { ("\u200B", "U+200B"), ("\u200D", "U+200D"), ("\u202E", "U+202E"), ("\u2060", "U+2060"), ("\u2028", "U+2028"), ("\u0007", "U+0007") })
        {
            var again = rig.OpenField(await rig.RunAsync("Quarterly report 2026"));
            TextEditRig.Type(again, "Quarterly" + typed + " report 2026");
            var preview = await rig.PreviewOfAsync("Quarterly" + typed + " report 2026");
            Assert.False(preview.CanCommit, code);
            Assert.Contains(code, rig.Session.Note, StringComparison.Ordinal);
            Assert.DoesNotContain(typed, rig.Session.Note!, StringComparison.Ordinal);
            rig.Session.Discard();
        }
    }

    [AvaloniaFact]
    public async Task TheOfferOfTheSwitchStaysUntilTheNextDecisionArrives()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-subset.pdf");
        await rig.EnterModeAsync();
        var bar = rig.Window.TextEditOf(rig.Tab)!.Bar;
        var field = rig.OpenField(await rig.RunAsync("Invoice total: 1,250 EUR"));
        TextEditRig.Type(field, "Invoice total: 1,259 EUR");
        Assert.True((await rig.PreviewOfAsync("Invoice total: 1,259 EUR")).WholeRunOffered);
        Assert.True(bar.WholeSwitchVisible);

        // The next key waits for its decision: the switch does not flicker away meanwhile.
        rig.Session.Debounce = TimeSpan.FromMinutes(5);
        TextEditRig.Type(field, "Invoice total: 1,258 EUR");
        Assert.Null(rig.Session.Preview);
        Assert.True(rig.Session.WholeRunOffered);
        Assert.True(bar.WholeSwitchVisible);

        // A decision that no longer offers it takes it away.
        rig.Session.Debounce = TimeSpan.Zero;
        TextEditRig.Type(field, "Invoice total: \u2192 EUR");
        await ViewFixture.WaitAsync(() => !bar.WholeSwitchVisible);
    }
}
