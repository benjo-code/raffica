using System.Collections.Specialized;
using Raffica.Core.Viewing;

namespace Raffica.Pdf.Documents;

/// <summary>
/// The open documents of the window over Core's <see cref="TabList{T}"/>. Only the active tab keeps its render cache at the full budget:
/// a change of the active tab tells the sessions through <see cref="ViewerSession.SetActive"/>.
/// </summary>
internal sealed class Workspace
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly TabList<PdfTab> _tabs = [];
    private readonly CloseGuard _guard;
    private PdfTab? _shown;

    public Workspace(CloseGuard guard)
    {
        ArgumentNullException.ThrowIfNull(guard);
        _guard = guard;
        _tabs.ActiveChanged += OnActiveChanged;
        _tabs.CollectionChanged += (sender, e) => TabsChanged?.Invoke(sender, e);
    }

    /// <summary>Raised after a tab was added or removed (the list is complete and <see cref="Active"/> is already new).</summary>
    public event NotifyCollectionChangedEventHandler? TabsChanged;

    /// <summary>Raised after <see cref="Active"/> changed.</summary>
    public event Action? ActiveChanged;

    public IReadOnlyList<PdfTab> Tabs => _tabs;

    public PdfTab? Active => _tabs.Active;

    /// <summary>The tab of a file (compared by full path, ignoring the case on Windows), or null.</summary>
    public PdfTab? Find(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        return _tabs.FirstOrDefault(t => string.Equals(t.Path, full, PathComparison));
    }

    /// <summary>Adds a tab for an opened (or opening) session next to the active tab and makes it active.</summary>
    public PdfTab Add(ViewerSession session, string path)
    {
        var tab = new PdfTab(session, path);
        _tabs.Add(tab);
        return tab;
    }

    /// <summary>Asks the guard; when the tab may close, disposes its session and removes it.</summary>
    /// <returns><see langword="true"/> when the tab is gone, <see langword="false"/> when it stays.</returns>
    public async Task<bool> CloseAsync(PdfTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (!await _guard.CanCloseAsync(tab))
        {
            return false;
        }

        tab.Dispose();
        _tabs.Close(tab);
        return true;
    }

    public void Select(PdfTab tab) => _tabs.Activate(tab);

    public void SelectNext() => _tabs.ActivateNext();

    public void SelectPrevious() => _tabs.ActivatePrevious();

    private void OnActiveChanged()
    {
        var previous = _shown;
        _shown = _tabs.Active;
        previous?.Session.SetActive(false);
        _shown?.Session.SetActive(true);
        ActiveChanged?.Invoke();
    }
}
