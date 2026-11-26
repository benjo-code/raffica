using Avalonia.Threading;

namespace Raffica.Write.Editing;

/// <summary>The beat of the field refresh: one timer that fires once, a fixed delay after it was last started (a seam: the tests fire it by hand).</summary>
internal interface IFieldClock
{
    /// <summary>Makes a stopped timer that calls <paramref name="tick"/> on the thread that owns the clock (the UI thread).</summary>
    IFieldTimer CreateTimer(Action tick);
}

/// <summary>A one-shot timer that is started again by every <see cref="Restart"/>.</summary>
internal interface IFieldTimer : IDisposable
{
    /// <summary>True from <see cref="Restart"/> until the tick, <see cref="Stop"/> or <see cref="IDisposable.Dispose"/>.</summary>
    bool IsArmed { get; }

    /// <summary>Starts (or starts over) the countdown to one tick.</summary>
    void Restart(TimeSpan delay);

    /// <summary>Cancels the countdown.</summary>
    void Stop();
}

/// <summary>
/// The clock of the app: a <see cref="DispatcherTimer"/> on the dispatcher captured when the clock is made (construct it on the UI thread; the tick never reads <c>Dispatcher.UIThread</c> on a pool thread). The tick runs at
/// Background priority, so typing and painting go first.
/// </summary>
internal sealed class DispatcherFieldClock : IFieldClock
{
    private readonly Dispatcher _dispatcher = Dispatcher.UIThread;

    public IFieldTimer CreateTimer(Action tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        return new Timer(_dispatcher, tick);
    }

    private sealed class Timer : IFieldTimer
    {
        private readonly DispatcherTimer _timer;
        private readonly Action _tick;

        public Timer(Dispatcher dispatcher, Action tick)
        {
            _tick = tick;
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background, dispatcher, (_, _) => OnTick());
        }

        public bool IsArmed => _timer.IsEnabled;

        public void Restart(TimeSpan delay)
        {
            _timer.Stop();
            _timer.Interval = delay;
            _timer.Start();
        }

        public void Stop() => _timer.Stop();

        public void Dispose() => _timer.Stop();

        private void OnTick()
        {
            _timer.Stop();
            _tick();
        }
    }
}
