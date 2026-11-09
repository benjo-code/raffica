using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Raffica.Shell;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Pdf.Tests.Hosting;

public sealed class WindowPlacementWindowTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-placement-window-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string SettingsPath => Path.Combine(_folder, "settings.json");

    private static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    private PdfWindow Make(params string[] extra)
    {
        var options = PdfOptions.Parse(["--settings-file", SettingsPath, .. extra]);
        return new PdfWindow(options, new SettingsStore(SettingsPath).Load());
    }

    [AvaloniaFact]
    public void AStoredPlacementIsAppliedBeforeTheWindowIsShown()
    {
        new SettingsStore(SettingsPath).Save(new ShellSettings(WindowPlacement: new WindowPlacement(60, 70, 1100, 700, false)));

        var window = Make();

        Assert.False(window.IsVisible);
        Assert.Equal(new PixelPoint(60, 70), window.Position);
        Assert.Equal(1100, window.Width);
        Assert.Equal(700, window.Height);
        Assert.Equal(WindowState.Normal, window.WindowState);
    }

    [AvaloniaFact]
    public void ANeverStoredPlacementKeepsTheDefaultSizeOnABigScreenAndStaysInsideTheWorkArea()
    {
        var window = Make();

        var screen = window.Screens.Primary!;
        Assert.True(window.Width <= 1280 && window.Height <= 860);
        Assert.True(window.Width <= (screen.WorkingArea.Width / screen.Scaling) - (2 * WindowPlacement.Margin));
        Assert.True(window.Height <= (screen.WorkingArea.Height / screen.Scaling) - (2 * WindowPlacement.Margin));
        Assert.True(window.Position.X >= screen.WorkingArea.X && window.Position.Y >= screen.WorkingArea.Y);
    }

    [AvaloniaFact]
    public void AMaximizedPlacementStartsMaximizedAndClosingStoresTheRestoreBounds()
    {
        new SettingsStore(SettingsPath).Save(new ShellSettings(WindowPlacement: new WindowPlacement(60, 70, 1100, 700, true)));
        var window = Make();
        Assert.Equal(WindowState.Maximized, window.WindowState);

        window.Show();
        Pump();
        window.Close();
        Pump();

        var stored = new SettingsStore(SettingsPath).Load().WindowPlacement;
        Assert.NotNull(stored);
        Assert.True(stored.Maximized);
        Assert.Equal(1100, stored.Width);
        Assert.Equal(700, stored.Height);
        Assert.Equal(60, stored.X);
        Assert.Equal(70, stored.Y);
    }

    [AvaloniaFact]
    public void ClosingStoresWhereTheWindowWasAndKeepsTheOtherSettings()
    {
        new SettingsStore(SettingsPath).Save(new ShellSettings(ThemeMode.Dark, "de", CompactCommandBar: true));
        var window = Make();
        window.Show();
        Pump();
        window.Position = new PixelPoint(120, 90);
        window.Width = 1150;
        window.Height = 720;
        Pump();

        window.Close();
        Pump();

        var settings = new SettingsStore(SettingsPath).Load();
        Assert.Equal(ThemeMode.Dark, settings.Theme);
        Assert.Equal("de", settings.Language);
        Assert.True(settings.CompactCommandBar);
        Assert.NotNull(settings.WindowPlacement);
        Assert.False(settings.WindowPlacement.Maximized);
        Assert.Equal(120, settings.WindowPlacement.X);
        Assert.Equal(90, settings.WindowPlacement.Y);
        Assert.Equal(1150, settings.WindowPlacement.Width);
        Assert.Equal(720, settings.WindowPlacement.Height);
    }

    [AvaloniaFact]
    public async Task AWindowThatIsMaximizedWhenItClosesStoresTheBoundsOfBeforeAndTheFlag()
    {
        var window = Make();
        window.Show();
        Pump();
        window.Position = new PixelPoint(80, 50);
        window.Width = 1100;
        window.Height = 700;
        Pump();
        await Task.Delay((int)PdfWindow.TransitionMs + 100, TestContext.Current.CancellationToken);
        window.WindowState = WindowState.Maximized;
        Pump();

        window.Close();
        Pump();

        var stored = new SettingsStore(SettingsPath).Load().WindowPlacement;
        Assert.NotNull(stored);
        Assert.True(stored.Maximized);
        Assert.Equal(1100, stored.Width);
        Assert.Equal(700, stored.Height);
    }

    [AvaloniaFact]
    public void AMeasurementRunKeepsTheFixedSizeAndStoresNothing()
    {
        new SettingsStore(SettingsPath).Save(new ShellSettings(WindowPlacement: new WindowPlacement(60, 70, 1100, 700, false)));
        var window = Make("--measure-startup", Raffica.TestSupport.TestData.Pdf("hello_world.pdf"));

        Assert.Equal(1280, window.Width);
        Assert.Equal(860, window.Height);

        window.Show();
        Pump();
        window.Close();
        Pump();
        Assert.Equal(new WindowPlacement(60, 70, 1100, 700, false), new SettingsStore(SettingsPath).Load().WindowPlacement);
    }

    [AvaloniaFact]
    public void TheSettingsDialogKeepsThePlacement()
    {
        var window = Make();
        var placement = new WindowPlacement(1, 2, 1000, 600, false);
        window.Settings.RememberWindow(placement);

        Assert.Equal(placement, window.Settings.Shell.WindowPlacement);
        Assert.Equal(placement, new SettingsStore(SettingsPath).Load().WindowPlacement);
    }

    [AvaloniaFact]
    public void APositionThatArrivesBeforeTheMaximizedStateIsNotTheRestoreBounds()
    {
        var window = Make();
        window.Show();
        Pump();
        var at = Environment.TickCount64 + 100_000;
        var normal = new WindowPlacement(100, 80, 1100, 700, false);
        window.NoteBounds(normal, at);

        // Win32 raises the move to the maximized corner (and the old size) before the state changes.
        window.NoteBounds(new WindowPlacement(-8, -8, 1100, 700, false), at + 8_990);
        window.NoteState(WindowState.Maximized, at + 9_000);

        Assert.Equal(normal, window.RestoreBounds);
        window.Close();
        Pump();
        var stored = new SettingsStore(SettingsPath).Load().WindowPlacement;
        Assert.NotNull(stored);
        Assert.True(stored.Maximized);
        Assert.Equal(100, stored.X);
        Assert.Equal(80, stored.Y);
        Assert.Equal(1100, stored.Width);
    }

    [AvaloniaFact]
    public void ASizeThatArrivesBeforeTheMinimizedStateAndTheSentinelPositionAreNotKept()
    {
        var window = Make();
        var normal = new WindowPlacement(100, 80, 1100, 700, false);
        window.NoteBounds(normal, 1_000);

        window.NoteBounds(new WindowPlacement(-32000, -32000, 1100, 700, false), 9_980);
        window.NoteBounds(new WindowPlacement(100, 80, 0, 0, false), 9_985);
        Assert.Equal(normal, window.RestoreBounds);

        window.NoteBounds(new WindowPlacement(120, 90, 160, 28, false), 9_990);
        window.NoteState(WindowState.Minimized, 10_000);
        Assert.Equal(normal, window.RestoreBounds);

        // Restoring brings the normal state back, and a later real move is noted again.
        window.NoteState(WindowState.Normal, 12_000);
        window.NoteBounds(new WindowPlacement(140, 100, 1000, 650, false), 20_000);
        Assert.Equal(new WindowPlacement(140, 100, 1000, 650, false), window.RestoreBounds);
    }

    [AvaloniaFact]
    public void AMoveThatIsLongBeforeTheMaximizeStaysTheRestoreBounds()
    {
        var window = Make();
        window.NoteBounds(new WindowPlacement(100, 80, 1100, 700, false), 1_000);
        window.NoteBounds(new WindowPlacement(200, 120, 1000, 650, false), 5_000);

        window.NoteState(WindowState.Maximized, 9_000);

        Assert.Equal(new WindowPlacement(200, 120, 1000, 650, false), window.RestoreBounds);
    }
}
