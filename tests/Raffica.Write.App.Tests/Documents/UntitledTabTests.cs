using System.Globalization;
using Avalonia.Headless.XUnit;
using Raffica.Core.Settings;
using Raffica.Hosting;
using Raffica.Write;
using Raffica.Write.Commands;
using Raffica.Write.Documents;
using Raffica.Write.Docx;
using Xunit;

namespace Raffica.WriteApp.Tests.Documents;

/// <summary>Untitled tabs (Task 2 of milestone 4.3c): a new document is a tab without a path, titled with the smallest free number, and Save on it is Save as.</summary>
public sealed class UntitledTabTests
{
    private static async Task<WriteTab> NewAsync(WriteWindow window)
    {
        var tab = await window.Flow.NewDocumentAsync(PaperSize.A4, CancellationToken.None);
        WindowFixture.Pump();
        Assert.NotNull(tab);
        return tab;
    }

    private static async Task ReadyAsync(WriteTab tab)
    {
        tab.Runner.Start();
        await WindowFixture.WaitAsync(() => tab.Runner.FullModelReady || tab.Runner.IsFaulted);
        Assert.True(tab.Runner.FullModelReady);
        tab.EnsureEditor();
    }

    [AvaloniaFact]
    public async Task TitlesAreTheSmallestFreeNumber()
    {
        var saved = WriteStrings.Culture;
        try
        {
            using var fixture = new WindowFixture();
            var window = fixture.Show();
            var first = await NewAsync(window);
            var second = await NewAsync(window);
            var third = await NewAsync(window);
            Assert.Equal(["Untitled", "Untitled 2", "Untitled 3"], new[] { first.Title, second.Title, third.Title });
            Assert.Equal(second.Title, second.Tab.Title);
            Assert.Null(second.Tab.Path);

            window.Workspace.Close(second);
            var again = await NewAsync(window);
            Assert.Equal("Untitled 2", again.Title);

            WriteStrings.Culture = CultureInfo.GetCultureInfo("de");
            // Titles are fixed when a tab is made: a German tab beside the English ones takes the first free German number.
            var german = await NewAsync(window);
            Assert.Equal("Unbenannt", german.Title);
            var german2 = await NewAsync(window);
            Assert.Equal("Unbenannt 2", german2.Title);
        }
        finally
        {
            WriteStrings.Culture = saved;
        }
    }

    [AvaloniaFact]
    public async Task AnUntitledTabIsEditableAtOnce()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = await NewAsync(window);
        Assert.True(tab.IsUntitled);
        Assert.False(tab.Docx.IsPrefix);
        await ReadyAsync(tab);
        Assert.False(tab.Tab.IsDirty);

        SaveHarness.Edit(tab);

        Assert.True(tab.Tab.IsDirty);
        Assert.True(tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task SaveOnAnUntitledTabIsSaveAs()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = await NewAsync(window);
        await ReadyAsync(tab);
        SaveHarness.Edit(tab);
        var target = Path.Combine(fixture.Folder, "fresh.docx");
        var picked = 0;
        window.SavePicker = t =>
        {
            picked++;
            Assert.Same(tab, t);
            return Task.FromResult<SavePick?>(new SavePick(target, Existed: false));
        };

        var outcome = await SaveHarness.AwaitAsync(window.SaveActiveAsync());

        Assert.Equal(new SavedAs(target), outcome);
        Assert.Equal(1, picked);
        Assert.Equal(target, tab.Path);
        Assert.False(tab.IsUntitled);
        Assert.Equal("fresh.docx", tab.Title);
        Assert.Contains(SaveHarness.Typed, SaveHarness.TextOf(target), StringComparison.Ordinal);
        Assert.False(tab.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task SaveIsEnabledForAnUntitledCleanTab()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = await NewAsync(window);
        await ReadyAsync(tab);
        Assert.False(tab.IsDirty);

        Assert.Null(SaveCommandState.Of(tab, flowRunning: false).Refusal(saveAs: false));
        var titled = new SaveCommandState(true, false, false, false, false, Dirty: false);
        Assert.Equal(SaveRefusal.NothingToSave, titled.Refusal(saveAs: false));
    }

    [AvaloniaFact]
    public async Task ALayoutFaultOfAnUntitledTabShowsNoOpenErrorButAsksTheCommandsAgain()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = await NewAsync(window);
        await ReadyAsync(tab);
        Assert.Null(SaveCommandState.Of(tab, flowRunning: false).Refusal(saveAs: true));
        WindowFixture.Pump();
        await Task.Delay(100);
        WindowFixture.Pump();
        var asked = 0;
        window.StatesChanged += () => asked++;

        tab.Runner.Fail(new InvalidOperationException("layout"));
        WindowFixture.Pump();

        // No card (the document has no file), but the disabled Save and Save as are read again so their reason shows.
        Assert.False(tab.Runner.FaultedByEdit);
        Assert.True(asked >= 1, "the command states were asked again");
        Assert.Equal(SaveRefusal.Unavailable, SaveCommandState.Of(tab, flowRunning: false).Refusal(saveAs: true));
        Assert.Equal(SaveRefusal.Unavailable, SaveCommandState.Of(tab, flowRunning: false).Refusal(saveAs: false));
    }

    [AvaloniaFact]
    public async Task AnUntitledTabIsNotInTheRecentList()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = await NewAsync(window);
        await ReadyAsync(tab);
        await window.Recent.FlushAsync();

        Assert.Empty(new RecentFilesStore(AppFiles.Write.RecentPathFor(fixture.SettingsFile)).Load().Items);
        Assert.Null(tab.SuggestedSavePath);
    }

    [AvaloniaFact]
    public async Task WorkspaceFindIgnoresUntitledTabs()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        _ = await NewAsync(window);
        var (tab, path) = await SaveHarness.OpenCopyAsync(fixture, window);

        Assert.Same(tab, window.Workspace.Find(path));
        Assert.Null(window.Workspace.Find(Path.Combine(fixture.Folder, "Untitled")));
    }

    [AvaloniaFact]
    public async Task RebindClearsTheSuggestedPath()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = await NewAsync(window);
        tab.SuggestedSavePath = Path.Combine(fixture.Folder, "old.docx");
        var target = Path.Combine(fixture.Folder, "new.docx");

        tab.Rebind(target);

        Assert.Null(tab.SuggestedSavePath);
        Assert.Equal(target, tab.Path);
    }
}
