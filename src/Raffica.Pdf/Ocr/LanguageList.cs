using Raffica.Core.Ocr;

namespace Raffica.Pdf.Ocr;

/// <summary>What happened to the choice of a language.</summary>
internal enum LanguageChoice
{
    /// <summary>The choice was taken.</summary>
    Done,

    /// <summary>More than <see cref="OcrLanguages.MaxLanguages"/> languages: refused, the selection stays as it was.</summary>
    TooMany,

    /// <summary>There is no file of that name: refused.</summary>
    NotInstalled,
}

/// <summary>
/// The languages of a recognition: the installed ones (with where each comes from) and the selection. The selection starts from the stored
/// choice, as far as it is installed, else from the default (the interface language and English, only as far as they are installed); its order
/// is the order of choosing, which is the order of priority of the engine. More than <see cref="OcrLanguages.SlowFrom"/> is allowed and
/// <see cref="IsSlow"/> says so; more than <see cref="OcrLanguages.MaxLanguages"/> is refused. Nothing here touches the disk: the caller reads the folders.
/// </summary>
internal sealed class LanguageList
{
    private readonly string _uiLanguage;
    private readonly List<string> _selected = [];
    private IReadOnlyList<TessdataLanguage> _installed = [];

    /// <param name="installed">The installed languages (<see cref="TessdataStore.List"/>).</param>
    /// <param name="uiLanguage">The two-letter code of the interface language.</param>
    /// <param name="stored">The choice of the last run (<see cref="Raffica.Core.Settings.AppSettings.OcrLanguages"/>), or null.</param>
    public LanguageList(IReadOnlyList<TessdataLanguage> installed, string uiLanguage, string? stored)
    {
        ArgumentNullException.ThrowIfNull(installed);
        ArgumentNullException.ThrowIfNull(uiLanguage);
        _uiLanguage = uiLanguage;
        _installed = installed;
        var names = OcrLanguages.Normalize(stored) is { } normalized ? OcrLanguages.Split(normalized).Where(IsInstalled).ToList() : [];
        _selected.AddRange(names.Count > 0 ? names : DefaultNames());
    }

    /// <summary>Gets the installed languages, sorted by name; for a name that has both, the file that was added by the user.</summary>
    public IReadOnlyList<TessdataLanguage> Installed => _installed;

    /// <summary>Gets the chosen names in the order of their priority.</summary>
    public IReadOnlyList<string> Selected => _selected;

    /// <summary>Gets the selection a run starts with when nothing was chosen before (empty when nothing is installed).</summary>
    public string Default => OcrLanguages.DefaultFor(_uiLanguage, [.. _installed.Select(l => l.Name)]);

    /// <summary>Gets the selection as the engine wants it (<c>deu+eng</c>), or null when nothing is chosen.</summary>
    public string? Selection => _selected.Count == 0 ? null : string.Join('+', _selected);

    /// <summary>Gets whether the selection has more languages than a run does well: slower and less accurate.</summary>
    public bool IsSlow => Selection is { } selection && OcrLanguages.IsSlow(selection);

    /// <summary>Gets whether the name is in the selection.</summary>
    public bool IsSelected(string name) => _selected.Contains(name, StringComparer.Ordinal);

    /// <summary>Chooses a language or drops it from the choice.</summary>
    public LanguageChoice Select(string name, bool on)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!on)
        {
            _selected.Remove(name);
            return LanguageChoice.Done;
        }

        if (!IsInstalled(name))
        {
            return LanguageChoice.NotInstalled;
        }

        if (IsSelected(name))
        {
            return LanguageChoice.Done;
        }

        if (_selected.Count >= OcrLanguages.MaxLanguages)
        {
            return LanguageChoice.TooMany;
        }

        _selected.Add(name);
        return LanguageChoice.Done;
    }

    /// <summary>Replaces the installed languages (after an import); the chosen ones that are still installed stay chosen.</summary>
    public void Reload(IReadOnlyList<TessdataLanguage> installed)
    {
        ArgumentNullException.ThrowIfNull(installed);
        _installed = installed;
        _selected.RemoveAll(name => !IsInstalled(name));
        if (_selected.Count == 0)
        {
            _selected.AddRange(DefaultNames());
        }
    }

    private bool IsInstalled(string name) => _installed.Any(l => string.Equals(l.Name, name, StringComparison.Ordinal));

    private IEnumerable<string> DefaultNames() => Default is { Length: > 0 } text ? OcrLanguages.Split(text) : [];
}
