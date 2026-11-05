using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Raffica.Core.Pdf;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Organizing;
using Raffica.Pdf.Tests.Printing;
using Xunit;

namespace Raffica.Pdf.Tests.Organizing;

/// <summary>The job of the file flows: the pool thread, the exception of the work, a card that cannot be shown, the cancel, and the refusals between jobs.</summary>
public sealed class JobRunnerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [AvaloniaFact]
    public async Task TheWorkRunsOnAPoolThreadAndItsResultIsReturned()
    {
        using var host = new PrintHost();
        var tab = await host.OpenAsync("pages-marked.pdf");
        var runner = new JobRunner(new DialogQueue(host.Host));

        var onPool = await runner.RunAsync(tab, "t", "{0}", (_, _) => Task.FromResult(Thread.CurrentThread.IsThreadPoolThread));

        Assert.True(onPool);
    }

    [AvaloniaFact]
    public async Task TheExceptionOfTheWorkAlwaysReachesTheCaller()
    {
        using var host = new PrintHost();
        var tab = await host.OpenAsync("pages-marked.pdf");
        var runner = new JobRunner(new DialogQueue(host.Host));

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync<int>(tab, "t", "{0}", (_, _) => throw new InvalidOperationException("boom")));

        // The same with a job card that cannot be made: the exception of the work is not replaced by the one of the dialog.
        runner.JobDialog = _ => throw new InvalidOperationException("no dialog");
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync<int>(tab, "t", "{0}", async (_, _) =>
        {
            await Task.Delay(50, Ct);
            throw new InvalidOperationException("boom");
        }));
    }

    [AvaloniaFact]
    public async Task ADialogThatCannotBeMadeNeverReplacesTheResultOfTheWork()
    {
        using var host = new PrintHost();
        var tab = await host.OpenAsync("pages-marked.pdf");
        var runner = new JobRunner(new DialogQueue(host.Host)) { JobDialog = _ => throw new InvalidOperationException("no dialog") };

        var result = await runner.RunAsync(tab, "t", "{0}", async (_, _) =>
        {
            await Task.Delay(50, Ct);
            return 42;
        });

        Assert.Equal(42, result);
    }

    [AvaloniaFact]
    public async Task CancelAndTheTabDisposingCancelTheToken()
    {
        using var host = new PrintHost();
        var tab = await host.OpenAsync("pages-marked.pdf");
        var runner = new JobRunner(new DialogQueue(host.Host));

        var first = runner.RunAsync(tab, "t", "{0}", async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        });
        await PrintHost.UntilAsync(() => host.Host.ActiveDialog is not null);
        runner.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        var second = runner.RunAsync(tab, "t", "{0}", async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        });
        await PrintHost.UntilAsync(() => host.Host.ActiveDialog is not null);
        tab.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
    }

    [AvaloniaFact]
    public async Task AnApplyIsRefusedWhileAnotherFileJobRuns()
    {
        using var rig = await ApplyRig.OpenAsync();
        rig.Session.Selection.Set(6, [0]);
        Assert.True(rig.Session.Rotate(90).IsDone);
        var flow = new ApplyFlow(new DialogQueue(rig.Host), _ => Task.FromResult(true), _ => Task.FromResult<string?>(null), () => new PdfEngine(), _ => 0, (_, _) => { }, otherJob: () => true);

        var run = flow.RunAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_Ok");

        Assert.Contains(ApplyRig.TextsOf(card), t => t == PdfStrings.FileJobBusy);
        ApplyRig.Click(card, "PART_Ok");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.False(flow.IsRunning);
    }
}
