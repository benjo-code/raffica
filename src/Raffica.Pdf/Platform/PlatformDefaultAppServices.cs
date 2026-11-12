using Raffica.Core.Distribution;
using Raffica.Pdf.Platform.Linux;
using Raffica.Pdf.Platform.MacOS;
using Raffica.Pdf.Platform.Windows;

namespace Raffica.Pdf.Platform;

/// <summary>Chooses the default app service of the operating system; called on first use, never at startup.</summary>
internal static class PlatformDefaultAppServices
{
    /// <summary>Creates the service of this system; elsewhere it is a stub that is not supported.</summary>
    public static IDefaultAppService Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsDefaultAppService();
        }

        if (OperatingSystem.IsLinux())
        {
            return new LinuxDefaultAppService();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacDefaultAppService(new LaunchServicesNative(), MacBundle.PathOf(Environment.ProcessPath));
        }

        return new UnsupportedDefaultAppService();
    }

    private sealed class UnsupportedDefaultAppService : IDefaultAppService
    {
        public bool IsSupported => false;

        public Task<DefaultAppStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(DefaultAppStatus.Unknown);

        public Task<DefaultAppResult> MakeDefaultAsync(CancellationToken cancellationToken) => Task.FromResult(DefaultAppResult.NotAvailable);
    }
}
