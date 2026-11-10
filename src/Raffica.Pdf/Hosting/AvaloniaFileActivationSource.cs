using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Raffica.Pdf.Hosting;

/// <summary>The files the system hands over through Avalonia's activation events (macOS: Finder, Dock, "Open With") and its reopen request. The only place that touches <see cref="IActivatableLifetime"/>.</summary>
internal sealed class AvaloniaFileActivationSource : IFileActivationSource, IReopenSource
{
    private readonly IActivatableLifetime _lifetime;
    private Action<IReadOnlyList<string>>? _handler;
    private Action? _reopened;
    private bool _subscribed;

    public AvaloniaFileActivationSource(IActivatableLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        _lifetime = lifetime;
    }

    public event Action<IReadOnlyList<string>>? FilesActivated
    {
        add
        {
            _handler += value;
            Update();
        }
        remove
        {
            _handler -= value;
            Update();
        }
    }

    public event Action? Reopened
    {
        add
        {
            _reopened += value;
            Update();
        }
        remove
        {
            _reopened -= value;
            Update();
        }
    }

    // The lifetime is listened to while somebody listens to this source.
    private void Update()
    {
        var wanted = _handler is not null || _reopened is not null;
        if (wanted == _subscribed)
        {
            return;
        }

        _subscribed = wanted;
        if (wanted)
        {
            _lifetime.Activated += OnActivated;
        }
        else
        {
            _lifetime.Activated -= OnActivated;
        }
    }

    // Files and Reopen; the other kinds are ignored.
    private void OnActivated(object? sender, ActivatedEventArgs e)
    {
        if (e.Kind == ActivationKind.Reopen)
        {
            _reopened?.Invoke();
            return;
        }

        if (e is not FileActivatedEventArgs { Kind: ActivationKind.File } files)
        {
            return;
        }

        var paths = new List<string>();
        foreach (var item in files.Files)
        {
            if (item.TryGetLocalPath() is { } path)
            {
                paths.Add(path);
            }
        }

        if (paths.Count > 0)
        {
            _handler?.Invoke(paths);
        }
    }
}
