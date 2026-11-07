using System.Globalization;
using Raffica.Core.Palette;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Panes;
using Raffica.Shell.Frame;

namespace Raffica.Pdf;

/// <summary>What the palette needs from the window, as delegates (the window gives its own, tests give theirs).</summary>
internal sealed class PaletteContext
{
    /// <summary>The open documents.</summary>
    public required Func<IReadOnlyList<PdfTab>> Tabs { get; init; }

    /// <summary>The active document, or null.</summary>
    public required Func<PdfTab?> ActiveTab { get; init; }

    /// <summary>Makes a document the active one.</summary>
    public required Action<PdfTab> Select { get; init; }

    /// <summary>The newest recent files (at most the given count); empty until the list was loaded.</summary>
    public required Func<int, IReadOnlyList<RecentFile>> Recent { get; init; }

    /// <summary>Opens a recent file.</summary>
    public required Action<RecentFile> OpenRecent { get; init; }

    /// <summary>The bookmarks of the active document, or null while they are not read (or without a document).</summary>
    public required Func<IReadOnlyList<OutlineEntry>?> Outline { get; init; }

    /// <summary>The number of pages of the active document (0 without one).</summary>
    public required Func<int> PageCount { get; init; }

    /// <summary>Goes to a page (0-based) of the active document.</summary>
    public required Action<int> GoToPage { get; init; }

    /// <summary>Shows the card with the facts about the active document.</summary>
    public required Action ShowDocumentInfo { get; init; }

    /// <summary>Makes Raffica the default app for PDF files (the command <c>app.default</c>); null: no entry.</summary>
    public Action? MakeDefaultApp { get; init; }

    /// <summary>Shows the About card (the command <c>app.about</c>); null: no entry.</summary>
    public Action? ShowAbout { get; init; }
}

/// <summary>
/// The extra results of the command palette (<see cref="SearchField.ExtraResults"/>): "Go to page N" for a number in range, the open
/// documents, "Document info", the bookmarks of the active document (at most <see cref="MaxOutline"/>) and the recent files (at most
/// <see cref="MaxRecent"/>), each matched with Core's <see cref="PaletteMatcher"/>. The palette asks on every keystroke, so this does no
/// I/O and reads no PDF: the recent list and the outline are in memory already (read after the first sharp page), and a section whose
/// data is not there yet is simply left out. It never throws: a section that fails is left out and the rest still shows, and an entry
/// that fails when it runs does nothing.
/// </summary>
internal sealed class PdfPaletteSource(PaletteContext context)
{
    /// <summary>The most recent files the palette lists.</summary>
    public const int MaxRecent = 10;

    /// <summary>The most bookmarks the palette lists.</summary>
    public const int MaxOutline = 50;

    // A longer query cannot match a title that fits a menu; matching it would only cost time.
    private const int MaxQueryLength = 200;

    /// <summary>The results for what was typed (the non-empty text of the field).</summary>
    public IReadOnlyList<SearchEntry> Find(string query)
    {
        try
        {
            var text = query?.Trim() ?? string.Empty;
            if (text.Length == 0 || text.Length > MaxQueryLength)
            {
                return [];
            }

            var entries = new List<SearchEntry>();
            Section(entries, () => GoToPage(text));
            Section(entries, () => OpenDocuments(text));
            Section(entries, () => DocumentInfo(text));
            Section(entries, () => DefaultApp(text));
            Section(entries, () => About(text));
            Section(entries, () => Outline(text));
            Section(entries, () => RecentFiles(text));
            return entries;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return [];
        }
    }

    private static void Section(List<SearchEntry> entries, Func<IEnumerable<SearchEntry>> make)
    {
        try
        {
            entries.AddRange(make());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // One failing source (a tab that closed meanwhile) must not take the other results away.
        }
    }

    // An entry that fails when it is chosen does nothing; the palette must not take the window down.
    private static Action Safe(Action run) => () =>
    {
        try
        {
            run();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }
    };

    private IEnumerable<SearchEntry> GoToPage(string text)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var page))
        {
            yield break;
        }

        var count = context.PageCount();
        if (page >= 1 && page <= count)
        {
            yield return new SearchEntry(
                PdfStrings.PaletteGroupGoTo,
                PdfStrings.Fill(PdfStrings.PaletteGoToPageFormat, page),
                Safe(() => context.GoToPage(page - 1)),
                PdfStrings.Fill(PdfStrings.PaletteGoToPageDetailFormat, count));
        }
    }

    private IEnumerable<SearchEntry> OpenDocuments(string text)
    {
        foreach (var tab in context.Tabs())
        {
            if (PaletteMatcher.Score(text, tab.Title) >= 0)
            {
                var chosen = tab;
                yield return new SearchEntry(PdfStrings.PaletteGroupTabs, tab.Title, Safe(() => context.Select(chosen)));
            }
        }
    }

    private IEnumerable<SearchEntry> DocumentInfo(string text)
    {
        if (context.ActiveTab() is not null && PaletteMatcher.Score(text, PdfStrings.InfoTitle) >= 0)
        {
            yield return new SearchEntry(PdfStrings.PaletteGroupDocument, PdfStrings.InfoTitle, Safe(context.ShowDocumentInfo));
        }
    }

    private IEnumerable<SearchEntry> DefaultApp(string text)
    {
        if (context.MakeDefaultApp is { } run && PaletteMatcher.Score(text, PdfStrings.CmdMakeDefaultApp) >= 0)
        {
            yield return new SearchEntry(PdfStrings.PaletteGroupApp, PdfStrings.CmdMakeDefaultApp, Safe(run));
        }
    }

    private IEnumerable<SearchEntry> About(string text)
    {
        if (context.ShowAbout is { } run && PaletteMatcher.Score(text, PdfStrings.CmdAbout) >= 0)
        {
            yield return new SearchEntry(PdfStrings.PaletteGroupApp, PdfStrings.CmdAbout, Safe(run));
        }
    }

    private IEnumerable<SearchEntry> Outline(string text)
    {
        if (context.Outline() is not { Count: > 0 } outline)
        {
            return [];
        }

        // The best matches first (a title that starts with the word beats one that only contains it), the order of the document for equal ones.
        var matches = new List<(OutlineEntry Entry, int Score, int Order)>();
        for (var i = 0; i < outline.Count; i++)
        {
            var score = PaletteMatcher.Score(text, outline[i].Title);
            if (score >= 0)
            {
                matches.Add((outline[i], score, i));
            }
        }

        return matches
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Order)
            .Take(MaxOutline)
            .Select(m =>
            {
                var page = m.Entry.Page;
                return new SearchEntry(
                    PdfStrings.PaletteGroupOutline,
                    m.Entry.Title,
                    Safe(() => context.GoToPage(page)),
                    PdfStrings.Fill(PdfStrings.PaletteOutlinePageFormat, page + 1));
            });
    }

    private IEnumerable<SearchEntry> RecentFiles(string text)
    {
        foreach (var file in context.Recent(MaxRecent))
        {
            if (PaletteMatcher.Score(text, file.Name) >= 0)
            {
                var chosen = file;
                yield return new SearchEntry(PdfStrings.PaletteGroupRecent, file.Name, Safe(() => context.OpenRecent(chosen)), file.Folder);
            }
        }
    }
}
