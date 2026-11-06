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
    public async Task AFormFileOfHostileFieldsEndsEveryLineInAnEditOrAReasonAndChangesNothing()
    {
        using var rig = await TextEditRig.OpenAsync("forms-hostile.pdf");
        await rig.EnterModeAsync();
        var original = await File.ReadAllBytesAsync(rig.Path, TestContext.Current.CancellationToken);

        await rig.Session.LoadPageAsync(0);
        var runs = rig.Session.RunsOf(0);
        Assert.NotEmpty(runs);
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
