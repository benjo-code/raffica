using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Hosting;
using Raffica.Shell.Frame;
using Raffica.Write.Documents;
using Xunit;
using RecentFilesStore = Raffica.Core.Settings.RecentFilesStore;

namespace Raffica.WriteApp.Tests;

/// <summary>A window over a folder of its own: the settings file (and with it the recent list) never touches the folders of the user.</summary>
internal sealed class WindowFixture : IDisposable
{
    private readonly List<WriteWindow> _windows = [];

    public WindowFixture() => Folder = Directory.CreateTempSubdirectory("raffica-write-app-").FullName;

    public string Folder { get; }

    public string SettingsFile => Path.Combine(Folder, "settings.json");

    public WriteWindow Window => _windows[^1];

    public static string Poi => RepoPaths.Corpus("poi-test-document");

    public static string Thesis => RepoPaths.Corpus("thesis");

    public WriteWindow Show(OpenWork? work = null, params string[] files) => ShowWith(null, work, files);

    /// <summary>Like <see cref="Show"/>, with the Write settings the window starts with (the view and zoom a new tab opens in).</summary>
    public WriteWindow ShowWith(Raffica.Write.Settings.WriteSettings? app, OpenWork? work = null, params string[] files)
    {
        var options = WriteOptions.Parse(["--settings-file", SettingsFile, .. files]);
        var window = new WriteWindow(options, new SettingsStore(SettingsFile).Load(), app, work);
        _windows.Add(window);
        window.Show();
        Pump();
        return window;
    }

    /// <summary>A copy of a corpus file in the folder of the fixture, so a test may move, lock or damage it.</summary>
    public string Copy(string source, string name)
    {
        var target = Path.Combine(Folder, name);
        File.Copy(source, target, overwrite: true);
        return target;
    }

    public string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(Folder, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    public static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 400 && !condition(); i++)
        {
            await Task.Delay(25);
            Pump();
        }

        Assert.True(condition(), "the condition was not met in time");
    }

    /// <summary>The texts of the card that is open.</summary>
    public static List<string> CardTexts(WriteWindow window) =>
        [.. window.Host.ActiveDialog!.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? string.Empty)];

    /// <summary>Seeds the recent list of the fixture's settings folder.</summary>
    public void SeedRecent(params string[] paths)
    {
        var files = new Raffica.Core.Settings.RecentFiles();
        foreach (var path in paths)
        {
            files.Touch(path, DateTimeOffset.UtcNow);
        }

        Assert.True(new RecentFilesStore(AppFiles.Write.RecentPathFor(SettingsFile)).TrySave(files));
    }

    public void Dispose()
    {
        foreach (var window in _windows)
        {
            if (window.IsVisible)
            {
                // A test that leaves unsaved changes behind must not be held by the close question of its own window.
                foreach (var tab in window.Workspace.Tabs)
                {
                    tab.Docx.Model.Edits.MarkSaved();
                }

                window.Close();
            }
        }

        Pump();
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // A file the test still holds; the temp folder is cleaned by the system.
        }
    }
}
