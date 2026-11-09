using Avalonia.Headless.XUnit;
using Raffica.Core.TextEdit;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Pdf.TextEditing;
using Xunit;

namespace Raffica.Pdf.Tests.TextEditing;

/// <summary>
/// The text editing against hostile files, through the window: the mode on <c>forms-hostile.pdf</c> (a page of widgets with loops, missing keys and an
/// oversized choice list) ends with every line either editable or locked with its reason, and changes nothing. The limits on runs and characters
/// (<c>text-edit-hostile.pdf</c>, 2 001 characters, a control character) are pinned in <c>TextEditRejectTests</c>, the locked lines of a real
/// page in <c>TextEditLockWordingTests</c>.
/// </summary>
public sealed class TextEditHostileTests
{
    [AvaloniaFact]
    public async Task AFormFileOfHostileFieldsEndsEveryLineInAnEditOrAReasonAndChangesNothing() =>
        await CheckAsync("forms-hostile.pdf", expectLocked: false);

    // text-edit-hostile.pdf page 1 has more runs than a page may offer, so every run of it is locked: the loop cannot pass without a reason.
    [AvaloniaFact]
    public async Task AFileOfTooManyRunsLocksEveryLineWithItsReasonAndChangesNothing() =>
        await CheckAsync("text-edit-hostile.pdf", expectLocked: true);

    private static async Task CheckAsync(string file, bool expectLocked)
    {
        using var rig = await TextEditRig.OpenAsync(file);
        await rig.EnterModeAsync();
        var original = await File.ReadAllBytesAsync(rig.Path, TestContext.Current.CancellationToken);

        await rig.Session.LoadPageAsync(0);
        var runs = rig.Session.RunsOf(0);
        Assert.NotEmpty(runs);
        if (expectLocked)
        {
            Assert.Contains(runs, r => r.Lock is not null);
        }

        foreach (var run in runs)
        {
            rig.Session.Focus(run);
            ViewFixture.Pump();
            if (run.Lock is { } reason)
            {
                Assert.Equal(TextEditWording.Lock(reason), rig.Session.Note);
                Assert.False(rig.Session.OpenDraft(run));
            }
        }

        Assert.False(rig.Tab.IsDirty);
        Assert.False(rig.Tab.Annotations.Editor.IsDirty);
        Assert.Equal(original, await File.ReadAllBytesAsync(rig.Path, TestContext.Current.CancellationToken));
    }
}
