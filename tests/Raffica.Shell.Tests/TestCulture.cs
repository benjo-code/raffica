using System.Globalization;
using System.Runtime.CompilerServices;
using Raffica.Shell.Localization;
using Xunit;

// The tests share process-wide state (the shell language, the application and its theme) and run on one UI thread whose queue they
// pump while they settle, so a test of another class could run inside a test that has just switched the language. One test at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

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
