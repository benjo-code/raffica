using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Shell.Tests.Frame;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "raffica-settings-" + Guid.NewGuid().ToString("N"));

    public SettingsStoreTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string FilePath => Path.Combine(_directory, "settings.json");

    [Fact]
    public void RoundTripKeepsEverySetting()
    {
        var store = new SettingsStore(Path.Combine(_directory, "nested", "settings.json"));
        var settings = new ShellSettings(ThemeMode.Dark, "de", CompactCommandBar: true, Autosave: false);

        Assert.True(store.TrySave(settings));

        Assert.Equal(settings, store.Load());
        Assert.Contains("\"Dark\"", File.ReadAllText(store.Path));
    }

    public static TheoryData<string, byte[]> BadFiles()
    {
        var garbage = new byte[1024 * 1024];
        new Random(7).NextBytes(garbage);
        return new()
        {
            { "empty", [] },
            { "open brace", "{"u8.ToArray() },
            { "wrong types", """{"Theme": 5, "Language": 12, "CompactCommandBar": "yes", "Autosave": []}"""u8.ToArray() },
            { "unknown theme", """{"Theme": "Purple"}"""u8.ToArray() },
            { "null document", "null"u8.ToArray() },
            { "array", "[1,2]"u8.ToArray() },
            { "1 MB of garbage", garbage },
            { "1 MB of valid-looking json", System.Text.Encoding.UTF8.GetBytes("{\"Language\": \"" + new string('x', 1024 * 1024) + "\"}") },
        };
    }

    [Theory]
    [MemberData(nameof(BadFiles))]
    public void CorruptOrMissingFileGivesDefaults(string name, byte[] content)
    {
        File.WriteAllBytes(FilePath, content);

        Assert.True(new ShellSettings().Equals(new SettingsStore(FilePath).Load()), name);
    }

    [Fact]
    public void MissingFileAndMissingFolderGiveDefaults()
    {
        Assert.Equal(new ShellSettings(), new SettingsStore(FilePath).Load());
        Assert.Equal(new ShellSettings(), new SettingsStore(Path.Combine(_directory, "no", "such", "settings.json")).Load());
    }

    [Fact]
    public void ADirectoryInPlaceOfTheFileGivesDefaults()
    {
        Directory.CreateDirectory(FilePath);

        Assert.Equal(new ShellSettings(), new SettingsStore(FilePath).Load());
    }

    [Fact]
    public void AnUnknownLanguageCodeFallsBackToFollowTheSystem()
    {
        // Languages.Resolve throws for a code other than de or en, which would stop the app from starting.
        File.WriteAllText(FilePath, """{"Theme": "Light", "Language": "fr", "Autosave": false}""");

        Assert.Equal(new ShellSettings(ThemeMode.Light, null, false, false), new SettingsStore(FilePath).Load());
    }

    [Fact]
    public void PartialFileKeepsTheOtherDefaults()
    {
        File.WriteAllText(FilePath, """{"CompactCommandBar": true}""");

        Assert.Equal(new ShellSettings(CompactCommandBar: true), new SettingsStore(FilePath).Load());
    }

    [Fact]
    public void SaveWritesAtomically()
    {
        var store = new SettingsStore(FilePath);
        Assert.True(store.TrySave(new ShellSettings(Language: "en")));
        Assert.True(store.TrySave(new ShellSettings(Language: "de")));
        Assert.Equal("de", store.Load().Language);
        Assert.Equal([FilePath], Directory.GetFiles(_directory));

        if (!OperatingSystem.IsWindows())
        {
            return; // A locked target (below) only fails the move on Windows.
        }

        // The target is held open, so the move over it fails: the old content survives and no temp file is left.
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(store.TrySave(new ShellSettings(Language: "en", Autosave: false)));
        }

        Assert.Equal(new ShellSettings(Language: "de"), store.Load());
        Assert.Equal([FilePath], Directory.GetFiles(_directory));
    }

    [Fact]
    public void UnwritablePathReturnsFalseFromTrySave()
    {
        // The parent of the target is a file, so the folder cannot be created.
        var blocker = Path.Combine(_directory, "blocker");
        File.WriteAllText(blocker, "x");
        var store = new SettingsStore(Path.Combine(blocker, "settings.json"));

        Assert.False(store.TrySave(new ShellSettings()));
        Assert.Throws<IOException>(() => store.Save(new ShellSettings()));
        Assert.Equal(new ShellSettings(), store.Load());
    }

    [Fact]
    public void AutosaveIsOnByDefault()
    {
        Assert.True(new ShellSettings().Autosave);
        Assert.True(new SettingsStore(FilePath).Load().Autosave);
    }

    [Fact]
    public void FirstStartHasNoLanguage()
    {
        Assert.Null(new ShellSettings().Language);
        Assert.Null(new SettingsStore(FilePath).Load().Language);
        Assert.Equal(ThemeMode.System, new SettingsStore(FilePath).Load().Theme);
    }

    [Fact]
    public void DefaultPathIsInTheApplicationDataFolder()
    {
        var path = SettingsStore.DefaultPath("Pdf");

        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), path);
        Assert.EndsWith(Path.Combine("Raffica", "Pdf", "settings.json"), path);
    }
}
