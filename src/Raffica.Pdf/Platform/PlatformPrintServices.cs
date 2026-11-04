using Raffica.Core.Printing;
using Raffica.Pdf.Platform.Linux;
using Raffica.Pdf.Platform.Windows;

namespace Raffica.Pdf.Platform;

/// <summary>Chooses the print service of the operating system; made on the first print, never at startup.</summary>
internal static class PlatformPrintServices
{
    /// <summary>
    /// Creates the service of this system. Where nothing can print (macOS for now) the service
    /// reports <see cref="IPrintService.IsAvailable"/> as <see langword="false"/>.
    /// </summary>
    public static IPrintService Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsPrintService();
        }

        if (OperatingSystem.IsLinux())
        {
            return new CupsPrintService();
        }

        return new NoPrintService();
    }
}

/// <summary>The service for a system without printing: not available, no dialog, no printers.</summary>
internal sealed class NoPrintService : IPrintService
{
    /// <inheritdoc/>
    public bool IsAvailable => false;

    /// <inheritdoc/>
    public bool HasSystemDialog => false;

    /// <inheritdoc/>
    public Task<PrintTicket?> ShowDialogAsync(PrintRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Printing is not available on this system.");

    /// <inheritdoc/>
    public Task<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PrinterInfo>>([]);

    /// <inheritdoc/>
    public Task<bool> SupportsDuplexAsync(string printerName, CancellationToken cancellationToken) => Task.FromResult(false);

    /// <inheritdoc/>
    public Task PrintAsync(PrintJobRequest job, IProgress<PrintProgress> progress, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Printing is not available on this system.");
}
