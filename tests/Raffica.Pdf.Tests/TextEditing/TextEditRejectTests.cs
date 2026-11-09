using System.Globalization;
using Avalonia.Headless.XUnit;
using Raffica.Core.TextEdit;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.TextEditing;
using Xunit;

namespace Raffica.Pdf.Tests.TextEditing;

/// <summary>The limits against hostile files and long input, through the UI path: every refusal ends in a note with its reason and no change.</summary>
public sealed class TextEditRejectTests
{
    // text-dense.pdf has no line of 2 001 characters and no page with that many runs; text-edit-hostile.pdf is the fixture made for both cases
    // (page 1: 10 000 text objects, page 2: one run of 2 001 characters). The long new text is typed into a line of text-edit-standard.pdf.
    [AvaloniaFact]
    public async Task LongRunAndManyRunsAreRefusedWithAReason()
    {
        using var hostile = await TextEditRig.OpenAsync("text-edit-hostile.pdf");
        await hostile.EnterModeAsync();

        // A page with more runs than the limit: every run of it is locked with the reason, nothing opens.
        var many = hostile.Session.RunsOf(0);
        Assert.True(many.Count > TextEditLimits.MaxRunsPerPage, many.Count.ToString(CultureInfo.InvariantCulture));
        Assert.All(many, run => Assert.Equal(TextEditLockReason.TooManyRuns, run.Lock));
        hostile.Session.Focus(many[0]);
        ViewFixture.Pump();
        Assert.Equal(TextEditWording.Lock(TextEditLockReason.TooManyRuns), hostile.Session.Note);
        Assert.Contains("5000", hostile.Session.Note, StringComparison.Ordinal);
        Assert.False(hostile.Session.OpenDraft(many[0]));
        Assert.Null(hostile.Interaction.Field);

        // A page with one run over the limit: that run is locked with its own reason.
        await hostile.Session.LoadPageAsync(1);
        var longRun = Assert.Single(hostile.Session.RunsOf(1));
        Assert.Equal(TextEditLimits.MaxCharsPerRun + 1, longRun.Text.Length);
        Assert.Equal(TextEditLockReason.RunTooLong, longRun.Lock);
        hostile.Session.Focus(longRun);
        Assert.Equal(TextEditWording.Lock(TextEditLockReason.RunTooLong), hostile.Session.Note);
        Assert.Contains("2000", hostile.Session.Note, StringComparison.Ordinal);
        Assert.False(hostile.Session.OpenDraft(longRun));
        Assert.False(hostile.Tab.IsDirty);

        // A text of 2 001 characters typed into a line is refused as a whole, with the limit named, and nothing changes.
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var run = await rig.RunAsync("Quarterly report 2026");
        var field = rig.OpenField(run);
        var text = new string('x', TextEditLimits.MaxCharsPerRun + 1);
        TextEditRig.Type(field, text);
        var preview = await rig.PreviewOfAsync(text);
        Assert.Equal(TextEditOutcome.Rejected, preview.Decision.Outcome);
        Assert.Equal(TextEditRejection.TooLong, preview.Decision.Rejection);
        Assert.Equal(TextEditWording.Rejection(TextEditRejection.TooLong), rig.Session.Note);
        Assert.Contains("2000", rig.Session.Note, StringComparison.Ordinal);

        var result = await rig.Session.CommitAsync();
        Assert.Equal(TextEditCommitKind.Refused, result.Kind);
        Assert.Equal(TextEditWording.Rejection(TextEditRejection.TooLong), result.Note);
        await rig.SettleAsync();
        Assert.Contains("Quarterly report 2026", await rig.PageTextAsync(), StringComparison.Ordinal);
        Assert.False(rig.Tab.Annotations.Editor.IsDirty);
        Assert.True(rig.Session.HasDraft);

        // Exactly the limit is fine.
        var exact = new string('x', TextEditLimits.MaxCharsPerRun);
        TextEditRig.Type(field, exact);
        Assert.True((await rig.PreviewOfAsync(exact)).CanCommit);
    }

    [AvaloniaFact]
    public async Task AControlCharacterIsRefusedAndNamedByItsCode()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();
        var run = await rig.RunAsync("Quarterly report 2026");
        var field = rig.OpenField(run);
        TextEditRig.Type(field, "Quarterly\treport 2026");
        var preview = await rig.PreviewOfAsync("Quarterly\treport 2026");
        Assert.Equal(TextEditOutcome.Rejected, preview.Decision.Outcome);
        Assert.Contains("U+0009", rig.Session.Note, StringComparison.Ordinal);
        Assert.Equal(TextEditCommitKind.Refused, (await rig.Session.CommitAsync()).Kind);
        Assert.False(rig.Tab.Annotations.Editor.IsDirty);
    }
}
