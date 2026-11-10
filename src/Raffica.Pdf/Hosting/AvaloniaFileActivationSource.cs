using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Raffica.Pdf.Hosting;

/// <summary>The files the system hands over through Avalonia's activation events (macOS: Finder, Dock, "Open With"). The only place that touches <see cref="IActivatableLifetime"/>.</summary>
internal sealed class AvaloniaFileActivationSource : IFileActivationSource
{
    private readonly IActivatableLifetime _lifetime;
    private Action<IReadOnlyList<string>>? _handler;

    public AvaloniaFileActivationSource(IActivatableLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        _lifetime = lifetime;
    }

    public event Action<IReadOnlyList<string>>? FilesActivated
    {
        add
        {
            var first = _handler is null;
            _handler += value;
            if (first)
            {
                _lifetime.Activated += OnActivated;
            }
        }
        remove
        {
            _handler -= value;
            if (_handler is null)
            {
                _lifetime.Activated -= OnActivated;
            }
        }
    }

    // Only files: the other kinds (Reopen is milestone 3.2) are ignored.
    private void OnActivated(object? sender, ActivatedEventArgs e)
    {
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
