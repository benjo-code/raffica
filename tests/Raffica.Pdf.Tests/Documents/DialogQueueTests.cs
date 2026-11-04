using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Raffica.Pdf.Dialogs;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Documents;

public sealed class DialogQueueTests
{
    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static (Window Window, DialogHost Host) Create()
    {
        var host = new DialogHost { Content = new Border() };
        var window = new Window { Content = host, Width = 600, Height = 400 };
        window.Show();
        return (window, host);
    }

    private static Dialog Card(string title, Action? onCreate = null)
    {
        onCreate?.Invoke();
        return new Dialog { Title = title, Content = new TextBlock { Text = title } };
    }

    [AvaloniaFact]
    public void ACardShowsAtOnceWhenTheHostIsFree()
    {
        var (window, host) = Create();
        var queue = new DialogQueue(host);
        var task = queue.ShowAsync<bool>(done => Card("one"));
        Assert.Equal("one", host.ActiveDialog?.Title);
        Assert.False(task.IsCompleted);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ACardQueuedBehindAnotherDialogAppearsWhenThatDialogCloses()
    {
        var (window, host) = Create();
        var queue = new DialogQueue(host);
        host.Show(Card("unsaved"));

        Action<string>? finish = null;
        var task = queue.ShowAsync<string>(done =>
        {
            finish = done;
            return Card("password");
        });
        Assert.Equal("unsaved", host.ActiveDialog?.Title);

        host.Close();
        Pump();

        Assert.Equal("password", host.ActiveDialog?.Title);
        finish!("secret");
        Assert.Equal("secret", await task);
        window.Close();
    }

    [AvaloniaFact]
    public async Task QueuedCardsAppearInOrder()
    {
        var (window, host) = Create();
        var queue = new DialogQueue(host);
        host.Show(Card("first"));
        var finishers = new Dictionary<string, Action<int>>();
        var a = queue.ShowAsync<int>(done =>
        {
            finishers["a"] = done;
            return Card("a");
        });
        var b = queue.ShowAsync<int>(done =>
        {
            finishers["b"] = done;
            return Card("b");
        });

        host.Close();
        Pump();
        Assert.Equal("a", host.ActiveDialog?.Title);
        Assert.False(b.IsCompleted);

        finishers["a"](1);
        Pump();
        Assert.Equal(1, await a);
        Assert.Equal("b", host.ActiveDialog?.Title);

        finishers["b"](2);
        Pump();
        Assert.Equal(2, await b);
        Assert.Null(host.ActiveDialog);
        window.Close();
    }

    [AvaloniaFact]
    public async Task ACardThatCannotBeCreatedFailsItsTaskAndTheQueueGoesOn()
    {
        var (window, host) = Create();
        var queue = new DialogQueue(host);
        host.Show(Card("first"));
        var bad = queue.ShowAsync<int>(_ => throw new InvalidOperationException("boom"));
        var good = queue.ShowAsync<int>(done => Card("good"));

        host.Close();
        Pump();

        await Assert.ThrowsAsync<InvalidOperationException>(() => bad);
        Assert.Equal("good", host.ActiveDialog?.Title);
        window.Close();
    }
}
