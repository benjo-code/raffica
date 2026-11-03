using System.Globalization;
using System.Runtime.CompilerServices;
using Raffica.Shell.Localization;

namespace Raffica.Shell.Tests;

/// <summary>
/// Pins the UI language of the shell strings to English once, when the test assembly loads, so no test depends on the language of the
/// machine it runs on (the string tables default to the OS UI language). A test that needs German sets
/// <see cref="ShellStrings.Culture"/> itself and puts the previous value back.
/// </summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void PinEnglish() => ShellStrings.Culture = CultureInfo.GetCultureInfo("en");
}
