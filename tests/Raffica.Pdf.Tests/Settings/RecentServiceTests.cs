using Raffica.Core.Settings;
using Raffica.Core.Viewing;
using Raffica.Pdf.Settings;
using Xunit;
using ShellSettingsStore = Raffica.Shell.Frame.SettingsStore;

namespace Raffica.Pdf.Tests.Settings;

public sealed class RecentServiceTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("raffica-recent-").FullName;

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

    private string RecentFilePath => Path.Combine(_folder, "recent.json");

    private RecentService Create() => new(new RecentFilesStore(RecentFilePath));

    private static RememberedView View(int page) =>
        new(new ViewState(new PageAnchor(page, 0.25), 0), 1.5, ZoomMode.Free, PageLayoutMode.Continuous);

    private string PathOf(string name) => Path.Combine(_folder, name);

    [Fact]
    public async Task RecentListSurvivesARestart()
    {
        var first = Create();
        await first.LoadAsync();
        first.Touch(PathOf("a.pdf"));
        first.Touch(PathOf("b.pdf"));
        await first.FlushAsync();

        var second = Create();
        await second.LoadAsync();

        var list = second.ForStartView(5);
        Assert.Equal(["b.pdf", "a.pdf"], list.Select(f => f.Name));
        Assert.All(list, f => Assert.Equal(_folder, f.Folder));
    }

    [Fact]
    public async Task CorruptFileGivesAnEmptyList()
    {
        File.WriteAllText(RecentFilePath, "{ this is not json");
        var service = Create();
        await service.LoadAsync();
        Assert.Empty(service.ForStartView(5));
        Assert.Empty(service.ForPalette(10));
    }

    [Fact]
    public async Task OversizedFileGivesAnEmptyList()
    {
        File.WriteAllText(RecentFilePath, new string(' ', 2 * 1024 * 1024));
        var service = Create();
        await service.LoadAsync();
        Assert.Empty(service.ForStartView(5));
    }

    [Fact]
    public async Task PositionIsRememberedPerFile()
    {
        var first = Create();
        await first.LoadAsync();
        first.Remember(PathOf("a.pdf"), View(3));
        first.Remember(PathOf("b.pdf"), View(7));
        first.Touch(PathOf("a.pdf"));
        await first.FlushAsync();

        var second = Create();
        await second.LoadAsync();

        Assert.Equal(3, second.ViewOf(PathOf("a.pdf"))!.View.Anchor.Page);
        Assert.Equal(7, second.ViewOf(PathOf("b.pdf"))!.View.Anchor.Page);
        Assert.Null(second.ViewOf(PathOf("c.pdf")));
    }

    [Fact]
    public async Task NothingIsReadBeforeLoadAsync()
    {
        var seed = Create();
        await seed.LoadAsync();
        seed.Touch(PathOf("a.pdf"));
        await seed.FlushAsync();

        var service = Create();
        Assert.Empty(service.ForStartView(5));
        Assert.Null(service.ViewOf(PathOf("a.pdf")));

        // Even a change before the load does not read the file; it is applied once the list is there.
        service.Touch(PathOf("b.pdf"));
        Assert.Empty(service.ForStartView(5));

        await service.LoadAsync();
        Assert.Equal(["b.pdf", "a.pdf"], service.ForStartView(5).Select(f => f.Name));
    }

    [Fact]
    public async Task ClearAndRemoveWork()
    {
        var service = Create();
        await service.LoadAsync();
        service.Touch(PathOf("a.pdf"));
        service.Touch(PathOf("b.pdf"));
        service.Remove(PathOf("a.pdf"));
        Assert.Equal(["b.pdf"], service.ForStartView(5).Select(f => f.Name));

        service.Clear();
        Assert.Empty(service.ForStartView(5));
        await service.FlushAsync();

        var again = Create();
        await again.LoadAsync();
        Assert.Empty(again.ForStartView(5));
    }

    [Fact]
    public async Task ListsAreCutToTheRequestedCount()
    {
        var service = Create();
        await service.LoadAsync();
        for (var i = 0; i < 12; i++)
        {
            service.Touch(PathOf($"f{i}.pdf"));
        }

        Assert.Equal(5, service.ForStartView(5).Count);
        Assert.Equal(10, service.ForPalette(10).Count);
        Assert.Equal("f11.pdf", service.ForStartView(5)[0].Name);
    }

    [Fact]
    public void RecentFilesNeverTouchTheUsersFolderWhenASettingsFileIsGiven()
    {
        var settings = Path.Combine(_folder, "nested", "shell.json");

        Assert.Equal(settings, PdfSettingsFiles.ShellPathFor(settings));
        Assert.Equal(Path.Combine(_folder, "nested", "pdf-settings.json"), PdfSettingsFiles.PdfSettingsPathFor(settings));
        Assert.Equal(Path.Combine(_folder, "nested", "recent.json"), PdfSettingsFiles.RecentPathFor(settings));

        var config = UserDataPaths.ConfigDirectory();
        Assert.DoesNotContain(config, PdfSettingsFiles.RecentPathFor(settings), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WithoutASettingsFileTheDefaultsAreUsed()
    {
        Assert.Equal(ShellSettingsStore.DefaultPath("Pdf"), PdfSettingsFiles.ShellPathFor(null));
        Assert.Equal(ShellSettingsStore.DefaultPath("Pdf"), PdfSettingsFiles.ShellPath);
        Assert.Equal(Path.Combine(UserDataPaths.ConfigDirectory(), "recent.json"), PdfSettingsFiles.RecentPath);
        Assert.Equal(Path.Combine(UserDataPaths.ConfigDirectory(), "pdf-settings.json"), PdfSettingsFiles.PdfSettingsPath);
    }
}
