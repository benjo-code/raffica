using Raffica.Core.Printing;
using Raffica.Pdf.Platform.Linux;
using Raffica.Pdf.Platform.Windows;

namespace Raffica.Pdf.Platform;

/// <summary>Chooses the print service of the operating system; made on the first print, never at startup.</summary>
internal static class PlatformPrintServices
{
    /// <summary>Creates the service of this system: the Windows dialog, or CUPS on Linux and macOS.</summary>
    public static IPrintService Create() =>
        Choose(OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), OperatingSystem.IsMacOS());

    /// <summary>The choice as a seam: <see cref="NoPrintService"/> where nothing can print.</summary>
    /// <param name="windows">Whether the system is Windows.</param>
    /// <param name="linux">Whether the system is Linux.</param>
    /// <param name="macOs">Whether the system is macOS.</param>
    /// <returns>The print service.</returns>
#pragma warning disable CA1416 // The flags are the platform guard; the services only run on the system they are chosen for.
    internal static IPrintService Choose(bool windows, bool linux, bool macOs) =>
        windows ? new WindowsPrintService() : linux || macOs ? new CupsPrintService() : new NoPrintService();
#pragma warning restore CA1416
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
