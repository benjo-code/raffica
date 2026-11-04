using Avalonia.Threading;
using Raffica.Pdf.Documents;
using Raffica.Shell.Frame;
using Raffica.TestSupport;

namespace Raffica.Pdf.Tests.Viewing;

/// <summary>A <see cref="PdfWindow"/> with its own settings folder (nothing is shared between tests); the folder is removed with the fixture.</summary>
internal sealed class WindowFixture : IDisposable
{
    private WindowFixture(string folder, PdfWindow window)
    {
        Folder = folder;
        Window = window;
    }

    public string Folder { get; }

    public PdfWindow Window { get; }

    public string RecentPath => Path.Combine(Folder, "recent.json");

    public string AppSettingsPath => Path.Combine(Folder, "pdf-settings.json");

    /// <summary>Makes the window; <paramref name="prepare"/> runs first and may write files into the settings folder.</summary>
    public static WindowFixture Create(Action<string>? prepare = null, bool show = true, double width = 1000, double height = 700, Action<Uri>? launcher = null, TimeProvider? time = null)
    {
        var folder = Directory.CreateTempSubdirectory("raffica-window-").FullName;
        prepare?.Invoke(folder);
        var options = PdfOptions.Parse(["--settings-file", Path.Combine(folder, "settings.json")]);

        // As the app does at startup: the app settings are read once, before the window, and handed to it.
        var appSettings = new Raffica.Core.Settings.SettingsStore(Path.Combine(folder, "pdf-settings.json")).LoadReadOnly();
        var window = new PdfWindow(options, new ShellSettings(), appSettings, launcher, time) { Width = width, Height = height };
        if (show)
        {
            window.Show();
            ViewFixture.Pump();
        }

        return new WindowFixture(folder, window);
    }

    /// <summary>Opens a test file as the active tab.</summary>
    public async Task<PdfTab> OpenAsync(string file)
    {
        var tab = await Window.Flow.OpenAsync(TestData.Pdf(file));
        ViewFixture.Pump();
        return tab ?? throw new InvalidOperationException(file + " did not open");
    }

    /// <summary>Loads the recent files (what the window does after its first sharp page).</summary>
    public async Task LoadRecentAsync()
    {
        await Window.OnFirstSharpPageAsync();
        ViewFixture.Pump();
    }

    public void Dispose()
    {
        if (Window.IsVisible)
        {
            Window.Close();
        }
        else
        {
            // Closed already, or never shown: the sessions of the tabs that are left still have to go.
            foreach (var tab in Window.Workspace.Tabs)
            {
                tab.Dispose();
            }
        }

        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }

        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
