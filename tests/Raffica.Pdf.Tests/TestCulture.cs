using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Raffica.Pdf;
using Raffica.Shell.Localization;
using Xunit;
using Xunit.v3;

// The tests share process-wide state (the string languages, the application and its theme) and run on one UI thread, so one test at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

// Every test starts and ends in English, whatever the machine speaks and whatever an earlier test (or code under test such as the
// --print-string command, which sets the language of the process) left behind. The order of the tests is not fixed.
[assembly: Raffica.Pdf.Tests.EnglishBeforeAndAfterEachTest]

namespace Raffica.Pdf.Tests;

/// <summary>Pins the UI language of the shell and app strings to English (the string tables default to the OS UI language).</summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void PinEnglish() => Reset();

    internal static void Reset()
    {
        var english = CultureInfo.GetCultureInfo("en");
        ShellStrings.Culture = english;
        PdfStrings.Culture = english;
    }
}

/// <summary>Resets the string languages to English before and after each test (applied to the whole assembly).</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class EnglishBeforeAndAfterEachTestAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest, IXunitTest test) => TestCulture.Reset();

    public override void After(MethodInfo methodUnderTest, IXunitTest test) => TestCulture.Reset();
}
