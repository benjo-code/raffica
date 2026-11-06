using System.Globalization;
using Avalonia.Headless.XUnit;
using Raffica.Core.TextEdit;
using Raffica.Pdf.TextEditing;
using Xunit;

namespace Raffica.Pdf.Tests.TextEditing;

/// <summary>Why a line cannot be edited: every reason has wording in both languages, and the lines of a real page show their reason instead of being dropped silently.</summary>
public sealed class TextEditLockWordingTests
{
    [AvaloniaFact]
    public void EveryLockReasonHasWordingInBothLanguages()
    {
        TextEditWordingTests.AssertEveryMember<TextEditLockReason>(TextEditWording.Lock);

        // The OCR layer is invisible text: the words say that the picture is what is seen, in both languages.
        var en = PdfStrings.GetIn("TextEditLockInvisibleText", CultureInfo.GetCultureInfo("en"));
        var de = PdfStrings.GetIn("TextEditLockInvisibleText", CultureInfo.GetCultureInfo("de"));
        Assert.Contains("picture is what you see", en, StringComparison.Ordinal);
        Assert.Contains("Bild das, was du siehst", de, StringComparison.Ordinal);

        // The limits are in the words of their reasons (the numbers are the ones of Core).
        Assert.Contains(TextEditLimits.MaxRunsPerPage.ToString(CultureInfo.InvariantCulture), TextEditWording.Lock(TextEditLockReason.TooManyRuns), StringComparison.Ordinal);
        Assert.Contains(TextEditLimits.MaxCharsPerRun.ToString(CultureInfo.InvariantCulture), TextEditWording.Lock(TextEditLockReason.RunTooLong), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task LockedLinesOfARealPageShowTheirReasonAndDoNotOpen()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-locked.pdf");
        await rig.EnterModeAsync();
        var runs = rig.Session.RunsOf(0);
        var locked = runs.Where(r => r.IsLocked).ToList();
        Assert.True(locked.Count >= 5, locked.Count.ToString(CultureInfo.InvariantCulture));

        // The scanned-picture case: the invisible layer says so.
        var invisible = runs.Single(r => r.Text.TrimEnd() == "Invisible text layer");
        Assert.Equal(TextEditLockReason.InvisibleText, invisible.Lock);

        // Every locked line, focused, shows its own reason in the bar and in the session; none opens a field, none changes the file.
        var bar = rig.Window.TextEditOf(rig.Tab)!.Bar;
        foreach (var run in locked)
        {
            rig.Session.Focus(run);
            ViewFixture_Pump();
            var reason = TextEditWording.Lock(run.Lock!.Value);
            Assert.Equal(reason, rig.Session.Note);
            Assert.Equal(reason, bar.Note);
            Assert.Equal(TextEditWording.RunName(runs.ToList().IndexOf(run) + 1, 1, locked: true), bar.LineName);
            Assert.False(rig.Session.OpenDraft(run));
            Assert.Null(rig.Interaction.Field);
            Assert.False(rig.Session.HasDraft);
        }

        Assert.False(rig.Tab.IsDirty);

        // An editable line of the same page opens and clears the reason.
        var open = runs.Single(r => r.Text.TrimEnd() == "Editable control line");
        rig.Session.Focus(open);
        Assert.Null(rig.Session.Note);
        Assert.True(rig.Session.OpenDraft(open));

        // The name of a locked outline for assistive technology is its place, never its text.
        Assert.DoesNotContain("Invisible", bar.LineName, StringComparison.Ordinal);
        Assert.DoesNotContain("Invisible", TextEditWording.RunName(2, 1, locked: true), StringComparison.Ordinal);
    }

    private static void ViewFixture_Pump() => Raffica.Pdf.Tests.Viewing.ViewFixture.Pump();
}
