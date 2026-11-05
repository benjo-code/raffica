using System.Globalization;
using Raffica.Core.Pages;
using Raffica.Core.Pdf;
using Raffica.Core.Saving;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Shell.Controls;

namespace Raffica.Pdf.Organizing;

/// <summary>What the export card asks for.</summary>
/// <param name="Range">The text of the pages (<see cref="PageRange"/> syntax).</param>
/// <param name="Dpi">The resolution, 1 to 1200 dpi (the scale is dpi / 72).</param>
/// <param name="Marks">Whether marks and form fields are drawn.</param>
/// <param name="Folder">The folder the images go to.</param>
internal readonly record struct ImageExportChoice(string Range, int Dpi, bool Marks, string Folder);

/// <summary>What the texts of the export card say about the pages and the resolution (<see cref="ImageExportPlan.Evaluate"/>).</summary>
/// <param name="Pages">The 0-based pages, or null when the range is no selection.</param>
/// <param name="Dpi">The resolution, or 0 when the text is none.</param>
/// <param name="RangeProblem">Why the range is no selection (in the UI language), or null.</param>
/// <param name="DpiProblem">Why the resolution is none (in the UI language), or null.</param>
/// <param name="LargestPage">The 0-based page with the most pixels among the selected ones, or -1 when there is none to tell.</param>
/// <param name="LargestWidth">The width of that page in pixels.</param>
/// <param name="LargestHeight">The height of that page in pixels.</param>
/// <param name="OverPage">The first selected 0-based page above the limit of pixels, or -1.</param>
/// <param name="OverPixels">The pixels of that page (<see cref="long.MaxValue"/> when a side does not fit in an int).</param>
internal sealed record ImageExportView(
    IReadOnlyList<int>? Pages,
    int Dpi,
    string? RangeProblem,
    string? DpiProblem,
    int LargestPage,
    double LargestWidth,
    double LargestHeight,
    int OverPage,
    long OverPixels)
{
    /// <summary>Gets whether pages and resolution make an export that Core will not refuse for its size.</summary>
    public bool IsOk => Pages is { Count: > 0 } && RangeProblem is null && DpiProblem is null && OverPage < 0;
}

/// <summary>
/// The pure part of the image export: the resolution text, the pixel size of a page and the check against the limit of Core. Core's own check
/// (<c>PageImageExporter.Exceeds</c>) is internal, so the same arithmetic is here (each side rounded up, at least 1, a side beyond the int range
/// is over every limit, exactly <see cref="PageImageExporter.MaxPixels"/> is allowed); a test pins that both agree at the boundary.
/// </summary>
internal static class ImageExportPlan
{
    /// <summary>The lowest resolution of the custom field.</summary>
    public const int MinDpi = 1;

    /// <summary>The highest resolution of the custom field.</summary>
    public const int MaxDpi = 1200;

    /// <summary>The scale (pixels per point) of a resolution; 72 dpi is 1.</summary>
    public static double ScaleOf(int dpi) => dpi / 72.0;

    /// <summary>The pixels of a page of <paramref name="size"/> at <paramref name="scale"/>, or <see cref="long.MaxValue"/> when a side does not fit in an int.</summary>
    public static long PixelsOf(PdfPageSize size, double scale)
    {
        var (width, height) = SidesOf(size, scale);
        if (!double.IsFinite(width) || !double.IsFinite(height) || width > int.MaxValue || height > int.MaxValue)
        {
            return long.MaxValue;
        }

        return (long)width * (long)height;
    }

    /// <summary>Whether the page has more than <paramref name="maxPixels"/> pixels at <paramref name="scale"/>.</summary>
    public static bool Exceeds(PdfPageSize size, double scale, long maxPixels = PageImageExporter.MaxPixels) => PixelsOf(size, scale) > maxPixels;

    /// <summary>Why the text is no resolution from <see cref="MinDpi"/> to <see cref="MaxDpi"/> (in the UI language), or null; <paramref name="dpi"/> is the value then.</summary>
    public static string? DpiProblem(string? text, out int dpi)
    {
        dpi = 0;
        if (text is not null && int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value is >= MinDpi and <= MaxDpi)
        {
            dpi = value;
            return null;
        }

        return PdfStrings.ExportDpiInvalid;
    }

    /// <summary>Works out what the card says for the texts of the range and the resolution over the page sizes of the document.</summary>
    public static ImageExportView Evaluate(string? range, string? dpiText, IReadOnlyList<PdfPageSize> sizes)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        var rangeProblem = InsertFileCard.Problem(range, sizes.Count);
        var dpiProblem = DpiProblem(dpiText, out var dpi);
        IReadOnlyList<int>? pages = rangeProblem is null ? PageRange.Parse(range!, sizes.Count) : null;
        if (pages is null || dpiProblem is not null)
        {
            return new ImageExportView(pages, dpi, rangeProblem, dpiProblem, -1, 0, 0, -1, 0);
        }

