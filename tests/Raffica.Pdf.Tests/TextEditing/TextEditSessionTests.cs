using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Raffica.Core.Pdf;
using Raffica.Core.TextEdit;
using Raffica.Core.Viewing;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.TextEditing;
using Xunit;

namespace Raffica.Pdf.Tests.TextEditing;

/// <summary>The text edit session through the window: a corrected line, an emptied line, missing characters, a text no font has, the switch for the whole line and a stale run.</summary>
public sealed class TextEditSessionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // text-basic.pdf has one line per page, so "the rest of the page" is shown on text-edit-standard.pdf, which has two lines on its page.
    [AvaloniaFact]
    public async Task CorrectedLineChangesItAndKeepsTheRestOfThePage()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var edited = await rig.RunAsync("Quarterly report 2026");
        var other = await rig.RunAsync("Dear customer, thank you");

        var field = rig.OpenField(edited);
        await ViewFixture.WaitAsync(() => rig.FieldHasFocus);
        TextEditRig.Type(field, "Quarterly report 2027");
        rig.Press(PhysicalKey.Enter);
        await rig.Interaction.Completion;
        await rig.SettleAsync();

        Assert.False(rig.Session.HasDraft);
        Assert.Null(rig.Interaction.Field);
        var after = await rig.DocumentRunsAsync();
        Assert.Contains(after, r => r.Text.TrimEnd() == "Quarterly report 2027");
        Assert.DoesNotContain(after, r => r.Text.Contains("2026", StringComparison.Ordinal));
        var untouched = after.Single(r => r.Text.TrimEnd() == "Dear customer, thank you");
        Assert.Equal(other.Text, untouched.Text);
        Assert.Equal(other.Bounds, untouched.Bounds);
        Assert.Equal(other.FontName, untouched.FontName);
        Assert.Equal(PdfStrings.TextEditNoteDone, rig.Session.Note);
        Assert.True(rig.Tab.IsDirty);

        // Saved with the real save: the file reopens with the new text, and the bytes of the original are its beginning.
        var saved = await rig.SaveAsync();
        var original = TextEditRig.OriginalBytes("text-edit-standard.pdf");
        Assert.True(saved.AsSpan(0, original.Length).SequenceEqual(original));
        using var reopened = new ViewerSession(new PdfEngine());
        await reopened.OpenAsync(rig.Path, null);
        var runs = await reopened.Document!.GetTextRunsAsync(0, Ct);
        Assert.Contains(runs, r => r.Text.TrimEnd() == "Quarterly report 2027");
        Assert.DoesNotContain(runs, r => r.Text.Contains("2026", StringComparison.Ordinal));
        Assert.Contains(runs, r => r.Text.TrimEnd() == "Dear customer, thank you");
    }

    [AvaloniaFact]
    public async Task EmptyTextRemovesTheRunAndUndoBringsItBack()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var run = await rig.RunAsync("Quarterly report 2026");
        var field = rig.OpenField(run);
        TextEditRig.Type(field, string.Empty);

        var preview = await rig.PreviewOfAsync(string.Empty);
        Assert.Equal(TextEditOutcome.Remove, preview.Decision.Outcome);
        Assert.Equal(PdfStrings.TextEditNoteEmpty, rig.Session.Note);
        Assert.Equal("Empty: the line is taken away. Undo brings it back.", PdfStrings.TextEditNoteEmpty);

        var result = await rig.Session.CommitAsync();
        Assert.Equal(TextEditCommitKind.Applied, result.Kind);
        await rig.SettleAsync();
        Assert.DoesNotContain("Quarterly", await rig.PageTextAsync(), StringComparison.Ordinal);
        Assert.True(rig.Tab.Annotations.CanUndo);

        await rig.Tab.Annotations.UndoAsync();
        await rig.SettleAsync();
        Assert.Contains("Quarterly report 2026", await rig.PageTextAsync(), StringComparison.Ordinal);
        Assert.Contains(await rig.DocumentRunsAsync(), r => r.Text.TrimEnd() == "Quarterly report 2026");
    }

    [AvaloniaFact]
    public async Task MissingCharactersGoToTheReplacementFontAndTheBarSaysWhich()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-subset.pdf");
        await rig.EnterModeAsync();
        var run = await rig.RunAsync("Invoice total: 1,250 EUR");
        var field = rig.OpenField(run);
        TextEditRig.Type(field, "Invoice total: 1,259 EUR");

        // The one decision of Core: the same Evaluate the commit makes, so the bar and the write agree.
        var preview = await rig.PreviewOfAsync("Invoice total: 1,259 EUR");
        Assert.Equal(TextEditOutcome.SubstituteMissing, preview.Decision.Outcome);
        Assert.Equal(["9"], preview.Decision.Missing);
        var said = PdfStrings.Fill(PdfStrings.TextEditNoteMissingFormat, "9");
        Assert.Equal(said, rig.Session.Note);
        Assert.Equal(said, rig.Window.TextEditOf(rig.Tab)!.Bar.Note);
        Assert.DoesNotContain("Geist", said, StringComparison.Ordinal);

        var result = await rig.Session.CommitAsync();
        Assert.Equal(TextEditCommitKind.Applied, result.Kind);
        Assert.Equal(TextEditOutcome.SubstituteMissing, result.Decision!.Outcome);
        await rig.SettleAsync();
        Assert.Contains("Invoice total: 1,259 EUR", await rig.PageTextAsync(), StringComparison.Ordinal);
        var pieces = (await rig.DocumentRunsAsync()).Where(r => r.Bounds.Top < run.Bounds.Bottom + 1 && r.Bounds.Bottom > run.Bounds.Top - 1 && r.Bounds.Left < 200).OrderBy(r => r.Bounds.Left).ToArray();
        Assert.Equal(["Invoice total: 1,25", "9", " EUR"], pieces.Select(r => r.Text));
        Assert.Equal(run.FontName, pieces[0].FontName);
        Assert.NotEqual(run.FontName, pieces[1].FontName);
    }

    [AvaloniaFact]
    public async Task CharacterInNeitherFontRejectsTheWholeText()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-subset.pdf");
        await rig.EnterModeAsync();
        var run = await rig.RunAsync("Invoice total: 1,250 EUR");
        var before = await rig.DocumentRunsAsync();
        var field = rig.OpenField(run);
        TextEditRig.Type(field, "Invoice total: → EUR");

        var preview = await rig.PreviewOfAsync("Invoice total: → EUR");
        Assert.Equal(TextEditOutcome.Rejected, preview.Decision.Outcome);
        Assert.Equal(TextEditRejection.UnsupportedCharacters, preview.Decision.Rejection);
        Assert.False(preview.CanCommit);
        var said = PdfStrings.Fill(PdfStrings.TextEditRejectUnsupportedFormat, "→");
        Assert.Equal(said, rig.Session.Note);

        // The whole text is refused with its reason, nothing is changed, and the draft stays (and counts as unsaved) until the user ends it.
        var result = await rig.Session.CommitAsync();
        Assert.Equal(TextEditCommitKind.Refused, result.Kind);
        Assert.Equal(said, result.Note);
        Assert.DoesNotContain("refused", result.Note, StringComparison.OrdinalIgnoreCase);
        await rig.SettleAsync();
        Assert.True(rig.Session.HasDraft);
        Assert.True(rig.Tab.Annotations.HasUnresolved);
        Assert.False(rig.Tab.Annotations.Editor.IsDirty);
        Assert.Equal(before.Select(r => r.Text), (await rig.DocumentRunsAsync()).Select(r => r.Text));
        Assert.Contains("Invoice total: 1,250 EUR", await rig.PageTextAsync(), StringComparison.Ordinal);

        rig.Session.Discard();
        ViewFixture.Pump();
        Assert.False(rig.Tab.Annotations.HasUnresolved);
        Assert.False(rig.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task WholeRunSwitchIsOfferedOnlyWhenPossibleAndNeverPreselected()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-subset.pdf");
        await rig.EnterModeAsync();
        var run = await rig.RunAsync("Invoice total: 1,250 EUR");
        var bar = rig.Window.TextEditOf(rig.Tab)!.Bar;
        var field = rig.OpenField(run);

        // Never on by itself: not for a new draft, not when it is offered.
        Assert.False(rig.Session.WholeRun);
        Assert.False(bar.WholeSwitchVisible);
        TextEditRig.Type(field, "Invoice total: 1,259 EUR");
        var possible = await rig.PreviewOfAsync("Invoice total: 1,259 EUR");
        Assert.True(possible.WholeRunOffered);
        Assert.True(bar.WholeSwitchVisible);
        Assert.False(bar.WholeSwitchOn);
        Assert.False(rig.Session.WholeRun);

        // Not offered when a character is not in the replacement font, and not for the text as it is.
        TextEditRig.Type(field, "Invoice total: → EUR");
        var impossible = await rig.PreviewOfAsync("Invoice total: → EUR");
        Assert.False(impossible.WholeRunOffered);
        Assert.False(impossible.Decision.WholeRunInGeistPossible);
        Assert.False(bar.WholeSwitchVisible);
        TextEditRig.Type(field, "Invoice total: 1,250 EUR");
        Assert.False((await rig.PreviewOfAsync("Invoice total: 1,250 EUR")).WholeRunOffered);

        // Turned on by the user: the decision is the whole line in the replacement font, and it is written so; the next draft starts with it off.
        TextEditRig.Type(field, "Invoice total: 1,259 EUR");
        await rig.PreviewOfAsync("Invoice total: 1,259 EUR");
        bar.SetWholeSwitch(true);
        Assert.True(rig.Session.WholeRun);
        var whole = await rig.PreviewOfAsync("Invoice total: 1,259 EUR", whole: true);
        Assert.Equal(TextEditOutcome.WholeRunInGeist, whole.Decision.Outcome);
        Assert.Equal(PdfStrings.TextEditNoteWholeRun, rig.Session.Note);
        var result = await rig.Session.CommitAsync();
        Assert.Equal(TextEditCommitKind.Applied, result.Kind);
        Assert.Equal(TextEditOutcome.WholeRunInGeist, result.Decision!.Outcome);
        await rig.SettleAsync();
        var written = (await rig.DocumentRunsAsync()).Single(r => r.Text.TrimEnd() == "Invoice total: 1,259 EUR");
        Assert.NotEqual(run.FontName, written.FontName);

        await rig.Session.LoadPageAsync(0);
        await ViewFixture.WaitAsync(() => rig.Session.IsLoaded(0));
        rig.OpenField(await rig.RunAsync("Invoice total: 1,259 EUR"));
        Assert.False(rig.Session.WholeRun);
        Assert.False(bar.WholeSwitchOn);
    }

    [AvaloniaFact]
    public async Task StaleRunIsRefused()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var run = await rig.RunAsync("Quarterly report 2026");
        var field = rig.OpenField(run);
        TextEditRig.Type(field, "Quarterly report 2027");

        // Behind the session's back the same line is changed (another tool, a quick second edit): the run the draft holds no longer matches the page.
        await rig.Tab.Annotations.Editor.ApplyTextEditAsync(run, "Quarterly report 2099", TextEditMode.KeepFont);
        await rig.SettleAsync();

        var result = await rig.Session.CommitAsync();
        Assert.Equal(TextEditCommitKind.Stale, result.Kind);
        Assert.Equal(PdfStrings.TextEditNoteStale, result.Note);
        await rig.SettleAsync();
        var text = await rig.PageTextAsync();
        Assert.Contains("Quarterly report 2099", text, StringComparison.Ordinal);
        Assert.DoesNotContain("2027", text, StringComparison.Ordinal);
        Assert.True(rig.Session.HasDraft, "the text stays in the field with its reason");
        Assert.Equal(PdfStrings.TextEditNoteStale, rig.Session.Note);

        // The same text for a stale run whose new characters are all in the old text goes the other way round (the write is refused by Core), also without a change.
        rig.Session.Discard();
        var second = rig.OpenField(run);
        TextEditRig.Type(second, "Quarterly report 2026 ");
        var again = await rig.Session.CommitAsync();
        Assert.Equal(TextEditCommitKind.Stale, again.Kind);
        Assert.DoesNotContain("2026", await rig.PageTextAsync(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task AClickOnALineOpensTheFieldOverIt()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var run = await rig.RunAsync("Dear customer, thank you");

        var at = rig.WindowPointOf(run);
        rig.Window.MouseDown(at, MouseButton.Left);
        rig.Window.MouseUp(at, MouseButton.Left);
        await ViewFixture.WaitAsync(() => rig.Interaction.Field is not null);

        Assert.Equal(run.Text, rig.Session.DraftText);
        Assert.Equal(run.ObjectIndex, rig.Session.Focused!.ObjectIndex);
        Assert.Equal(run.Text, rig.Interaction.Field!.Field.Text);
        // Over the line, in its size: the field covers the outline of the line.
        var rect = rig.View.ToView(run.Page, run.Bounds)!.Value;
        Assert.True(rig.View.FieldRect.Contains(rect.Center));
        Assert.True(rig.View.FieldRect.Width >= rect.Width);
        // The untouched field is no change of the file.
        Assert.False(rig.Tab.IsDirty);
    }
}
