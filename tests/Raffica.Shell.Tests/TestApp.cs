using Avalonia;
using Avalonia.Headless;
using Raffica.Shell;
using Raffica.Shell.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Raffica.Shell.Tests;

/// <summary>The application of the headless tests: the shell styles and nothing else (real Skia text shaping, so font tests measure real glyphs).</summary>
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
