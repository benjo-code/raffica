using Raffica.Core.Distribution;

namespace Raffica.Pdf.Platform;

/// <summary>
/// <c>--register-file-types=user|machine</c> and <c>--unregister-file-types=user|machine</c>, run by the installer. Writes only
/// what <see cref="FileTypeRegistration"/> names (never <c>UserChoice</c> or <c>FileExts</c>) and never opens a window.
/// </summary>
internal static class RegistrationCommand
{
    /// <summary>The exit code of a failed registry write.</summary>
    public const int WriteFailed = 2;

    /// <summary>Gets whether <paramref name="options"/> ask for a registration or removal.</summary>
    public static bool Applies(PdfOptions options) => options.RegisterFileTypes is not null || options.UnregisterFileTypes is not null;

    /// <summary>Runs the requested registration or removal.</summary>
    /// <returns>0 on success (also when no switch was given), 2 if a write failed.</returns>
    public static int Run(PdfOptions options, IRegistryWriter writer, string exePath)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(writer);
        try
        {
            if (options.RegisterFileTypes is { } register)
            {
                writer.Write(register, FileTypeRegistration.Entries(exePath));
            }
            else if (options.UnregisterFileTypes is { } unregister)
            {
                writer.Remove(unregister, FileTypeRegistration.Removals());
            }

            return 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException or ArgumentException)
        {
            Console.Error.WriteLine($"error: the file type registration failed: {ex.GetType().Name}: {ex.Message}");
            return WriteFailed;
        }
    }

    /// <summary>The writer of this platform, or null where there is no registry (the installer of the platform does the work).</summary>
    public static IRegistryWriter? CreateWriter() =>
        OperatingSystem.IsWindows() ? new Windows.WindowsFileTypeRegistry() : null;
}