        var scale = ScaleOf(dpi);
        var largest = -1;
        long largestPixels = -1;
        var over = -1;
        long overPixels = 0;
        foreach (var page in pages)
        {
            var pixels = PixelsOf(sizes[page], scale);
            if (pixels > largestPixels)
            {
                largest = page;
                largestPixels = pixels;
            }

            if (over < 0 && Exceeds(sizes[page], scale))
            {
                over = page;
                overPixels = pixels;
            }
        }

        var (width, height) = largest < 0 ? (0, 0) : SidesOf(sizes[largest], scale);
        return new ImageExportView(pages, dpi, null, null, largest, width, height, over, overPixels);
    }

    private static (double Width, double Height) SidesOf(PdfPageSize size, double scale) =>
        (Math.Max(1, Math.Ceiling(size.Width * scale)), Math.Max(1, Math.Ceiling(size.Height * scale)));
}

/// <summary>
/// "Export pages as images": PNG files of pages of the open document, without the plan (the command is locked while a plan is open or marks are
/// unsaved, and the flow refuses again). The page sizes are read first (a background read, awaited, never blocking the UI thread) so the card can
/// show the largest page in pixels and name a page over the limit before Export is possible; then the card, then <see cref="PageImageExporter"/>
/// of Core as a cancelable job with progress (the shared <see cref="JobRunner"/>), then the result card. Files are named by Core
/// (<c>&lt;name&gt;-p&lt;NNN&gt;.png</c>, as <c>raffica export</c> does), an existing file is never replaced and is named, a canceled or failed run keeps
/// and lists the finished files. Nothing here exists until the command runs; nothing escapes to the caller.
/// </summary>
/// <param name="dialogs">Where the cards appear.</param>
/// <param name="pickFolder">Asks for the folder; null: canceled.</param>
/// <param name="changed">Called when the flow starts or ends (the command states refresh); null: nothing.</param>
/// <param name="otherJob">Whether another file job of the window runs (a save, an apply, an extract or a split); the export is then refused; null: never.</param>
internal sealed class ImageExportFlow(
    DialogQueue dialogs,
    Func<PdfTab, Task<string?>> pickFolder,
    Action? changed = null,
    Func<bool>? otherJob = null)
{
    private readonly JobRunner _jobs = new(dialogs);
    private CancellationTokenSource? _reading;

    /// <summary>The export of Core (<see cref="PageImageExporter.ExportAsync"/> without overwrite).</summary>
    internal delegate Task<IReadOnlyList<string>> PagesExporter(
        PdfDocument document,
        string stem,
        IReadOnlyList<int> pages,
        double scale,
        bool annotations,
        string directory,
        IProgress<double>? progress,
        ICollection<string> written,
        string sourcePath,
        CancellationToken cancellationToken);

    /// <summary>Reads the page sizes of the document (tests give hostile sizes); null: <see cref="PdfDocument.GetPageSizesAsync"/>.</summary>
    internal Func<PdfDocument, CancellationToken, Task<IReadOnlyList<PdfPageSize>>>? SizeReader { get; set; }

    /// <summary>Replaces the export of Core (tests cancel between pages); null: Core's.</summary>
    internal PagesExporter? Exporter { get; set; }

    /// <summary>Replaces the dialog around the job card (tests); null: the normal dialog.</summary>
    internal Func<SaveJobCard, Dialog>? JobDialog
    {
        get => _jobs.JobDialog;
        set => _jobs.JobDialog = value;
    }

    /// <summary>Gets whether a flow is running (from its first card to its last).</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Cancels the running job and the read of the sizes, if any (the window is closing, or the user chose Cancel).</summary>
    public void Cancel()
    {
        _jobs.Cancel();
        try
        {
            _reading?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The read ended meanwhile.
        }
    }

    /// <summary>Runs "Export pages as images" for the active document of <paramref name="tab"/>. Returns whether all images were written.</summary>
    public async Task<bool> ExportAsync(PdfTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (IsRunning)
        {
            return false;
        }

        var title = PdfStrings.CmdExportImages;
        if (otherJob?.Invoke() == true)
        {
            await Tell(title, PdfStrings.FileJobBusy);
            return false;
        }

        IsRunning = true;
        try
        {
            JobRunner.Guarded(() => changed?.Invoke());
            if (SplitFlow.Refusal(tab) is { } reason)
            {
                await Tell(title, reason);
                return false;
            }

            return await CoreAsync(tab);
        }
        catch (OperationCanceledException)
        {
            // Canceled by the user (or the tab went away) before anything was written: no card.
            return false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Whatever is not foreseen ends as a card, never as an exception in a command handler.
            await Tell(title, FailureText(ex));
            return false;
        }
        finally
        {
            IsRunning = false;
            JobRunner.Guarded(() => changed?.Invoke());
        }
    }

    private async Task<bool> CoreAsync(PdfTab tab)
    {
        var source = DocumentSplitter.SourceOf(tab.Session) ?? throw new InvalidOperationException(PdfStrings.OutputNoSource);
        var sizes = await ReadSizesAsync(tab, source.Document);
        var choice = await dialogs.ShowAsync<ImageExportChoice?>(done => ExportImagesCard.Create(tab.Title, sizes, () => pickFolder(tab), done));
        if (choice is not { } chosen)
        {
            return false;
        }

        var pages = PageRange.Parse(chosen.Range, sizes.Count);
        var stem = Path.GetFileNameWithoutExtension(tab.Path ?? tab.Title);
        var scale = ImageExportPlan.ScaleOf(chosen.Dpi);
        var written = new List<string>();
        Exception? failure = null;
        var canceled = false;
        try
        {
            var export = Exporter ?? ExportThroughCoreAsync;
            await _jobs.RunAsync(
                tab,
                PdfStrings.CmdExportImages,
                PdfStrings.ExportJobTitleFormat,
                (progress, token) => export(source.Document, stem, pages, scale, chosen.Marks, chosen.Folder, progress, written, source.File.Path, token));
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            failure = ex;
        }

        var files = written.ToArray();
        var status = files.Length switch
        {
            0 => PdfStrings.OutputNothingWritten,
            1 => PdfStrings.OutputWrittenOne,
            _ => PdfStrings.Fill(PdfStrings.OutputWrittenFormat, files.Length),
        };
        var why = canceled ? PdfStrings.OutputCanceled : failure is null ? null : FailureText(failure);
        var shownFolder = files.Length > 0 ? Path.GetDirectoryName(files[0]) : chosen.Folder;
        var report = new PartsReport(PdfStrings.ExportResultTitle, status, why, files, shownFolder, CanOpen: false);
        await dialogs.ShowAsync<bool>(done => PartsResultCard.Create(report, done));
        return why is null;
    }

    private static Task<IReadOnlyList<string>> ExportThroughCoreAsync(
        PdfDocument document,
        string stem,
        IReadOnlyList<int> pages,
        double scale,
        bool annotations,
        string directory,
        IProgress<double>? progress,
        ICollection<string> written,
        string sourcePath,
        CancellationToken cancellationToken) =>
        PageImageExporter.ExportAsync(document, stem, pages, scale, annotations, directory, overwrite: false, progress, cancellationToken, written, sourcePath);

    private async Task<IReadOnlyList<PdfPageSize>> ReadSizesAsync(PdfTab tab, PdfDocument document)
    {
        using var reading = new CancellationTokenSource();
        _reading = reading;

        // A tab that closes during the read ends it as a quiet cancel (the document goes with the tab).
        void OnTabDisposing()
        {
            try
            {
                reading.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The read ended meanwhile.
            }
        }

        tab.Disposing += OnTabDisposing;
        try
        {
            return await (SizeReader?.Invoke(document, reading.Token)
                ?? document.GetPageSizesAsync(0, document.PageCount, PdfPriority.Background, reading.Token));
        }
        finally
        {
            tab.Disposing -= OnTabDisposing;
            _reading = null;
        }
    }

    /// <summary>The sentence for why an export stopped or was refused (the reason; the card names what was written).</summary>
    internal static string FailureText(Exception ex) => ex switch
    {
        PageExportException export => export.Error switch
        {
            PageExportError.TooLarge => PdfStrings.Fill(PdfStrings.ExportFailTooLargeFormat, export.Page + 1),
            PageExportError.DirectoryMissing => PdfStrings.OutputFailDirectoryMissing,
            PageExportError.TargetExists => PdfStrings.Fill(PdfStrings.OutputFailTargetExistsFormat, SplitFlow.NamesOf(export.Paths)),
            PageExportError.TargetIsSource => PdfStrings.OutputFailTargetIsSource,
            PageExportError.UnsafeName => PdfStrings.OutputFailUnsafeName,
            PageExportError.TooManyPages => PdfStrings.ExportFailTooMany,
            PageExportError.WriteFailed => PdfStrings.Fill(PdfStrings.ExportFailWriteFormat, export.Page + 1),
            _ => PdfStrings.OutputFailOther,
        },
        PdfRenderException => PdfStrings.ExportFailRender,
        FormatException or ArgumentOutOfRangeException => PdfStrings.OutputFailRange,
        _ => PdfStrings.OutputFailOther,
    };

    private Task<bool> Tell(string title, string message) =>
        dialogs.ShowAsync<bool>(done => NoticeCard.Create(title, message, () => done(true)));
}
