using Avalonia.Headless.XUnit;
using Raffica.Write.Documents;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Panes;
using Raffica.Write.Recovery;
using Raffica.WriteApp.Tests.Documents;
using Raffica.WriteApp.Tests.Recovery;
using Raffica.WriteApp.Tests.Viewing;
using Xunit;

namespace Raffica.WriteApp.Tests.Editing;

/// <summary>
/// A derived change (a field refresh) replaces text without being an edit: the readers of content (the memoized facts of the session, the outline, the effective-format index) must
/// notice it through <see cref="DocumentEdits.Stamp"/>, and the history side (the dirty mark, the revision the recovery scheduler compares) must not.
/// </summary>
public sealed class DerivedStampConsumersTests
{
    private static async Task<WriteTab> OpenWholeAsync(string? path = null)
    {
        var attempt = DocumentOpener.Open(Path.GetFullPath(path ?? WindowFixture.Poi), new OpenWork(), DocumentOpener.DefaultMaxFileLength, CancellationToken.None);
        Assert.NotNull(attempt.Document);
        var tab = new WriteTab(path ?? WindowFixture.Poi, attempt.Document!);
        tab.Runner.LeaseWaitTimeout = TimeSpan.FromSeconds(60);
        tab.Runner.Start();
        await ViewHarness.WaitAsync(() => tab.Runner.FullModelReady || tab.Runner.IsFaulted, 30000);
        Assert.True(tab.Runner.FullModelReady);
        return tab;
    }

    private static Paragraph FirstParagraph(WriteTab tab) => tab.Docx.Model.Body.Blocks.OfType<Paragraph>().First();

    private static TextRange Whole(Paragraph paragraph) =>
        new(new TextPosition(StoryRef.Body, paragraph, 0), new TextPosition(StoryRef.Body, paragraph, ParagraphProjection.Build(paragraph, ProjectionHost.Plain).Text.Length));

    // Replaces the text of the paragraph the way a refresh does: no edit, no revision.
    private static void ReplaceTextAsARefreshDoes(WriteTab tab, Paragraph paragraph, string text)
    {
        var contents = paragraph.Inlines.OfType<Run>().SelectMany(r => r.Content).OfType<TextContent>().ToList();
        contents[0].Text = text;
        foreach (var other in contents.Skip(1))
        {
            other.Text = "";
        }

        tab.Docx.Model.Edits.RaiseDerived();
    }

    [AvaloniaFact]
    public async Task TheFactsAndTheDisplayOfTheSessionAreReadAgainAfterADerivedChange()
    {
        using var tab = await OpenWholeAsync();
        var session = tab.EnsureEditor();
        _ = session.Editor;
        var range = Whole(FirstParagraph(tab));
        session.SetSelection(range);

        _ = session.ReadFormatFacts(range);
        _ = session.ReadFormatFacts(range);
        _ = session.ReadInspector(range);
        _ = session.ReadListFacts(range);
        _ = session.ReadSelectionFormat(range);
        var facts = session.FactReads;
        var inspected = session.InspectorReads;
        var listed = session.ListReads;
        Assert.Equal(1, facts);

        tab.Docx.Model.Edits.RaiseDerived();
        _ = session.ReadFormatFacts(range);
        _ = session.ReadInspector(range);
        _ = session.ReadListFacts(range);

        Assert.Equal(facts + 1, session.FactReads);
        Assert.Equal(inspected + 1, session.InspectorReads);
        Assert.Equal(listed + 1, session.ListReads);
    }

    [AvaloniaFact]
    public async Task TheOutlineRowsFollowTheNewTextOfAHeading()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Thesis);
        var model = tab.Docx.Model;
        var first = OutlineSource.ReadHeadings(model)[0];
        var heading = first.Paragraph;
        var old = first.Entry.Text;

        ReplaceTextAsARefreshDoes(tab, heading, "New");

        var rows = OutlineSource.Read(model);
        Assert.Contains(rows, row => row.Text == "New");
        Assert.DoesNotContain(rows, row => row.Text == old);
    }

    [AvaloniaFact]
    public async Task TheEffectiveFormatIndexIsWalkedAgainAfterADerivedChange()
    {
        using var tab = await OpenWholeAsync(WindowFixture.Thesis);
        var model = tab.Docx.Model;
        var format = new LayoutEffectiveFormat(model);
        var table = model.Body.Blocks.OfType<Table>().FirstOrDefault();
        Assert.NotNull(table);
        var cellParagraph = ((TableCell)((TableRow)table!.Rows[0]).Items[0]).Blocks.OfType<Paragraph>().First();
        var run = cellParagraph.Inlines.OfType<Run>().First();

        _ = format.ForRun(StoryRef.Body, cellParagraph, run);
        _ = format.ForRun(StoryRef.Body, cellParagraph, run);
        var walks = format.StoryWalks;
        Assert.Equal(1, walks);

        model.Edits.RaiseDerived();
        _ = format.ForRun(StoryRef.Body, cellParagraph, run);

        Assert.Equal(walks + 1, format.StoryWalks);
    }

    [AvaloniaFact]
    public async Task ARefreshOfACleanDocumentLeavesItClean()
    {
        using var tab = await OpenWholeAsync();

        ReplaceTextAsARefreshDoes(tab, FirstParagraph(tab), "refreshed");
        tab.UpdateDirty();

        Assert.False(tab.Docx.Model.Edits.IsDirty);
        Assert.False(tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task ARefreshCausesNoRecoveryCopyAndNoDirtyMark()
    {
        using var tab = await OpenWholeAsync();
        var model = tab.Docx.Model;
        var folder = Directory.CreateTempSubdirectory("raffica-derived-").FullName;
        try
        {
            var clock = new ManualRecoveryClock();
            var store = new RecoveryStore(Path.Combine(folder, "recovery"));
            using var scheduler = new AutosaveScheduler(store, clock, () => true);
            var subject = new WriteTabRecoverySubject(tab);
            var session = tab.EnsureEditor();
            var paragraph = FirstParagraph(tab);
            Assert.NotNull(session.Apply(e => e.InsertText(new TextRange(new TextPosition(StoryRef.Body, paragraph, 0), new TextPosition(StoryRef.Body, paragraph, 0)), "x"), 0));
            scheduler.Track(subject);
            clock.Fire();
            await SaveHarness.AwaitAsync(scheduler.WhenIdleAsync());
            var entry = Assert.Single(store.List().Entries);
            var written = File.GetLastWriteTimeUtc(Path.Combine(store.Folder, entry.Id + ".docx"));
            var revision = subject.Revision;

            ReplaceTextAsARefreshDoes(tab, FirstParagraph(tab), "refreshed");
            await Task.Delay(50);
            clock.Fire();
            await SaveHarness.AwaitAsync(scheduler.WhenIdleAsync());

            Assert.Equal(revision, subject.Revision);
            Assert.True(model.Edits.IsDirty);
            Assert.Equal(written, File.GetLastWriteTimeUtc(Path.Combine(store.Folder, entry.Id + ".docx")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
