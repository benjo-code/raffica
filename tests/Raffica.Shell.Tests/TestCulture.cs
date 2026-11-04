using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Raffica.Gallery;
using Raffica.Shell.Localization;
using Xunit;
using Xunit.v3;

// The tests share process-wide state (the shell language, the application and its theme) and run on one UI thread, so one test at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

// Every test starts and ends in English, whatever the machine speaks and whatever an earlier test (or code under test such as the
// --print-string command, which sets the language of the process) left behind. The order of the tests is not fixed.
[assembly: Raffica.Shell.Tests.EnglishBeforeAndAfterEachTest]

namespace Raffica.Shell.Tests;

/// <summary>
/// Pins the UI language of the shell and gallery strings to English (the string tables default to the OS UI language). A test that
/// needs German sets <see cref="ShellStrings.Culture"/> itself; <see cref="EnglishBeforeAndAfterEachTestAttribute"/> takes it back
/// to English around every test, so a language set in one test can never be seen by another.
/// </summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void PinEnglish() => Reset();

    internal static void Reset()
    {
        var english = CultureInfo.GetCultureInfo("en");
        ShellStrings.Culture = english;
        GalleryStrings.Culture = english;
    }
}

/// <summary>Resets the string languages to English before and after each test (applied to the whole assembly).</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class EnglishBeforeAndAfterEachTestAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest, IXunitTest test) => TestCulture.Reset();

    public override void After(MethodInfo methodUnderTest, IXunitTest test) => TestCulture.Reset();
}
