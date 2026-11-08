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
    public void AWindowThatIsMaximizedWhenItClosesStoresTheBoundsOfBeforeAndTheFlag()
    {
        var window = Make();
        window.Show();
        Pump();
        window.Position = new PixelPoint(80, 50);
        window.Width = 1100;
        window.Height = 700;
        Pump();
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
}
