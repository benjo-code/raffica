using Raffica.Core.Viewing;
using Raffica.Write.Settings;
using Xunit;

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
}
