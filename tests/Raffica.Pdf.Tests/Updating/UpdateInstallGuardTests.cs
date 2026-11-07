using Raffica.Core.Pdf;
using Raffica.Core.Updates;
using Raffica.Core.Viewing;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Updating;
using Xunit;

namespace Raffica.Pdf.Tests.Updating;

public sealed class UpdateInstallGuardTests : IDisposable
{
    private readonly PdfEngine _engine = new();
    private readonly List<PdfTab> _tabs = [];
    private readonly UpdateKit _kit = new();

    public void Dispose()
    {
        foreach (var tab in _tabs)
        {
            tab.Dispose();
        }

        _kit.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private PdfTab NewTab(string name, bool dirty)
    {
        var tab = new PdfTab(new ViewerSession(_engine), name + ".pdf");
        _tabs.Add(tab);
        if (dirty)
        {
            tab.MarkDirty();
        }

        return tab;
    }

    private async Task<PdfTab> PlanTabAsync()
    {
        var session = new ViewerSession(_engine);
        await session.OpenAsync(Raffica.TestSupport.TestData.Pdf("pages-marked.pdf"));
        var tab = new PdfTab(session, Raffica.TestSupport.TestData.Pdf("pages-marked.pdf"));
        _tabs.Add(tab);
        tab.Organize.Enter();
        tab.Organize.Selection.Select(0);
        Assert.True(tab.Organize.Rotate(90).IsDone);
        return tab;
    }

    // A guard that logs every question and every save, and answers from a table (tab title to choice).
    private static CloseGuard Guard(List<string> log, Dictionary<string, CloseChoice> answers, Func<string, bool>? saves = null, PlanClose? plan = null) => new(
        tab =>
        {
            log.Add("ask " + tab.Title);
            return Task.FromResult(answers[tab.Title]);
        },
        tab =>
        {
            log.Add("save " + tab.Title);
            return Task.FromResult(saves?.Invoke(tab.Title) ?? true);
        },
        plan);

    [Fact]
    public async Task EveryDirtyTabIsAskedAndCancelAnywhereKeepsAll()
    {
        // The staged update of a finished download: Cancel at the question must leave it exactly as it was.
        _kit.Release("0.2.0");
        using var flow = _kit.Flow();
        await flow.CheckNowAsync(Ct);
        await flow.DownloadAndStageAsync(Ct);
        var staged = flow.Staged!;

        var first = NewTab("first", dirty: true);
        var second = NewTab("second", dirty: true);
        var third = NewTab("third", dirty: true);
        var clean = NewTab("clean", dirty: false);
        var log = new List<string>();
        var guard = new UpdateInstallGuard(Guard(log, new()
        {
            ["first.pdf"] = CloseChoice.Save,
            ["second.pdf"] = CloseChoice.Cancel,
            ["third.pdf"] = CloseChoice.Save,
        }));
        var afterLast = 0;

        var result = await guard.AskAllTabsAsync([first, second, clean, third], () => afterLast++);

        // The first tab said Save, but nothing is written before the last answer: after the Cancel of the second no tab was saved, none closed, none asked later.
        Assert.False(result);
        Assert.Equal(["ask first.pdf", "ask second.pdf"], log);
        Assert.Equal(0, afterLast);
        Assert.All(new[] { first, second, third }, tab => Assert.True(tab.IsDirty));
        Assert.False(guard.IsAsking);

        // The staged update is untouched and installing is still possible with it.
        Assert.True(File.Exists(staged.FilePath));
        Assert.True(File.Exists(staged.RequestPath));
        Assert.Equal(UpdateStage.Ready, flow.State.Stage);
        Assert.Equal(0, _kit.Applier.Calls);
    }

    [Fact]
    public async Task SavedAnswersAreWrittenBeforeTheInstallerStarts()
    {
        _kit.Release("0.2.0");
        using var flow = _kit.Flow();
        await flow.CheckNowAsync(Ct);
        await flow.DownloadAndStageAsync(Ct);

        var first = NewTab("first", dirty: true);
        var second = NewTab("second", dirty: true);
        var third = NewTab("third", dirty: true);
        var log = new List<string>();
        _kit.Applier.OnApply = () => log.Add("installer");
        var guard = new UpdateInstallGuard(Guard(log, new()
        {
            ["first.pdf"] = CloseChoice.Save,
            ["second.pdf"] = CloseChoice.DontSave,
            ["third.pdf"] = CloseChoice.Save,
        }));

        // The window's sequence: the question, then the session (after the last answer), then the installer.
        if (await guard.AskAllTabsAsync([first, second, third], () => log.Add("session")))
        {
            await flow.InstallAsync(Ct);
        }

        // All three questions come first, then the two saves in tab order, then the session, and only then the installer.
        Assert.Equal(["ask first.pdf", "ask second.pdf", "ask third.pdf", "save first.pdf", "save third.pdf", "session", "installer"], log);
        Assert.Equal(1, _kit.Applier.Calls);
    }

    [Fact]
    public async Task AFailedSaveStopsTheUpdateBeforeTheSessionAndTheInstaller()
    {
        var first = NewTab("first", dirty: true);
        var second = NewTab("second", dirty: true);
        var log = new List<string>();
        var guard = new UpdateInstallGuard(Guard(
            log,
            new() { ["first.pdf"] = CloseChoice.Save, ["second.pdf"] = CloseChoice.Save },
            saves: title => title != "first.pdf"));
        var afterLast = 0;

        Assert.False(await guard.AskAllTabsAsync([first, second], () => afterLast++));

        // The first save failed: the second is not written after it, the session is not written, the tabs stay.
        Assert.Equal(["ask first.pdf", "ask second.pdf", "save first.pdf"], log);
        Assert.Equal(0, afterLast);
        Assert.True(first.IsDirty && second.IsDirty);
    }

    [Fact]
    public async Task CleanTabsAreNotAskedAndTheSessionIsWritten()
    {
        var log = new List<string>();
        var guard = new UpdateInstallGuard(Guard(log, []));
        var afterLast = 0;

        Assert.True(await guard.AskAllTabsAsync([NewTab("a", dirty: false), NewTab("b", dirty: false)], () => afterLast++));

        Assert.Empty(log);
        Assert.Equal(1, afterLast);
    }

    [Fact]
    public async Task AChangedPagePlanAsksItsOwnCardFirstAndIsAppliedOnlyAfterTheLastAnswer()
    {
        var planTab = await PlanTabAsync();
        var other = NewTab("other", dirty: true);
        var log = new List<string>();
        var plan = new PlanClose(
            tab =>
            {
                log.Add("plan ask");
                return Task.FromResult(CloseChoice.Save);
            },
            tab =>
            {
                log.Add("plan apply");
                return Task.FromResult(true);
            });
        var guard = new UpdateInstallGuard(Guard(log, new() { ["other.pdf"] = CloseChoice.Save }, plan: plan));

        Assert.True(await guard.AskAllTabsAsync([planTab, other]));

        // The plan asks its own question (never the generic one), and "Apply" is written after every tab has answered.
        Assert.Equal(["plan ask", "ask other.pdf", "plan apply", "save other.pdf"], log);
    }

    [Fact]
    public async Task ACancelAtThePlanQuestionAlsoKeepsEverything()
    {
        var planTab = await PlanTabAsync();
        var other = NewTab("other", dirty: true);
        var log = new List<string>();
        var plan = new PlanClose(
            tab =>
            {
                log.Add("plan ask");
                return Task.FromResult(CloseChoice.Cancel);
            },
            tab => throw new InvalidOperationException("applied"));
        var guard = new UpdateInstallGuard(Guard(log, new() { ["other.pdf"] = CloseChoice.Save }, plan: plan));

        Assert.False(await guard.AskAllTabsAsync([other, planTab]));

        Assert.Equal(["ask other.pdf", "plan ask"], log);
        Assert.False(planTab.Organize.Plan.IsIdentity);
    }

    [Fact]
    public async Task ASecondQuestionWhileOneIsOpenIsRefused()
    {
        var gate = new TaskCompletionSource<CloseChoice>();
        var guard = new UpdateInstallGuard(new CloseGuard(_ => gate.Task, _ => Task.FromResult(true)));
        var tab = NewTab("a", dirty: true);

        var running = guard.AskAllTabsAsync([tab]);
        Assert.True(guard.IsAsking);
        Assert.False(await guard.AskAllTabsAsync([tab]));

        gate.SetResult(CloseChoice.DontSave);
        Assert.True(await running);
        Assert.False(guard.IsAsking);
    }
}
