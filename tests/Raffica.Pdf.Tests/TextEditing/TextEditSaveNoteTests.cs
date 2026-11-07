using System.Globalization;
using Avalonia.Headless.XUnit;
using Raffica.Core.Annotations;
using Raffica.Core.Text;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.TextEditing;

/// <summary>After the first save of edited text the window says where the old text stays; once per tab, no forced rewrite.</summary>
public sealed class TextEditSaveNoteTests
{
    [AvaloniaFact]
    public async Task OldTextNoteShownOnceAfterTheFirstSave()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var bar = rig.Window.TextEditOf(rig.Tab)!.Bar;
        var note = PdfStrings.GetIn("TextEditOldTextNote", CultureInfo.GetCultureInfo("en"));
        Assert.Equal("The old text stays in earlier versions of the file. To remove it for real: Redact.", note);
        Assert.Equal("Der alte Text bleibt in früheren Fassungen der Datei erhalten. Zum echten Entfernen: Schwärzen.", PdfStrings.GetIn("TextEditOldTextNote", CultureInfo.GetCultureInfo("de")));

        // Before any save the note is not there.
        Assert.False(bar.OldTextShown);
        Assert.True(string.IsNullOrEmpty(rig.Window.Status.Hint));

        // An edit is written, and the first save says so in the status bar and in the hint bar.
        var run = await rig.RunAsync("Quarterly report 2026");
        TextEditRig.Type(rig.OpenField(run), "Quarterly report 2027");
        Assert.Equal(Raffica.Pdf.TextEditing.TextEditCommitKind.Applied, (await rig.Session.CommitAsync()).Kind);
        await rig.SettleAsync();
        Assert.True(string.IsNullOrEmpty(rig.Window.Status.Hint), "an edit that is not saved says nothing about the file");
        await rig.SaveAsync();
        Assert.Equal(PdfStrings.TextEditOldTextNote, rig.Window.Status.Hint);
        Assert.True(bar.OldTextShown);

