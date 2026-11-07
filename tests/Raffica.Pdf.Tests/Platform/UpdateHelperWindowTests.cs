using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Core.Updates;
using Raffica.Pdf.Platform;
using Raffica.Pdf.Platform.Windows;
using Raffica.Pdf.Updating;
using Xunit;

namespace Raffica.Pdf.Tests.Platform;

public sealed class UpdateHelperWindowTests : IDisposable
{
    private readonly ApplierKit _kit = new();
    private readonly FakeStarter _starter = new();
    private readonly FakeRunner _runner = new();

    public void Dispose() => _kit.Dispose();

    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static T Find<T>(Window window, string name)
        where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private WindowsUpdateHelper Helper(StagedUpdate staged, int? afterProcess = null) =>
        new(new WindowsUpdateHelperSetup(staged.Directory, _kit.Config, _kit.Keys, ApplierKit.RunningVersion, _starter, _runner, () => { }) { AfterProcess = afterProcess, ProductVersionOf = _ => "0.2.0" });

    private StagedUpdate Stage()
    {
        var program = _kit.MakeProgramDirectory();
        return _kit.Stage("0.2.0", UpdateFileKinds.WindowsSetup, exePath: Path.Combine(program, "RafficaPdf.exe"), targetDirectory: program);
    }

    [AvaloniaFact]
    public async Task AFailedInstallShowsTheWordedErrorWithTryAgainAndRetriesWithAFreshCard()
    {
        PdfStrings.Culture = CultureInfo.GetCultureInfo("de");
        _runner.Outcomes.Enqueue(new ProcessOutcome(1, string.Empty));
        using var helper = Helper(Stage());
        var window = new UpdateHelperWindow(helper);
        window.Show();
        Pump();
        await helper.RunAsync();
        Pump();

        // The error is in the app's words (German here), with Try again and Close; the card is the one of the update flow in the shell theme.
        Assert.Equal(PdfStrings.UpdateProgressTitle, window.Title);
        Assert.Equal(UpdateWording.ForApplier(UpdateApplyProblem.HelperInstallFailed, PdfStrings.Culture), Find<TextBlock>(window, "PART_Failure").Text);
        Assert.True(Find<Button>(window, "PART_Retry").IsVisible);
        Assert.Equal("Erneut versuchen", Find<Button>(window, "PART_Retry").Content);
        Assert.True(Find<Button>(window, "PART_Dismiss").IsVisible);
        Assert.False(Find<Button>(window, "PART_Page").IsVisible);

        var failedCard = window.Card;
        Find<Button>(window, "PART_Retry").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        await helper.RunAsync();
        for (var i = 0; i < 50 && _runner.Runs.Count < 2; i++)
        {
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }

        Assert.NotSame(failedCard, window.Card);
        Assert.Equal(2, _runner.Runs.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void ARefusedStagingFolderShowsTheErrorWithCloseOnlyAndStartsNothing()
    {
        var staged = Stage();
        File.Delete(Path.Combine(staged.Directory, UpdateStaging.RequestFileName));
        using var helper = Helper(staged);
        var window = new UpdateHelperWindow(helper);
        window.Show();
        Pump();

        Assert.Equal(UpdateWording.ForFailure(UpdateFailure.Rejected, PdfStrings.Culture), Find<TextBlock>(window, "PART_Failure").Text);
        Assert.False(Find<Button>(window, "PART_Retry").IsVisible);
        Assert.Empty(_runner.Runs);
        Assert.Empty(_starter.Starts);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheWindowDoesNotCloseWhileTheSetupProgramRuns()
    {
        var staged = Stage();
        var release = new TaskCompletionSource();
        var entered = new TaskCompletionSource();
        using var helper = new WindowsUpdateHelper(new WindowsUpdateHelperSetup(staged.Directory, _kit.Config, _kit.Keys, ApplierKit.RunningVersion, _starter, new BlockingRunner(release.Task, entered), () => { }));
        var window = new UpdateHelperWindow(helper);
        window.Show();
        Pump();
        var running = helper.RunAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal(UpdateStage.Installing, helper.State.Stage);
        window.Close();
        Assert.True(window.IsVisible);
        Assert.False(Find<Button>(window, "PART_Cancel").IsEnabled);

        release.SetResult();
        await running;
        window.Close();
    }

    private sealed class BlockingRunner(Task release, TaskCompletionSource entered) : IProcessRunner
    {
        public async Task<ProcessOutcome?> RunAsync(string file, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken cancellationToken)
        {
            entered.TrySetResult();
            await release;
            return new ProcessOutcome(1, string.Empty);
        }
    }
}
