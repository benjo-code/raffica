using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Documents;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Documents;

public sealed class WorkspaceTests : IDisposable
{
    private readonly PdfEngine _engine = new();
    private readonly List<ViewerSession> _sessions = [];

    public void Dispose()
    {
        foreach (var session in _sessions)
        {
            session.Dispose();
        }
    }

    private static Workspace NewWorkspace() => new(new CloseGuard(_ => Task.FromResult(CloseChoice.DontSave), _ => Task.FromResult(true)));

    private async Task<ViewerSession> OpenAsync()
    {
        var session = new ViewerSession(_engine);
        _sessions.Add(session);
        await session.OpenAsync(TestData.Pdf("hello_world.pdf"));
        return session;
    }

    [Fact]
    public async Task OpeningTheSameFileSelectsTheExistingTab()
    {
        var workspace = NewWorkspace();
        var path = TestData.Pdf("hello_world.pdf");
        var first = workspace.Add(await OpenAsync(), path);
        var other = workspace.Add(await OpenAsync(), Path.Combine(Path.GetDirectoryName(path)!, "other.pdf"));
        Assert.Same(other, workspace.Active);

        // The same file again, spelled differently (a ".." segment; on Windows also another case), is found.
        var spelled = Path.Combine(Path.GetDirectoryName(path)!, "..", "pdfs", Path.GetFileName(path));
        Assert.Same(first, workspace.Find(spelled));
        Assert.Null(workspace.Find(Path.Combine(Path.GetDirectoryName(path)!, "nothing.pdf")));
        if (OperatingSystem.IsWindows())
        {
            Assert.Same(first, workspace.Find(path.ToUpperInvariant()));
        }

        workspace.Select(workspace.Find(path)!);
        Assert.Same(first, workspace.Active);
        Assert.Equal(2, workspace.Tabs.Count);
    }

    [Fact]
    public async Task ClosingTheActiveTabSelectsTheNeighbour()
    {
        var workspace = NewWorkspace();
        var a = workspace.Add(await OpenAsync(), "a.pdf");
        var b = workspace.Add(await OpenAsync(), "b.pdf");
        var c = workspace.Add(await OpenAsync(), "c.pdf");
        workspace.Select(b);

        Assert.True(await workspace.CloseAsync(b));
        Assert.Same(c, workspace.Active);
        Assert.Equal([a, c], workspace.Tabs);

        Assert.True(await workspace.CloseAsync(c));
        Assert.Same(a, workspace.Active);
    }

    [Fact]
    public async Task ActiveTabAloneHoldsTheFullRenderBudget()
    {
        // The public API of ViewerSession does not report SetActive calls, so the effect shows in the budget of each render cache.
        var workspace = NewWorkspace();
        var first = await OpenAsync();
        var second = await OpenAsync();
        var a = workspace.Add(first, "a.pdf");
        var b = workspace.Add(second, "b.pdf");

        Assert.Equal(TabBudget.InactiveBytes, first.Cache!.BudgetBytes);
        Assert.Equal(TabBudget.ActiveBytes, second.Cache!.BudgetBytes);

        workspace.Select(a);
        Assert.Equal(TabBudget.ActiveBytes, first.Cache.BudgetBytes);
        Assert.Equal(TabBudget.InactiveBytes, second.Cache.BudgetBytes);

        workspace.SelectNext();
        Assert.Same(b, workspace.Active);
        Assert.Equal(TabBudget.InactiveBytes, first.Cache.BudgetBytes);
        Assert.Equal(TabBudget.ActiveBytes, second.Cache.BudgetBytes);

        workspace.SelectPrevious();
        Assert.Same(a, workspace.Active);
        Assert.Equal(TabBudget.ActiveBytes, first.Cache.BudgetBytes);
        Assert.Equal(TabBudget.InactiveBytes, second.Cache.BudgetBytes);
    }

    [Fact]
    public async Task ClosingTheLastTabLeavesTheWorkspaceEmpty()
    {
        var workspace = NewWorkspace();
        var changes = 0;
        workspace.ActiveChanged += () => changes++;
        var tab = workspace.Add(await OpenAsync(), "a.pdf");
        Assert.Equal(1, changes);

        Assert.True(await workspace.CloseAsync(tab));
        Assert.Empty(workspace.Tabs);
        Assert.Null(workspace.Active);
        Assert.Equal(2, changes);
        Assert.Null(tab.Session.Document);
    }

    [Fact]
    public async Task ACancelledCloseKeepsTheTabAndItsSession()
    {
        var workspace = new Workspace(new CloseGuard(_ => Task.FromResult(CloseChoice.Cancel), _ => Task.FromResult(true)));
        var tab = workspace.Add(await OpenAsync(), "a.pdf");
        tab.MarkDirty();

        Assert.False(await workspace.CloseAsync(tab));
        Assert.Same(tab, workspace.Active);
        Assert.Equal(ViewerState.Ready, tab.Session.State);
    }
}
