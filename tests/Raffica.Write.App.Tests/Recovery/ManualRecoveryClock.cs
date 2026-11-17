using Raffica.Write.Recovery;

namespace Raffica.WriteApp.Tests.Recovery;

/// <summary>A clock the test ticks by hand.</summary>
internal sealed class ManualRecoveryClock : IRecoveryClock
{
    private Action? _tick;

    /// <summary>How many times <see cref="Every"/> was called.</summary>
    public int Started { get; private set; }

    /// <summary>The interval of the last call.</summary>
    public TimeSpan Interval { get; private set; }

    /// <summary>Whether the handle was disposed.</summary>
    public bool Stopped { get; private set; }

    public IDisposable Every(TimeSpan interval, Action tick)
    {
        Started++;
        Interval = interval;
        _tick = tick;
        return new Handle(this);
    }

    /// <summary>One beat; nothing after the clock was stopped.</summary>
    public void Fire()
    {
        if (!Stopped)
        {
            _tick?.Invoke();
        }
    }

    private sealed class Handle(ManualRecoveryClock owner) : IDisposable
    {
        public void Dispose() => owner.Stopped = true;
    }
}
