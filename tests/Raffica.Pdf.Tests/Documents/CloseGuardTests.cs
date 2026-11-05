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
}
