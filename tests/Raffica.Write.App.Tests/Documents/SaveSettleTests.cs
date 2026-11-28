using Avalonia.Headless.XUnit;
using Raffica.Write.Documents;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Settings;
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

    // The pages layout is made anew by the save: the one of the window went stale with the edit in the writing view, and no view is switched.
    [AvaloniaFact]
    public async Task ASaveAsFromTheWritingViewWritesRealPageNumbersAndKeepsTheView()
    {
        var (fixture, window, tab, _) = await OpenAsync(FieldTestDocument.TocBytes());
        using (fixture)
        {
            await WindowFixture.WaitAsync(() => tab.Runner.PageLayout.IsComplete);
            var view = window.ViewOf(tab)!;
            view.Mode = WriteView.Writing;
            await WindowFixture.WaitAsync(() => tab.Runner.FlowLayout is { IsComplete: true });
            var host = tab.EnsureFields().Host;
            var first = (Paragraph)tab.Docx.Model.Body.Blocks[0];
            Assert.NotNull(view.ApplyOperation(e => e.InsertToc(new TextPosition(StoryRef.Body, first, 0), new TocOptions(1, 3, true, true, TocLeader.Dots, null), host)));
            Assert.True(tab.Runner.PageLayoutIsStale);
            Assert.Contains(Lines(tab.Docx.Model), line => line.EndsWith("\t?", StringComparison.Ordinal));
            var target = Path.Combine(fixture.Folder, "writing.docx");
            window.SavePicker = _ => Task.FromResult<SavePick?>(new SavePick(target, Existed: false));

            var outcome = await SaveHarness.AwaitAsync(window.SaveAsActiveAsync());

            Assert.Equal(new SavedAs(target), outcome);
            using (var written = DocxFile.Open(target))
            {
                var lines = Lines(written.Model);
                Assert.DoesNotContain(lines, line => line.Contains('?', StringComparison.Ordinal));
                Assert.Equal(["Alpha\t1", "Beta\t1", "Gamma\t1"], lines.Where(line => line.Contains('\t', StringComparison.Ordinal)));
                Assert.Contains("see page 1", lines);
            }

            Assert.Equal(WriteView.Writing, view.Mode);
            Assert.Equal(LayoutMode.Flow, tab.Editor!.ActiveMode);
            Assert.True(tab.Runner.PageLayoutIsStale, "the save must not leave a page layout a switch of the view would trust");
            Assert.False(tab.Runner.IsFaulted);

            // The switch back makes the pages anew, and they show what was saved.
            view.Mode = WriteView.Page;
            await WindowFixture.WaitAsync(() => !tab.Runner.PageLayoutIsStale && tab.Runner.PageLayout.IsComplete);
        }
    }

    private static List<string> Lines(WordDocument model) => [.. model.Body.Blocks.OfType<Paragraph>().Select(p => ParagraphProjection.Build(p, ProjectionHost.Plain).Text)];

    [AvaloniaFact]
    public async Task AThrowingScanBeforeTheSettleStillLetsTheSaveWrite()
    {
        var (fixture, window, tab, _) = await OpenAsync(FieldTestDocument.Bytes());
        using (fixture)
        {
            tab.FieldScan = _ => throw new InvalidOperationException("test");
            var target = Path.Combine(fixture.Folder, "saved.docx");
            window.SavePicker = _ => Task.FromResult<SavePick?>(new SavePick(target, Existed: false));

            var outcome = await SaveHarness.AwaitAsync(window.SaveAsActiveAsync());

            Assert.Equal(new SavedAs(target), outcome);
            Assert.True(File.Exists(target));
            Assert.True(tab.FieldWiringFailures > 0);
        }
    }

    [AvaloniaFact]
    public async Task AThrowingSubscriberDuringTheSettleStillLetsTheSaveWrite()
    {
        var (fixture, window, tab, path) = await OpenAsync(FieldTestDocument.Bytes());
        using (fixture)
        {
            SaveHarness.Edit(tab, "typed ");
            tab.ModelEdited += () => throw new InvalidOperationException("test");

            var outcome = await SaveHarness.AwaitAsync(window.SaveActiveAsync());

            Assert.Equal(new Saved(path), outcome);
            var written = SaveHarness.TextOf(path);
            Assert.Contains("typed ", written, StringComparison.Ordinal);
            Assert.Contains("Figure 1", written, StringComparison.Ordinal);
            Assert.True(tab.Fields!.Failures >= 1);
        }
    }
}
