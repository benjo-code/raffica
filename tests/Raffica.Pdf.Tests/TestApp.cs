using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Raffica.Pdf.Tests;
using Raffica.Shell;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Raffica.Pdf.Tests;

/// <summary>The application of the headless tests: the shell styles and the page view style of the app (real Skia text shaping).</summary>
public sealed class TestApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new RafficaStyles());
        Styles.Add(new StyleInclude(new Uri("avares://RafficaPdf/")) { Source = new Uri("avares://RafficaPdf/Styles/PageView.axaml") });
        Styles.Add(new StyleInclude(new Uri("avares://RafficaPdf/")) { Source = new Uri("avares://RafficaPdf/Styles/Panes.axaml") });
        Styles.Add(new StyleInclude(new Uri("avares://RafficaPdf/")) { Source = new Uri("avares://RafficaPdf/Styles/SearchBar.axaml") });
    }
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
