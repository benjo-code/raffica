using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Raffica.Hosting;
using Xunit;

namespace Raffica.Pdf.Tests.Hosting;

public sealed class AppEndActionTests
{
    private sealed class FakeWindow : IHostedWindow
    {
        public bool IsClosing { get; set; }

        public bool QuitRequested { get; set; }

        public IReadOnlyList<string> Initial { get; init; } = [];

        public List<string> Undelivered { get; } = [];

        public event EventHandler? Closed;

        public void Handover(IReadOnlyList<string> paths)
        {
        }

        public void BringToFront()
        {
        }

        public IReadOnlyList<string> TakeUndelivered()
        {
            var taken = Undelivered.ToList();
            Undelivered.Clear();
            return taken;
        }

        public void Close() => Closed?.Invoke(this, EventArgs.Empty);
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
        var made = new List<FakeWindow>();
        var shutdowns = 0;
        var host = new WindowHost(paths =>
        {
            var window = new FakeWindow { Initial = paths };
            made.Add(window);
            return window;
        }, () => shutdowns++, Dispatcher.UIThread);
        var first = new FakeWindow();
        host.Adopt(first);
        var ended = 0;
        var end = App.EndAction(host, () => ended++);

        end();
        Assert.Equal(1, shutdowns);
        Assert.Equal(0, ended);

        // A file that was waiting and a Dock click arrive while the windows go: nothing is made.
        first.Undelivered.Add("late.pdf");
        first.Close();
        host.OpenFiles(["a.pdf"]);
        host.Reopen();
        Pump();
        Assert.Empty(made);
        Assert.Equal(1, shutdowns);
    }

    [AvaloniaFact]
    public void WithoutAHostTheUpdateEndsWithThePlainShutdown()
    {
        var ended = 0;
        App.EndAction(null, () => ended++)();
        Assert.Equal(1, ended);
    }
}
