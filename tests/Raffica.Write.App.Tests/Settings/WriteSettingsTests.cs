using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Core.Viewing;
using Raffica.Hosting;
using Raffica.Write;
using Raffica.Write.Settings;
using Raffica.WriteApp.Tests.Documents;
using Xunit;
using ShellSettingsStore = Raffica.Shell.Frame.SettingsStore;

namespace Raffica.WriteApp.Tests.Settings;

public sealed class WriteSettingsTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-write-settings-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string FilePath => Path.Combine(_folder, "write-settings.json");

    [Fact]
    public void AMissingFileGivesTheDefaults() =>
        Assert.Equal(new WriteSettings(WriteView.Page, ZoomMode.FitWidth), new WriteSettingsStore(FilePath).Load());

    [Fact]
    public void ASavedFileComesBackAsItWas()
    {
        var store = new WriteSettingsStore(FilePath);
        Assert.True(store.TrySave(new WriteSettings(WriteView.Writing, ZoomMode.FitPage)));

        Assert.Equal(new WriteSettings(WriteView.Writing, ZoomMode.FitPage), new WriteSettingsStore(FilePath).Load());
        Assert.Contains("Writing", File.ReadAllText(FilePath), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ \"DefaultView\": \"Nonsense\" }")]
    [InlineData("not json at all")]
    [InlineData("[1, 2]")]
    public void ACorruptFileGivesTheDefaultsAndIsKeptOnceOnRequest(string text)
    {
        File.WriteAllText(FilePath, text);

        Assert.Equal(WriteSettings.Default, new WriteSettingsStore(FilePath).Load());
        Assert.False(File.Exists(FilePath + ".bad"));
        Assert.Equal(WriteSettings.Default, new WriteSettingsStore(FilePath).Load(keepBackup: true));
        Assert.True(File.Exists(FilePath + ".bad"));
    }

    [Fact]
    public void AValueOutOfRangeIsRepaired()
    {
        File.WriteAllText(FilePath, "{ \"DefaultView\": \"Writing\", \"DefaultZoomMode\": \"Free\" }");
        Assert.Equal(new WriteSettings(WriteView.Writing, ZoomMode.FitWidth), new WriteSettingsStore(FilePath).Load());

        Assert.Equal(WriteView.Page, new WriteSettings((WriteView)99).Normalized().DefaultView);
        Assert.Equal(ZoomMode.FitWidth, new WriteSettings(WriteView.Page, (ZoomMode)99).Normalized().DefaultZoomMode);
    }

    [Fact]
    public void AnOversizedFileIsNotReadAndTheFolderIsMadeOnSave()
    {
        File.WriteAllText(FilePath, new string(' ', 2 * 1024 * 1024));
        Assert.Equal(WriteSettings.Default, new WriteSettingsStore(FilePath).Load());

        var nested = Path.Combine(_folder, "a", "b", "write-settings.json");
        Assert.True(new WriteSettingsStore(nested).TrySave(WriteSettings.Default));
        Assert.True(File.Exists(nested));
    }

    // The dialog of a real window (Task 7 of milestone 4.3c).

    internal static CheckBox AutosaveBox(WriteWindow window) =>
        window.Host.ActiveDialog!.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Name == "PART_Autosave");

    internal static void OpenSettings(WriteWindow window)
    {
        window.Settings.Open();
        WindowFixture.Pump();
        Assert.True(window.Host.IsOpen);
    }

    [AvaloniaTheory]
    [InlineData("en", "Keep a recovery copy of unsaved work")]
    [InlineData("de", "Wiederherstellungskopie ungespeicherter Arbeit anlegen")]
    public void TheDialogShowsTheAutosaveRowWithWritesLabel(string language, string expected)
    {
        var previous = WriteStrings.Culture;
        WriteStrings.Culture = CultureInfo.GetCultureInfo(language);
        try
        {
            using var fixture = new WindowFixture();
            var window = fixture.Show();

            OpenSettings(window);

            var box = AutosaveBox(window);
            Assert.Equal(expected, box.Content);
            Assert.True(box.IsChecked);
        }
        finally
        {
            WriteStrings.Culture = previous;
        }
    }

    [AvaloniaFact]
    public void ConfirmingStoresTheValue()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        OpenSettings(window);

        AutosaveBox(window).IsChecked = false;
        SaveHarness.Click(window, "PART_Ok");

        Assert.False(window.Settings.Shell.Autosave);
        Assert.False(new ShellSettingsStore(AppFiles.Write.ShellPathFor(fixture.SettingsFile)).Load().Autosave);

        // Cancel keeps what is stored.
        OpenSettings(window);
        Assert.False(AutosaveBox(window).IsChecked);
        AutosaveBox(window).IsChecked = true;
        SaveHarness.Click(window, "PART_Cancel");
        Assert.False(window.Settings.Shell.Autosave);
    }

    [AvaloniaFact]
    public async Task TurningItOffStopsTheSchedulerWithoutDeletingCopies()
    {
        using var fixture = new WindowFixture();
        var clock = new Raffica.WriteApp.Tests.Recovery.ManualRecoveryClock();
        var window = fixture.Show();
        window.RecoveryClockFactory = () => clock;
        var (tab, _) = await SaveHarness.OpenCopyAsync(fixture, window);
        SaveHarness.Edit(tab);
        clock.Fire();
        await SaveHarness.AwaitAsync(window.Autosave!.WhenIdleAsync());
        var store = new Raffica.Write.Recovery.RecoveryStore(window.RecoveryFolder);
        var entries = Assert.Single(store.List().Entries);

        OpenSettings(window);
        AutosaveBox(window).IsChecked = false;
        SaveHarness.Click(window, "PART_Ok");
        SaveHarness.Edit(tab, "more");
        clock.Fire();
        await SaveHarness.AwaitAsync(window.Autosave.WhenIdleAsync());

        // The copy stays, and no new revision of it was written.
        var after = Assert.Single(store.List().Entries);
        Assert.Equal(entries, after);

        OpenSettings(window);
        AutosaveBox(window).IsChecked = true;
        SaveHarness.Click(window, "PART_Ok");
        clock.Fire();
        await SaveHarness.AwaitAsync(window.Autosave.WhenIdleAsync());
        Assert.Single(store.List().Entries);
    }
}
