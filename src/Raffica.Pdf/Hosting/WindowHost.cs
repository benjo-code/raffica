using Avalonia.Threading;

namespace Raffica.Pdf.Hosting;

/// <summary>What the host needs of a window (macOS keeps the app alive when the last window closes, so something outside the window decides what happens next).</summary>
internal interface IHostedWindow
{
    /// <summary>True once the window is confirmed to close (every close question was answered) and has not closed yet: a hand-over now would be lost with it.</summary>
    bool IsClosing { get; }

    /// <summary>True while a close that came from a quit (Cmd+Q, a log out) is under way; cleared when a question is answered Cancel.</summary>
    bool QuitRequested { get; }

    /// <summary>Opens the files in this window (the files are checked off the UI thread; call it on the UI thread).</summary>
    void Handover(IReadOnlyList<string> paths);

    void BringToFront();

    event EventHandler? Closed;
}

/// <summary>The system's "reopen" request (a click on the Dock icon of a running app that has no window).</summary>
internal interface IReopenSource
{
    /// <summary>Raised on any thread.</summary>
    event Action? Reopened;
}

/// <summary>
/// Owns "which window is current" for an app that outlives its last window (macOS). Files from Finder or a later launch go to the current window, or to a new
/// one when there is none; a file that arrives while the last window is closing waits and opens in the window made when it has closed. Quit ends the app
/// once the window is closed; a plain close leaves the host alive. <see cref="OpenFiles"/> and <see cref="Reopen"/> may be called from any thread: they post to the
/// dispatcher captured at construction (the constructor runs on the UI thread, nothing here reads <c>Dispatcher.UIThread</c>).
/// </summary>
internal sealed class WindowHost
{
    private readonly Func<IReadOnlyList<string>, IHostedWindow> _create;
    private readonly Action _shutdown;
    private readonly Dispatcher _dispatcher;
    private readonly List<string> _waiting = [];
    private bool _quit;

    public WindowHost(Func<IReadOnlyList<string>, IHostedWindow> create, Action shutdown, Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(shutdown);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _create = create;
        _shutdown = shutdown;
        _dispatcher = dispatcher;
    }

    /// <summary>The window that receives files; null while there is none.</summary>
    public IHostedWindow? Current { get; private set; }

    /// <summary>Raised on the dispatcher thread whenever <see cref="Current"/> changes.</summary>
    public event Action? CurrentChanged;

    /// <summary>Takes over the window the app made at its start.</summary>
    public void Adopt(IHostedWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        Take(window);
    }

    /// <summary>Opens the files in the current window, in a new one when there is none, or in the next one when the current window is closing.</summary>
    public void OpenFiles(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            return;
        }

        var copy = paths.ToList();
        _dispatcher.Post(() => OpenFilesCore(copy));
    }

    /// <summary>A click on the Dock icon: the current window comes forward, or an empty one is made.</summary>
    public void Reopen() => _dispatcher.Post(ReopenCore);

    /// <summary>Ends the app (once); nothing is made afterwards.</summary>
    public void Quit()
    {
        if (_quit)
        {
            return;
        }

        _quit = true;
        _waiting.Clear();
        _shutdown();
    }

    private void OpenFilesCore(IReadOnlyList<string> paths)
    {
        if (_quit)
        {
            return;
        }

        if (Current is not { } window)
        {
            Take(_create(paths));
        }
        else if (window.IsClosing)
        {
            _waiting.AddRange(paths);
        }
        else
        {
            window.Handover(paths);
        }
    }

    private void ReopenCore()
    {
        if (_quit)
        {
            return;
        }

        if (Current is { } window)
        {
            window.BringToFront();
        }
        else
        {
            Take(_create([]));
        }
    }

    private void Take(IHostedWindow window)
    {
        Current = window;
        window.Closed += OnClosed;
        CurrentChanged?.Invoke();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (sender is not IHostedWindow window)
        {
            return;
        }

        window.Closed -= OnClosed;
        var quit = window.QuitRequested;
        if (ReferenceEquals(Current, window))
        {
            Current = null;
            CurrentChanged?.Invoke();
        }

        if (quit)
        {
            Quit();
            return;
        }

        if (_waiting.Count > 0 && !_quit && Current is null)
        {
            var paths = _waiting.ToList();
            _waiting.Clear();
            Take(_create(paths));
        }
    }
}
