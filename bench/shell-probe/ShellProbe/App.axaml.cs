using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace ShellProbe;

internal sealed class App : Application
{
    private readonly ProbeOptions _options;

    public App(ProbeOptions options)
    {
        _options = options;
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Window window;
            TextBox? input = null;
            if (_options.Mode == ProbeMode.Empty)
            {
                window = new EmptyWindow();
            }
            else
            {
                var frame = new ProbeWindow();
                input = frame.InputBox;
                window = frame;
            }

            if (Measurement.IsActive(_options))
            {
                new Measurement(_options, window, input).Start();
            }

            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
