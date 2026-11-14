using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Headless;
using Raffica.Shell;
using Raffica.Shell.Localization;
using Raffica.Write;
using Raffica.WriteApp.Tests;
using Xunit;
using Xunit.v3;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

// The tests share process-wide state (the string languages, the application and its theme) and run on one UI thread, so one test at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

// Every test starts and ends in English, whatever the machine speaks and whatever an earlier test left behind.
[assembly: EnglishBeforeAndAfterEachTest]

namespace Raffica.WriteApp.Tests;

/// <summary>The application of the headless tests: the shell styles (real Skia text shaping).</summary>
public sealed class TestApp : Application
{
    public override void Initialize() => Styles.Add(new RafficaStyles());
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Pins the UI language of the shell and app strings to English (the string tables default to the OS UI language).</summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void PinEnglish() => Reset();

    internal static void Reset()
    {
        var english = CultureInfo.GetCultureInfo("en");
        ShellStrings.Culture = english;
        WriteStrings.Culture = english;
    }
}

/// <summary>Resets the string languages to English before and after each test (applied to the whole assembly).</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class EnglishBeforeAndAfterEachTestAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest, IXunitTest test) => TestCulture.Reset();

    public override void After(MethodInfo methodUnderTest, IXunitTest test) => TestCulture.Reset();
}
