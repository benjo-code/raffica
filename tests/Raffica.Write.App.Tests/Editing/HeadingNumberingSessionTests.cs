using Avalonia.Headless.XUnit;
using Raffica.Write.Documents;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Xunit;

namespace Raffica.WriteApp.Tests.Editing;

/// <summary>The session side of the heading numbering switch: a result with a style and no block change restyles the layout, marks the tab dirty and is undone as one step.</summary>
public sealed class HeadingNumberingSessionTests
{
    [AvaloniaFact]
    public async Task TheSwitchGoesThroughTheStylePathAndIsOneUndoStep()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = await window.Flow.NewDocumentAsync(PaperSize.A4, CancellationToken.None);
        WindowFixture.Pump();
        Assert.NotNull(tab);
        tab.Runner.Start();
        await WindowFixture.WaitAsync(() => tab.Runner.FullModelReady || tab.Runner.IsFaulted);
        Assert.True(tab.Runner.FullModelReady);
        var session = tab.EnsureEditor();
        var model = tab.Docx.Model;
        var layout = tab.Runner.PageLayout;
        var restarts = layout.Stats.Restarts;
        Assert.False(tab.IsDirty);
        Assert.Equal(HeadingNumberingState.Off, HeadingNumbering.StateOf(model));

        var result = session.Apply(e => e.SetHeadingNumbering(true, session.Selection!.Value.Start), 0);

        Assert.NotNull(result?.Style);
        Assert.Empty(result.Changes);
        Assert.True(layout.Stats.Restarts > restarts);
        Assert.Equal(HeadingNumberingState.On, HeadingNumbering.StateOf(model));
        Assert.True(tab.IsDirty);
        Assert.True(session.CanUndo);

        Assert.NotNull(session.Apply(e => e.History.Undo(), 0));
        Assert.Equal(HeadingNumberingState.Off, HeadingNumbering.StateOf(model));
        Assert.False(session.CanUndo);
        Assert.False(tab.IsDirty);
    }
}
