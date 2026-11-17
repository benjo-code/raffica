using Avalonia.Threading;

namespace Raffica.Write.Recovery;

/// <summary>The beat of the autosave: calls <c>tick</c> every interval until the result is disposed (a seam: the tests tick by hand).</summary>
internal interface IRecoveryClock
{
    /// <summary>Starts a repeating tick; the tick runs on the thread that owns the clock (the UI thread). Disposing the result stops it.</summary>
    IDisposable Every(TimeSpan interval, Action tick);
}

/// <summary>
/// The clock of the app: a <see cref="DispatcherTimer"/> on the dispatcher captured when the clock is made (construct it on the UI thread; the timer's tick never reads
/// <c>Dispatcher.UIThread</c> on a pool thread). The tick runs at Background priority, so typing and painting go first.
/// </summary>
internal sealed class DispatcherRecoveryClock : IRecoveryClock
{
    private readonly Dispatcher _dispatcher = Dispatcher.UIThread;

    public IDisposable Every(TimeSpan interval, Action tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        var timer = new DispatcherTimer(interval, DispatcherPriority.Background, _dispatcher, (_, _) => tick());
        timer.Start();
        return new Stopper(timer);
    }

    private sealed class Stopper(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}
