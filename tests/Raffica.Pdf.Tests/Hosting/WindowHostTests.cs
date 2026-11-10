using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Raffica.Pdf.Hosting;
using Xunit;

namespace Raffica.Pdf.Tests.Hosting;

public sealed class WindowHostTests
{
    private sealed class FakeWindow : IHostedWindow
    {
        public bool IsClosing { get; set; }

        public bool QuitRequested { get; set; }

        public List<IReadOnlyList<string>> Handovers { get; } = [];

        public int Fronts { get; private set; }

        public IReadOnlyList<string> Initial { get; init; } = [];

        public event EventHandler? Closed;

        public void Handover(IReadOnlyList<string> paths) => Handovers.Add(paths);

        public void BringToFront() => Fronts++;

        public List<string> Undelivered { get; } = [];

        public IReadOnlyList<string> TakeUndelivered()
        {
            var taken = Undelivered.ToList();
            Undelivered.Clear();
            return taken;
        }

        public void Close() => Closed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Rig
    {
        public Rig() => Host = new WindowHost(paths =>
        {
            var window = new FakeWindow { Initial = paths };
            Made.Add(window);
            return window;
        }, () => Shutdowns++, Dispatcher.UIThread);

        public WindowHost Host { get; }

        public List<FakeWindow> Made { get; } = [];

        public int Shutdowns { get; private set; }
    }

    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public void TheUpdateEndsTheHostedAppThroughQuitSoNoWindowIsMadeAfterwards()
    {
        var rig = new Rig();
        var first = new FakeWindow();
        rig.Host.Adopt(first);
        var ended = 0;
        var end = App.EndAction(rig.Host, () => ended++);

        end();
        Assert.Equal(1, rig.Shutdowns);
        Assert.Equal(0, ended);

        // A file that was waiting and a Dock click arrive while the windows go: nothing is made.
        first.Undelivered.Add("late.pdf");
        first.Close();
        rig.Host.OpenFiles(["a.pdf"]);
        rig.Host.Reopen();
        Pump();
        Assert.Empty(rig.Made);
        Assert.Equal(1, rig.Shutdowns);
    }

    [AvaloniaFact]
    public void WithoutAHostTheUpdateEndsWithThePlainShutdown()
    {
        var ended = 0;
        App.EndAction(null, () => ended++)();
        Assert.Equal(1, ended);
    }

    [AvaloniaFact]
    public void WithNoWindowOpenFilesMakesOneWithThosePaths()
    {
        var rig = new Rig();

        rig.Host.OpenFiles(["a.pdf", "b.pdf"]);
        Pump();

        var window = Assert.Single(rig.Made);
        Assert.Equal(["a.pdf", "b.pdf"], window.Initial);
        Assert.Same(window, rig.Host.Current);
    }

    [AvaloniaFact]
    public void WithAWindowOpenFilesHandsOver()
    {
        var rig = new Rig();
        var window = new FakeWindow();
        rig.Host.Adopt(window);

        rig.Host.OpenFiles(["a.pdf"]);
        Pump();

        Assert.Equal(["a.pdf"], Assert.Single(window.Handovers));
        Assert.Empty(rig.Made);
    }

    [AvaloniaFact]
    public void FilesThatArriveWhileTheWindowIsClosingOpenInTheNextWindow()
    {
        var rig = new Rig();
        var window = new FakeWindow { IsClosing = true };
        rig.Host.Adopt(window);

        rig.Host.OpenFiles(["a.pdf"]);
        rig.Host.OpenFiles(["b.pdf"]);
        Pump();
        Assert.Empty(window.Handovers);
        Assert.Empty(rig.Made);

        window.Close();
        Pump();

        var next = Assert.Single(rig.Made);
        Assert.Equal(["a.pdf", "b.pdf"], next.Initial);
        Assert.Same(next, rig.Host.Current);
        Assert.Equal(0, rig.Shutdowns);
    }

    [AvaloniaFact]
    public void ReopenWithNoWindowMakesAnEmptyOne()
    {
        var rig = new Rig();

        rig.Host.Reopen();
        Pump();

        Assert.Empty(Assert.Single(rig.Made).Initial);
    }

    [AvaloniaFact]
    public void ReopenWithAWindowBringsItForwardAndMakesNoSecond()
    {
        var rig = new Rig();
        var window = new FakeWindow();
        rig.Host.Adopt(window);

        rig.Host.Reopen();
        Pump();

        Assert.Equal(1, window.Fronts);
        Assert.Empty(rig.Made);
    }

