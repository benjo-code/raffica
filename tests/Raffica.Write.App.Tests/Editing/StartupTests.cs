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

    [AvaloniaFact]
    public async Task StartupCreatesNoPickerWork()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = Assert.IsType<Raffica.Write.Documents.Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
        WindowFixture.Pump();
        await WindowFixture.WaitAsync(() => window.ViewOf(tab) is not null);
        window.Ribbon.RefreshCommandStates();
        WindowFixture.Pump();

        // The ribbon holds the pickers, but no list was asked (the installed fonts, the styles of the file), no font was listed or loaded, no session or timer was made and nothing was read for the display.
        Assert.NotNull(window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == Raffica.Write.Commands.WriteCommandIds.Font).Picker);
        Assert.Equal(0, window.Pickers.ListReads);
        Assert.Null(window.FormatRefresh);
        Assert.Null(tab.Editor);
        Assert.Null(Raffica.Write.Commands.EditCommandState.FormatOf(tab));
        Assert.Null(tab.Editor);
    }

    [AvaloniaFact]
    public async Task ThePaneDoesNotExistBeforeFirstOpen()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = Assert.IsType<Raffica.Write.Documents.Opened>(await window.Flow.OpenAsync(WindowFixture.Thesis)).Tab;
        WindowFixture.Pump();
        await WindowFixture.WaitAsync(() => window.ViewOf(tab) is not null);
        window.Ribbon.RefreshCommandStates();
        WindowFixture.Pump();

        // The ribbon has the Formatting switch, but there is no slot, no pane, no timer, no editing session and nothing was read for an inspector.
        Assert.Contains(window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands), c => c.Id == Raffica.Write.Commands.WriteCommandIds.FormattingPane);
        Assert.Null(window.FormattingHost);
        Assert.Null(window.Frame.RightPane);
        Assert.False(window.IsFormattingOpen);
        Assert.Null(window.FormattingOf(tab));
        Assert.Null(tab.Editor);

        // Opening it makes the slot and the pane of the active tab, but still no session (the first caret makes it) and no read.
        Assert.True(window.RunCommand(Raffica.Write.Commands.WriteCommandIds.FormattingPane));
        WindowFixture.Pump();
        var pane = window.FormattingOf(tab)!;
        Assert.NotNull(pane);
        Assert.Null(tab.Editor);
        Assert.False(pane.Refresh.TimerCreated);
        Assert.Equal(WriteStrings.FormattingNoSelection, pane.HintText);
    }
}
