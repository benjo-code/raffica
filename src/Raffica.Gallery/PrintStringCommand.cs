using System.Globalization;
using Raffica.Shell.Localization;

namespace Raffica.Gallery;

/// <summary>The <c>--print-string</c> switch: prints a shell string in the chosen language, before any window exists.</summary>
public static class PrintStringCommand
{
    /// <summary>Returns the exit code: 0 on success, 1 for an unknown key.</summary>
    public static int Run(GalleryOptions options, TextWriter output, TextWriter error)
    {
        ShellStrings.Culture = Languages.Resolve(options.Lang, CultureInfo.CurrentUICulture);
        try
        {
            output.WriteLine(ShellStrings.Get(options.PrintString!));
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }
}