        // The second edit and save of the same tab do not say it again.
        rig.Window.Status.Hint = null;
        await rig.Session.LoadPageAsync(0);
        await ViewFixture.WaitAsync(() => rig.Session.IsLoaded(0));
        var again = await rig.RunAsync("Dear customer, thank you");
        TextEditRig.Type(rig.OpenField(again), "Dear customer, thanks");
        Assert.Equal(Raffica.Pdf.TextEditing.TextEditCommitKind.Applied, (await rig.Session.CommitAsync()).Kind);
        await rig.SettleAsync();
        await rig.SaveAsync();
        Assert.True(string.IsNullOrEmpty(rig.Window.Status.Hint), "once per tab");
    }

    [AvaloniaFact]
    public async Task ASaveWithoutAnEditedTextSaysNothing()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        Assert.NotNull(await rig.Tab.Annotations.Editor.ApplyMarkupAsync(0, AnnotationKind.Highlight, [new PageRect(10, 10, 100, 30)], new AnnotationColor(255, 255, 0), null, null));
        await rig.Tab.Annotations.FlushAsync();
        await rig.SaveAsync();
        Assert.True(string.IsNullOrEmpty(rig.Window.Status.Hint));
        Assert.Null(rig.Tab.TextEditIfCreated);
    }

    [AvaloniaFact]
    public async Task TheNoteGoesWithTheModeAndIsNeverForced()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var run = await rig.RunAsync("Quarterly report 2026");
        TextEditRig.Type(rig.OpenField(run), "Quarterly report 2027");
        await rig.Session.CommitAsync();
        await rig.SettleAsync();
        var saved = await rig.SaveAsync();

        // The save is the incremental one (the bytes of the original stay the beginning of the file): no forced rewrite.
        var original = TextEditRig.OriginalBytes("text-edit-standard.pdf");
        Assert.True(saved.AsSpan(0, original.Length).SequenceEqual(original));
        Assert.Equal(PdfStrings.TextEditOldTextNote, rig.Window.Status.Hint);

        // Leaving the mode gives the cell of the status bar back.
        rig.Command("text.editDone").Run!();
        await ViewFixture.WaitAsync(() => !rig.Session.IsActive);
        Assert.True(string.IsNullOrEmpty(rig.Window.Status.Hint));
    }

    [AvaloniaFact]
    public async Task ANoteThatCouldNotBeShownIsOfferedAgainByTheNextSave()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");

        // The mode is off and the status cell is taken by another owner: the note is not shown, so its slot is not used up.
        rig.Window.Status.Hint = "another owner";
        var run = (await rig.DocumentRunsAsync()).Single(r => r.Text.TrimEnd() == "Quarterly report 2026");
        await rig.Tab.Annotations.Editor.ApplyTextEditAsync(run, "Quarterly report 2027", Raffica.Core.TextEdit.TextEditMode.KeepFont);
        await rig.Tab.Annotations.FlushAsync();
        await rig.SaveAsync();
        Assert.Equal("another owner", rig.Window.Status.Hint);

        // The cell is free at the next save of edited text: the note is shown then, once.
        rig.Window.Status.Hint = null;
        var again = (await rig.DocumentRunsAsync()).Single(r => r.Text.TrimEnd() == "Dear customer, thank you");
        await rig.Tab.Annotations.Editor.ApplyTextEditAsync(again, "Dear customer, thanks", Raffica.Core.TextEdit.TextEditMode.KeepFont);
        await rig.Tab.Annotations.FlushAsync();
        await rig.SaveAsync();
        Assert.Equal(PdfStrings.TextEditOldTextNote, rig.Window.Status.Hint);
    }

    [AvaloniaFact]
    public async Task AnEditThatWasUndoneBeforeTheSaveSaysNothingAndTheHintGoesWithTheNextChange()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        var run = (await rig.DocumentRunsAsync()).Single(r => r.Text.TrimEnd() == "Quarterly report 2026");
        await rig.Tab.Annotations.Editor.ApplyTextEditAsync(run, "Quarterly report 2027", Raffica.Core.TextEdit.TextEditMode.KeepFont);
        await rig.Tab.Annotations.UndoAsync();
        Assert.NotNull(await rig.Tab.Annotations.Editor.ApplyMarkupAsync(0, AnnotationKind.Highlight, [new PageRect(10, 10, 100, 30)], new AnnotationColor(255, 255, 0), null, null));
        await rig.Tab.Annotations.FlushAsync();
        Assert.True(rig.Tab.Annotations.Editor.HasTextEdits, "the history knows that a text was touched");
        Assert.False(rig.Tab.Annotations.Editor.HasLiveTextEdits);
        await rig.SaveAsync();
        Assert.True(string.IsNullOrEmpty(rig.Window.Status.Hint), "no changed text was saved");

        // A real edit is noted (the undone one did not use up the slot), and the next change of the tab takes the hint away again.
        var real = (await rig.DocumentRunsAsync()).Single(r => r.Text.TrimEnd() == "Quarterly report 2026");
        await rig.Tab.Annotations.Editor.ApplyTextEditAsync(real, "Quarterly report 2029", Raffica.Core.TextEdit.TextEditMode.KeepFont);
        await rig.Tab.Annotations.FlushAsync();
        await rig.SaveAsync();
        Assert.Equal(PdfStrings.TextEditOldTextNote, rig.Window.Status.Hint);
        var next = (await rig.DocumentRunsAsync()).Single(r => r.Text.TrimEnd() == "Dear customer, thank you");
        await rig.Tab.Annotations.Editor.ApplyTextEditAsync(next, "Dear customer, thanks", Raffica.Core.TextEdit.TextEditMode.KeepFont);
        await rig.Tab.Annotations.FlushAsync();
        ViewFixture.Pump();
        Assert.True(string.IsNullOrEmpty(rig.Window.Status.Hint), "the next change of the tab ends the hint");
    }
}
