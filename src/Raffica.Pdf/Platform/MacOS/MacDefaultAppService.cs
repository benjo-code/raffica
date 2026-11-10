using Raffica.Core.Distribution;

namespace Raffica.Pdf.Platform.MacOS;

/// <summary>
/// Default app for PDF files on macOS through LaunchServices. Only an app installed in a normal place can be made the
/// default; a dev build, a translocated copy or one on a mounted disk image is not supported. Status and result are read
/// back from the system: the answer is the real default, not the return code alone.
/// </summary>
internal sealed class MacDefaultAppService : IDefaultAppService
{
    private const string PdfContentType = "com.adobe.pdf";

    private readonly ILaunchServices _services;
    private readonly string? _bundlePath;

    public MacDefaultAppService(ILaunchServices services, string? bundlePath)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        _bundlePath = bundlePath;
    }

    public bool IsSupported => _bundlePath is not null && MacBundle.LocationOf(_bundlePath) == MacBundleLocation.Installed;

    public Task<DefaultAppStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        Task.Run(QueryStatus, CancellationToken.None);

    public Task<DefaultAppResult> MakeDefaultAsync(CancellationToken cancellationToken) =>
        Task.Run(SetDefault, CancellationToken.None);

    private static bool IsOurs(string? handler) =>
        handler is not null && string.Equals(handler, MacBundle.Identifier, StringComparison.OrdinalIgnoreCase);

    private DefaultAppStatus QueryStatus()
    {
        try
        {
            return IsOurs(_services.DefaultHandlerOf(PdfContentType)) ? DefaultAppStatus.IsDefault : DefaultAppStatus.NotDefault;
        }
        catch (Exception)
        {
            return DefaultAppStatus.Unknown;
        }
    }

    private DefaultAppResult SetDefault()
    {
        try
        {
            if (_services.SetDefaultHandler(PdfContentType, MacBundle.Identifier) != 0)
            {
                return DefaultAppResult.Failed;
            }

            return IsOurs(_services.DefaultHandlerOf(PdfContentType)) ? DefaultAppResult.Done : DefaultAppResult.Failed;
        }
        catch (Exception)
        {
            return DefaultAppResult.Failed;
        }
    }
}
