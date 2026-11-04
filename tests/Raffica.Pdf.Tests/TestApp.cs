using Avalonia;
using Avalonia.Headless;
using Raffica.Pdf.Tests;
using Raffica.Shell;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Raffica.Pdf.Tests;

/// <summary>The application of the headless tests: the shell styles and nothing else (real Skia text shaping).</summary>
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
