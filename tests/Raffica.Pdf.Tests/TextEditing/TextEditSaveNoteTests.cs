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
}
