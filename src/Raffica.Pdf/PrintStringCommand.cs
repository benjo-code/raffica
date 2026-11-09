using System.Globalization;
using Raffica.Shell.Localization;

namespace Raffica.Pdf;

/// <summary>The <c>--print-string</c> switch: prints a string of the app (or, failing that, of the shell) in the chosen language, before any window exists.</summary>
internal static class PrintStringCommand
{
    /// <summary>Returns the exit code: 0 on success, 1 for an unknown key.</summary>
    public static int Run(PdfOptions options, TextWriter output, TextWriter error)
    {
        var culture = Languages.Resolve(options.Lang, CultureInfo.CurrentUICulture);
        PdfStrings.Culture = culture;
        ShellStrings.Culture = culture;
        var key = options.PrintString!;
        try
        {
            output.WriteLine(PdfStrings.Get(key));
            return 0;
        }
        catch (InvalidOperationException)
        {
            // Not a key of the app: the shell's own strings (Cancel, Close, ...) are looked up the same way.
        }

        try
        {
            output.WriteLine(ShellStrings.Get(key));
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            error.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }
}
