using System.Collections.Specialized;
using Raffica.Core.Saving;
using Raffica.Core.Viewing;

namespace Raffica.Write.Documents;

/// <summary>
/// The open documents of the window over Core's <see cref="TabList{T}"/>. <see cref="Close"/> closes without asking: the question about unsaved changes is the window's
/// (<c>WriteWindow.CloseTabAsync</c> and the close of the window, through <see cref="CloseGuard"/>). UI thread only.
/// </summary>
internal sealed class WriteWorkspace
{
    private readonly TabList<WriteTab> _tabs = [];
    private bool _sealed;

    public WriteWorkspace()
    {
        _tabs.ActiveChanged += () => ActiveChanged?.Invoke();
        _tabs.CollectionChanged += (sender, e) => TabsChanged?.Invoke(sender, e);
    }

    /// <summary>Raised after a tab was added or removed (the list is complete and <see cref="Active"/> is already new).</summary>
    public event NotifyCollectionChangedEventHandler? TabsChanged;

    /// <summary>Raised after <see cref="Active"/> changed.</summary>
    public event Action? ActiveChanged;

    public IReadOnlyList<WriteTab> Tabs => _tabs;

    public WriteTab? Active => _tabs.Active;

    /// <summary>The tab of a file (compared by full path with the rule of PathRules), or null.</summary>
    public WriteTab? Find(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        return _tabs.FirstOrDefault(t => PathRules.AreEqual(t.Path, full));
    }

    /// <summary>True once the window is closing: no tab is added any more (an open that finishes late must not leave a tab nobody disposes).</summary>
    public bool IsSealed => _sealed;

    /// <summary>Refuses every later <see cref="TryAdd"/>; the window calls it first thing when it closes.</summary>
    public void Seal() => _sealed = true;

    /// <summary>Adds an opened tab next to the active one and makes it active; false (and nothing added) once the workspace is sealed.</summary>
    public bool TryAdd(WriteTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (_sealed)
        {
            return false;
        }

        _tabs.Add(tab);
        return true;
    }

    /// <summary>Removes the tab and disposes it (its layout is cancelled and its file and pictures are let go).</summary>
    public void Close(WriteTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (_tabs.Contains(tab))
        {
            _tabs.Close(tab);
            tab.Dispose();
        }
    }

    public void Select(WriteTab tab) => _tabs.Activate(tab);

    public void SelectNext() => _tabs.ActivateNext();

    public void SelectPrevious() => _tabs.ActivatePrevious();
}
