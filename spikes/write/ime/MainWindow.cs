using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Themes.Fluent;

namespace ImeSpike;

public static class Program
{
    [STAThread]
    public static int Main(string[] args) => AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}

public sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}

/// <summary>The custom control (ComposeBox), Avalonia's TextBox as the control group, and the log of what the input method did.</summary>
public sealed class MainWindow : Window
{
    readonly TextBox _log = new() { IsReadOnly = true, AcceptsReturn = true, Height = 260, FontFamily = "Consolas, monospace" };

    public MainWindow()
    {
        Title = "Write IME spike";
        Width = 640; Height = 640;
        var compose = new ComposeBox(Log);
        var control = new TextBox { Watermark = "Avalonia TextBox (control group)" };
        control.GotFocus += (_, _) => Log("TextBox focus");
        var copy = new Button { Content = "Copy log" };
        copy.Click += async (_, _) => { if (Clipboard is { } c) await c.SetTextAsync(_log.Text ?? ""); };
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "1. ComposeBox (our custom control). Click it, switch the input method, compose a word." });
        panel.Children.Add(compose);
        panel.Children.Add(new TextBlock { Text = "2. The same in Avalonia's TextBox (the control group)." });
        panel.Children.Add(control);
        panel.Children.Add(new TextBlock { Text = "Event log (preedit, commits, focus):" });
        panel.Children.Add(_log);
        panel.Children.Add(copy);
        Content = panel;
        Opened += (_, _) => { Log($"os {Environment.OSVersion}, desktop {Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "-"}"); compose.Focus(); };
    }

    void Log(string line)
    {
        _log.Text += $"{DateTime.Now:HH:mm:ss.fff} {line}\n";
        _log.CaretIndex = _log.Text.Length;
    }
}
