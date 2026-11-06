using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Documents;
using Xunit;

namespace Raffica.Pdf.Tests.Documents;

public sealed class CloseGuardTests : IDisposable
{
    private readonly PdfEngine _engine = new();
    private readonly List<PdfTab> _tabs = [];

    public void Dispose()
    {
        foreach (var tab in _tabs)
        {
            tab.Dispose();
        }
    }

    private PdfTab NewTab(bool dirty)
    {
        var tab = new PdfTab(new ViewerSession(_engine), "a.pdf");
        _tabs.Add(tab);
        if (dirty)
        {
            tab.MarkDirty();
        }

        return tab;
    }

    [Fact]
    public async Task CleanTabClosesWithoutAPrompt()
    {
        var prompts = 0;
        var guard = new CloseGuard(_ => { prompts++; return Task.FromResult(CloseChoice.Cancel); }, _ => Task.FromResult(true));
        Assert.True(await guard.CanCloseAsync(NewTab(dirty: false)));
        Assert.Equal(0, prompts);
    }

    [Fact]
    public async Task DirtyTabAsksOnce()
    {
        var prompts = 0;
        var saves = 0;
        var guard = new CloseGuard(_ => { prompts++; return Task.FromResult(CloseChoice.DontSave); }, _ => { saves++; return Task.FromResult(true); });
        Assert.True(await guard.CanCloseAsync(NewTab(dirty: true)));
        Assert.Equal(1, prompts);
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task SaveClosesWhenTheSaveSucceeded()
    {
        var saves = 0;
        var guard = new CloseGuard(_ => Task.FromResult(CloseChoice.Save), _ => { saves++; return Task.FromResult(true); });
        Assert.True(await guard.CanCloseAsync(NewTab(dirty: true)));
        Assert.Equal(1, saves);
    }

    [Fact]
    public async Task SaveThenCloseKeepsTheTabWhenADraftIsUnresolved()
    {
        // A draft that cannot be made (a refused text box text) keeps the tab dirty; a save writes nothing for it and returns, so the
        // close must not go on and drop the draft.
        var tab = NewTab(dirty: false);
        using var draft = tab.Annotations.BeginUnresolved();
        Assert.True(tab.IsDirty);
        var saves = 0;
        var guard = new CloseGuard(_ => Task.FromResult(CloseChoice.Save), _ => { saves++; return Task.FromResult(true); });

        Assert.False(await guard.CanCloseAsync(tab));
        Assert.Equal(1, saves);

        // Once the draft is gone the same choice closes the tab.
        draft.Dispose();
        Assert.False(tab.IsDirty);
        Assert.True(await guard.CanCloseAsync(tab));
    }

    [Fact]
    public async Task SaveThatFailsKeepsTheTab()
    {
        var guard = new CloseGuard(_ => Task.FromResult(CloseChoice.Save), _ => Task.FromResult(false));
        Assert.False(await guard.CanCloseAsync(NewTab(dirty: true)));
    }

    [Fact]
    public async Task CancelKeepsTheTab()
    {
        var saves = 0;
        var guard = new CloseGuard(_ => Task.FromResult(CloseChoice.Cancel), _ => { saves++; return Task.FromResult(true); });
        Assert.False(await guard.CanCloseAsync(NewTab(dirty: true)));
        Assert.Equal(0, saves);
    }

    [Fact]
    public async Task CloseAllStopsAtTheFirstCancel()
    {
        var asked = new List<PdfTab>();
        var first = NewTab(dirty: true);
        var second = NewTab(dirty: true);
        var third = NewTab(dirty: true);
        var guard = new CloseGuard(
            tab =>
            {
                asked.Add(tab);
                return Task.FromResult(asked.Count == 2 ? CloseChoice.Cancel : CloseChoice.DontSave);
            },
            _ => Task.FromResult(true));

        Assert.False(await guard.CanCloseAllAsync([first, second, third]));
        Assert.Equal([first, second], asked);

        var allow = new CloseGuard(_ => Task.FromResult(CloseChoice.DontSave), _ => Task.FromResult(true));
        Assert.True(await allow.CanCloseAllAsync([first, second, third, NewTab(dirty: false)]));
    }

    private async Task<PdfTab> PlanTabAsync(bool changed)
    {
        var session = new ViewerSession(_engine);
        await session.OpenAsync(Raffica.TestSupport.TestData.Pdf("pages-marked.pdf"));
        var tab = new PdfTab(session, Raffica.TestSupport.TestData.Pdf("pages-marked.pdf"));
        _tabs.Add(tab);
        tab.Organize.Enter();
        if (changed)
        {
            tab.Organize.Selection.Select(0);
            Assert.True(tab.Organize.Rotate(90).IsDone);
        }

        return tab;
    }

    private static PlanClose Plan(CloseChoice choice, Func<bool>? apply = null) =>
        new(_ => Task.FromResult(choice), _ => Task.FromResult(apply?.Invoke() ?? false));

    [Fact]
    public async Task PlanTabAsksItsOwnQuestionAndNeverTheSaveOne()
    {
        var plans = 0;
        var applies = 0;
        var guard = new CloseGuard(
            _ => throw new InvalidOperationException("the save question must not be asked"),
            _ => throw new InvalidOperationException("the save must not run"),
            new PlanClose(_ => { plans++; return Task.FromResult(CloseChoice.Save); }, _ => { applies++; return Task.FromResult(false); }));
        var tab = await PlanTabAsync(changed: true);

        // Apply that does not succeed keeps the tab; one that does closes it.
        Assert.False(await guard.CanCloseAsync(tab));
        Assert.Equal((1, 1), (plans, applies));
        var succeeding = new CloseGuard(_ => Task.FromResult(CloseChoice.Cancel), _ => Task.FromResult(true), Plan(CloseChoice.Save, () => true));
        Assert.True(await succeeding.CanCloseAsync(tab));
    }

    [Fact]
    public async Task PlanDiscardClosesAndPlanCancelKeeps()
    {
        var tab = await PlanTabAsync(changed: true);
        var cancel = new CloseGuard(_ => Task.FromResult(CloseChoice.DontSave), _ => Task.FromResult(true), Plan(CloseChoice.Cancel, () => true));
        Assert.False(await cancel.CanCloseAsync(tab));
        Assert.True(tab.Organize.IsActive);

        var discard = new CloseGuard(_ => throw new InvalidOperationException("asked"), _ => Task.FromResult(true), Plan(CloseChoice.DontSave));
        Assert.True(await discard.CanCloseAsync(tab));

        // The guard never ends the plan: it goes with the tab when the tab really closes.
        Assert.True(tab.Organize.IsActive);
    }

    [Fact]
    public async Task IdentityPlanTabClosesWithoutAnyQuestion()
    {
        var guard = new CloseGuard(
            _ => throw new InvalidOperationException("asked"),
            _ => throw new InvalidOperationException("saved"),
            new PlanClose(_ => throw new InvalidOperationException("asked about the plan"), _ => throw new InvalidOperationException("applied")));
        Assert.True(await guard.CanCloseAsync(await PlanTabAsync(changed: false)));
    }

    // Marks that were committed before the mode started stay unsaved next to the plan; neither an unchanged plan nor Discard may drop them.
    [Fact]
    public async Task UnsavedMarksNextToAnIdentityPlanAreStillAsked()
    {
        var tab = await PlanTabAsync(changed: false);
        using var pending = tab.Annotations.BeginUnresolved();
        Assert.True(tab.IsDirty);
        var asked = 0;
        var guard = new CloseGuard(
            _ => { asked++; return Task.FromResult(CloseChoice.Cancel); },
            _ => Task.FromResult(true),
            new PlanClose(_ => throw new InvalidOperationException("asked about the plan"), _ => throw new InvalidOperationException("applied")));

        Assert.False(await guard.CanCloseAsync(tab));
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task DiscardOfAChangedPlanStillAsksForTheUnsavedMarks()
    {
        var tab = await PlanTabAsync(changed: true);
        using var pending = tab.Annotations.BeginUnresolved();
        var asked = 0;
        var guard = new CloseGuard(
            _ => { asked++; return Task.FromResult(CloseChoice.Cancel); },
            _ => Task.FromResult(true),
            Plan(CloseChoice.DontSave));

        // The answer was Discard but the tab stays (Cancel at the marks question): nothing was dropped, the plan is still open and the tab dirty.
        Assert.False(await guard.CanCloseAsync(tab));
        Assert.Equal(1, asked);
        Assert.True(tab.Organize.IsActive);
        Assert.False(tab.Organize.Plan.IsIdentity);
        Assert.True(tab.IsDirty);
    }

    [Fact]
    public async Task DiscardWithoutMarksSkipsTheGenericQuestionAndSaveWritesMarksOnly()
    {
        // The plan makes the tab dirty by itself: after Discard only the marks decide, so a plan alone closes without the generic question.
        var tab = await PlanTabAsync(changed: true);
        var alone = new CloseGuard(_ => throw new InvalidOperationException("asked"), _ => throw new InvalidOperationException("saved"), Plan(CloseChoice.DontSave));
        Assert.True(tab.IsDirty);
        Assert.True(await alone.CanCloseAsync(tab));

        // With marks the Save of that question is the marks-only save, never the guard's own (which would be the Apply while the plan is open).
        using var pending = tab.Annotations.BeginUnresolved();
        var saves = 0;
        var marksSaves = 0;
        var guard = new CloseGuard(
            _ => Task.FromResult(CloseChoice.Save),
            _ => { saves++; return Task.FromResult(true); },
            new PlanClose(_ => Task.FromResult(CloseChoice.DontSave), _ => Task.FromResult(true), _ => { marksSaves++; return Task.FromResult(true); }));

        // The unresolved draft keeps the tab open after a save, as for any save.
        Assert.False(await guard.CanCloseAsync(tab));
        Assert.Equal((0, 1), (saves, marksSaves));
    }
}
