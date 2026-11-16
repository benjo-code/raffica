using Avalonia.Threading;

namespace Raffica.Write.Commands;

/// <summary>
/// Brings the ribbon's display of the formatting (the pressed switches and the values of the pickers) up to date once typing and selecting pause. Every request restarts the delay; nothing exists
/// before the first request (no timer, no work), and a request is a restart of a timer, never a read of the document: the read happens in the refresh, on the UI thread, when the delay is over.
/// </summary>
internal sealed class FormatDisplayRefresh(Action refresh, TimeSpan delay) : IDisposable
{
    private DispatcherTimer? _timer;
    private bool _disposed;

    /// <summary>How long the typing and selecting must pause before the display is read again.</summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(100);

    /// <summary>Whether the timer has been made (it is made by the first request).</summary>
    public bool TimerCreated => _timer is not null;

    /// <summary>Whether a refresh is waiting for the delay.</summary>
    public bool IsPending => _timer?.IsEnabled == true;

    /// <summary>Asks for a refresh after the delay; a request while one is waiting moves it back.</summary>
    public void Request()
    {
        if (_disposed)
        {
            return;
        }

        if (_timer is null)
        {
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = delay };
            _timer.Tick += (_, _) =>
            {
                _timer?.Stop();
                if (!_disposed)
                {
                    refresh();
                }
            };
        }

        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Drops a waiting refresh (nothing is read); the next request starts it again.</summary>
    public void Cancel() => _timer?.Stop();

    /// <summary>Runs a waiting refresh now (the tests do not wait for the delay).</summary>
    public void Flush()
    {
        if (!_disposed && _timer is { IsEnabled: true })
        {
            _timer.Stop();
            refresh();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer?.Stop();
        _timer = null;
    }
}
