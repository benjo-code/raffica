using Avalonia.Headless.XUnit;
using Raffica.WriteApp.Tests.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Editing;

/// <summary>
/// The umbrella guard of milestone 4.3a: a viewer that only opens and scrolls creates nothing of editing. The narrower guards are
/// <c>StartupCreatesNoEditSession</c> (EditSessionTests), <c>NoBlinkTimerBeforeTheFirstCaret</c> (PageViewEditingTests) and
/// <c>NoImeClientBeforeTheFirstFocus</c> (ImeTests); this one asserts the same through the seams of one opened view.
/// </summary>
public sealed class StartupTests
{
    [AvaloniaFact]
    public async Task StartupCreatesNoEditorWork()
    {
        using var h = new ViewHarness(WindowFixture.Thesis);
        var view = h.View;
        var tab = h.Tab;
        await h.LayOutEverythingAsync();

        // The session owns the TextEditor, its EditHistory and the clone of every edited paragraph: none of them exists before the first caret.
        Assert.Null(tab.Editor);

        // Scrolling and a frame per step touch no editing state either.
        for (var i = 0; i < 12; i++)
        {
            Assert.True(view.ScrollByWheel(-3));
            ViewHarness.Pump();
        }

        view.ScrollToStart();
        ViewHarness.Pump();
        view.Focus();
        ViewHarness.Pump();

        Assert.Null(tab.Editor);
        Assert.Null(view.Caret);
        Assert.Null(view.EditClipboard);          // no clipboard object, so no clipboard access
        Assert.False(view.ImeClientCreated);      // no input method client
        Assert.False(view.BlinkTimerCreated);     // no timer
        Assert.False(view.BlinkRunning);
        Assert.False(view.IsComposing);
        Assert.True(h.Tab.Runner.PageLayout.Stats.Edits == 0, "no edit reached the layout");
        Assert.Equal(0, tab.Docx.Model.Edits.Revision);
        Assert.False(tab.Docx.Model.Edits.IsDirty);
    }
}
