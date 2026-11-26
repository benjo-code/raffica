using Avalonia.Headless.XUnit;
using Raffica.Write.Documents;
using Raffica.WriteApp.Tests.Editing;
using Xunit;

namespace Raffica.WriteApp.Tests.Documents;

/// <summary>A save refreshes the field results first: what is written is what the screen shows, and the refresh itself changes no revision (milestone 4.5b, task 6).</summary>
public sealed class SaveSettleTests
{
    private static async Task<(WindowFixture Fixture, WriteWindow Window, WriteTab Tab, string Path)> OpenAsync(byte[] bytes)
    {
        var fixture = new WindowFixture();
        var window = fixture.Show();
        var path = fixture.Write("fields.docx", bytes);
        var tab = Assert.IsType<Opened>(await window.Flow.OpenAsync(path)).Tab;
        tab.FieldClock = new ManualFieldClock();
        WindowFixture.Pump();
        await WindowFixture.WaitAsync(() => window.ViewOf(tab) is not null);
        tab.Runner.Start();
        await WindowFixture.WaitAsync(() => tab.Runner.FullModelReady);
        return (fixture, window, tab, path);
    }

    [AvaloniaFact]
    public async Task SaveAsWritesTheRefreshedModel()
    {
        var (fixture, window, tab, _) = await OpenAsync(FieldTestDocument.Bytes());
        using (fixture)
        {
            // The document is clean, its results are stale and nothing has refreshed them (no timer was fired).
            Assert.False(tab.IsDirty);
            Assert.Equal("Figure 77", FieldTestDocument.TextOf(tab, 1));
            var target = Path.Combine(fixture.Folder, "saved.docx");
            window.SavePicker = _ => Task.FromResult<SavePick?>(new SavePick(target, Existed: false));

            var outcome = await SaveHarness.AwaitAsync(window.SaveAsActiveAsync());

            Assert.Equal(new SavedAs(target), outcome);
            var written = SaveHarness.TextOf(target);
            Assert.Contains("Figure 1", written, StringComparison.Ordinal);
            Assert.Contains("Figure 2", written, StringComparison.Ordinal);
            Assert.DoesNotContain("77", written, StringComparison.Ordinal);
            Assert.Contains("idx", written, StringComparison.Ordinal);
            Assert.Equal(0, tab.Docx.Model.Edits.Revision);
            Assert.False(tab.IsDirty);
        }
    }

    [AvaloniaFact]
    public async Task SaveWritesWhatTheBurstHadNotRefreshedYet()
    {
        var (fixture, window, tab, path) = await OpenAsync(FieldTestDocument.Bytes());
        using (fixture)
        {
            SaveHarness.Edit(tab, "typed ");
            var runner = tab.Fields!;
            Assert.Equal(0, runner.Passes);
            Assert.Equal("Figure 77", FieldTestDocument.TextOf(tab, 1));

            var outcome = await SaveHarness.AwaitAsync(window.SaveActiveAsync());

            Assert.Equal(new Saved(path), outcome);
            Assert.Equal(1, runner.Passes);
            var written = SaveHarness.TextOf(path);
            Assert.Contains("typed ", written, StringComparison.Ordinal);
            Assert.Contains("Figure 1", written, StringComparison.Ordinal);
            Assert.DoesNotContain("77", written, StringComparison.Ordinal);
            Assert.False(tab.IsDirty, "the save marked the revision it wrote and the refresh added none");
        }
    }

    [AvaloniaFact]
    public async Task ASaveOfADocumentWithoutFieldsRefreshesNothing()
    {
        var (fixture, window, tab, path) = await OpenAsync(FieldTestDocument.PlainBytes());
        using (fixture)
        {
            SaveHarness.Edit(tab, "typed ");

            var outcome = await SaveHarness.AwaitAsync(window.SaveActiveAsync());

            Assert.Equal(new Saved(path), outcome);
            Assert.Equal(0, tab.Fields?.Passes ?? 0);
            Assert.Equal(0, tab.Docx.Model.Edits.DerivedVersion);
        }
    }
}