    [AvaloniaFact]
    public void ACloseThatWasAQuitCallsShutdownAndMakesNothing()
    {
        var rig = new Rig();
        var window = new FakeWindow { IsClosing = true, QuitRequested = true };
        rig.Host.Adopt(window);
        rig.Host.OpenFiles(["a.pdf"]);
        Pump();

        window.Close();
        Pump();

        Assert.Equal(1, rig.Shutdowns);
        Assert.Empty(rig.Made);
        Assert.Null(rig.Host.Current);
    }

    [AvaloniaFact]
    public void ACloseWithoutQuitLeavesTheHostAliveWithNoWindow()
    {
        var rig = new Rig();
        var window = new FakeWindow();
        rig.Host.Adopt(window);

        window.Close();
        Pump();

        Assert.Null(rig.Host.Current);
        Assert.Equal(0, rig.Shutdowns);
        Assert.Empty(rig.Made);
        rig.Host.OpenFiles(["a.pdf"]);
        Pump();
        Assert.Single(rig.Made);
    }

    [AvaloniaFact]
    public void AfterQuitNothingIsMade()
    {
        var rig = new Rig();

        rig.Host.Quit();
        rig.Host.Quit();
        rig.Host.OpenFiles(["a.pdf"]);
        rig.Host.Reopen();
        Pump();

        Assert.Empty(rig.Made);
        Assert.Equal(1, rig.Shutdowns);
    }

    [AvaloniaFact]
    public async Task OpenFilesFromAPoolThreadRunsOnTheDispatcher()
    {
        var rig = new Rig();
        var madeOn = -1;
        var uiThread = Environment.CurrentManagedThreadId;
        var host = new WindowHost(paths =>
        {
            madeOn = Environment.CurrentManagedThreadId;
            return new FakeWindow { Initial = paths };
        }, () => { }, Dispatcher.UIThread);

        await Task.Run(() => host.OpenFiles(["a.pdf"]), TestContext.Current.CancellationToken);
        Assert.Equal(-1, madeOn);
        Pump();

        Assert.Equal(uiThread, madeOn);
        Assert.NotNull(host.Current);
        Assert.Empty(rig.Made);
    }

    [AvaloniaFact]
    public void CurrentChangedIsRaisedOnAdoptOnCreateAndOnClose()
    {
        var rig = new Rig();
        var raised = 0;
        rig.Host.CurrentChanged += () => raised++;
        var window = new FakeWindow();

        rig.Host.Adopt(window);
        Assert.Equal(1, raised);

        window.Close();
        Assert.Equal(2, raised);

        rig.Host.Reopen();
        Pump();
        Assert.Equal(3, raised);
    }

    [AvaloniaFact]
    public void FilesTheClosedWindowNeverOpenedGoToTheNextWindow()
    {
        var rig = new Rig();
        var window = new FakeWindow();
        rig.Host.Adopt(window);
        window.Undelivered.AddRange(["queued.pdf"]);
        rig.Host.OpenFiles(["late.pdf"]);
        window.IsClosing = true;
        Pump();

        window.Close();
        Pump();

        Assert.Equal(["late.pdf", "queued.pdf"], Assert.Single(rig.Made).Initial);
        Assert.Empty(window.Undelivered);
    }

    [AvaloniaFact]
    public void AQuitDiscardsTheUndeliveredFiles()
    {
        var rig = new Rig();
        var window = new FakeWindow { IsClosing = true, QuitRequested = true };
        window.Undelivered.Add("queued.pdf");
        rig.Host.Adopt(window);

        window.Close();
        Pump();

        Assert.Empty(rig.Made);
        Assert.Equal(1, rig.Shutdowns);
    }

    [AvaloniaFact]
    public void ReopenWhileTheWindowIsClosingMakesAnEmptyWindowAfterTheClose()
    {
        var rig = new Rig();
        var window = new FakeWindow { IsClosing = true };
        rig.Host.Adopt(window);

        rig.Host.Reopen();
        Pump();
        Assert.Empty(rig.Made);
        Assert.Equal(0, window.Fronts);

        window.Close();
        Pump();

        Assert.Empty(Assert.Single(rig.Made).Initial);
    }

    [AvaloniaFact]
    public void AFactoryThatThrowsLosesNoWaitingFile()
    {
        var calls = 0;
        var seen = new List<IReadOnlyList<string>>();
        var host = new WindowHost(paths =>
        {
            seen.Add(paths);
            if (calls++ == 0)
            {
                throw new InvalidOperationException("no window");
            }

            return new FakeWindow { Initial = paths };
        }, () => { }, Dispatcher.UIThread);
        var window = new FakeWindow { IsClosing = true };
        host.Adopt(window);
        host.OpenFiles(["a.pdf"]);
        Pump();

        Assert.Throws<InvalidOperationException>(window.Close);
        host.Reopen();
        Pump();

        Assert.Equal(["a.pdf"], seen[^1]);
        Assert.NotNull(host.Current);
    }
}
