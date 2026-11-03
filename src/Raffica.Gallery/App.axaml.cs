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
            var size = new Size(window.Bounds.Width, window.Bounds.Height);
            var scale = window.RenderScaling;
            var dpi = new Vector(96 * scale, 96 * scale);
            using var frame = new Avalonia.Media.Imaging.RenderTargetBitmap(PixelSizeOf(size, scale), dpi);
            frame.Render(window);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var popups = window is GalleryWindow gallery ? gallery.PopupTopLevels() : [];
            if (popups.Count == 0)
            {
                frame.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                return 0;
            }

            // A popup is a window of its own: paint the window and every open popup at its screen position into one picture.
            var origin = window.PointToScreen(new Point(0, 0));
            var canvas = new Canvas { Width = size.Width, Height = size.Height };
            canvas.Children.Add(new Image { Source = frame, Width = size.Width, Height = size.Height, Stretch = Avalonia.Media.Stretch.Fill });
            var parts = new List<Avalonia.Media.Imaging.RenderTargetBitmap>();
            try
            {
                foreach (var popup in popups.OfType<WindowBase>())
                {
                    var popupSize = new Size(popup.Bounds.Width, popup.Bounds.Height);
                    var part = new Avalonia.Media.Imaging.RenderTargetBitmap(PixelSizeOf(popupSize, popup.RenderScaling), new Vector(96 * popup.RenderScaling, 96 * popup.RenderScaling));
                    parts.Add(part);
                    part.Render(popup);
                    var image = new Image { Source = part, Width = popupSize.Width, Height = popupSize.Height, Stretch = Avalonia.Media.Stretch.Fill };
                    var at = popup.PointToScreen(new Point(0, 0));
                    Canvas.SetLeft(image, (at.X - origin.X) / scale);
                    Canvas.SetTop(image, (at.Y - origin.Y) / scale);
                    canvas.Children.Add(image);
                }

                canvas.Measure(size);
                canvas.Arrange(new Rect(size));
                using var composed = new Avalonia.Media.Imaging.RenderTargetBitmap(PixelSizeOf(size, scale), dpi);
                composed.Render(canvas);
                composed.Save(path, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
            finally
            {
                foreach (var part in parts)
                {
                    part.Dispose();
                }
            }

            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine($"error: the screenshot could not be written: {ex.Message}");
            return 1;
        }
    }

    private static PixelSize PixelSizeOf(Size size, double scale) =>
        new((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale));
}
