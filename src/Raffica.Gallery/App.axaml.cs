using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Raffica.Shell;
using Raffica.Shell.Localization;

namespace Raffica.Gallery;

internal sealed partial class App : Application
{
    private readonly GalleryOptions _options;

    public App(GalleryOptions options)
    {
        _options = options;
    }

    public override void Initialize()
    {
        var culture = Languages.Resolve(_options.Lang, System.Globalization.CultureInfo.CurrentUICulture);
        ShellStrings.Culture = culture;
        GalleryStrings.Culture = culture;

        AvaloniaXamlLoader.Load(this);
        ShellTheme.Apply(this, _options.Theme, _options.Ink, _options.HighContrast ? true : null);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new GalleryWindow(_options);
            desktop.MainWindow = window;
            if (_options.Screenshot is { } path)
            {
                window.Opened += (_, _) => Screenshot.CaptureAfterFirstFrame(window, path, desktop.Shutdown);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>The <c>--screenshot</c> switch: renders the window once after its first frame into a PNG file.</summary>
internal static class Screenshot
{
    // Time for the layout, the fonts and the forced states to settle before the single frame is taken.
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(400);

    public static void CaptureAfterFirstFrame(Window window, string path, Action<int> done) =>
        DispatcherTimer.RunOnce(() => done(Capture(window, path)), Settle);

    private static int Capture(Window window, string path)
    {
        try
        {
            var size = new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height);
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96));
            bitmap.Render(window);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            bitmap.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine($"error: the screenshot could not be written: {ex.Message}");
            return 1;
        }
    }
}
