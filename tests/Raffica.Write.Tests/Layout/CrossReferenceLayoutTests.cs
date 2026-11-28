using System.Diagnostics;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Raffica.Write.Tests.Support;
using Xunit;

namespace Raffica.Write.Tests.Layout;

/// <summary>A cross-reference from the last page of the thesis to a heading on the first (milestone 4.5b): the step holds and re-plans the two paragraphs it changes and nothing between them.</summary>
public sealed class CrossReferenceLayoutTests
{
    private static string Root => Path.Combine(Raffica.TestSupport.TestData.Root, "testdata", "write-corpus");

    [Fact]
    public async Task AReferenceFromTheLastPageToTheFirstTouchesTwoParagraphsAndEqualsAFullLayout()
    {
        using var file = DocxFile.Open(Path.Combine(Root, "thesis", "thesis.docx"));
        var model = file.Model;
        var options = new LayoutOptions();
        var layout = await SectionOracleTests.LaidAsync(model);
        var editor = new TextEditor(model, (s, p) => layout.ProjectionHost(s, p));
        var host = new FakeFieldHost(model);
        var target = CrossReferences.Targets(model, host, CrossRefTargetKind.Heading)[0];
        var body = model.Body.Blocks;
        var last = body.OfType<Paragraph>().Last(p => ParagraphProjection.Build(p, ProjectionHost.Plain).Text.Length > 0);
        var blocks = body.ToList();
        var atIndex = blocks.IndexOf(last);
        var start = ModelSnapshot.Of(model).ToString(System.Xml.Linq.SaveOptions.DisableFormatting);

        var clock = Stopwatch.StartNew();
        var result = editor.InsertCrossReference(new TextPosition(StoryRef.Body, last, 0), target, CrossRefKind.NumberOnly, host);
        Assert.NotNull(result);
        var applied = 0;
        foreach (var change in result.ChangesInApplyOrder())
        {
            layout.ApplyEdit(change, Math.Max(1, (layout.Locate(Math.Min(change.First, Math.Max(0, body.Count - 1)))?.Page ?? 0) + 1));
            applied++;
        }

        var insertMs = clock.Elapsed.TotalMilliseconds;
        await layout.LayOutRestAsync(null, CancellationToken.None);
        var info = $"cross-reference thesis: history {editor.History.UndoBytes} bytes, {applied} block changes applied, insert and ApplyEdit {insertMs:F1} ms, blocks {blocks.Count}";
        TestContext.Current.SendDiagnosticMessage(info);

        // Two paragraphs changed, every other block is the very object it was, and the history holds clones of those two only.
        Assert.True(applied == 2, info);
        var changed = Enumerable.Range(0, blocks.Count).Where(i => !ReferenceEquals(blocks[i], body[i])).ToList();
        Assert.Empty(changed);
        Assert.True(editor.History.UndoBytes < 100_000, "the history holds " + editor.History.UndoBytes + " bytes");
        LayoutOracle.AssertEqualToFull(layout, model, options, "after the reference");

        // Undo and redo are as exact: the bytes of both paragraphs come back and the layout follows.
        var after = ModelSnapshot.Of(model).ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        var undone = editor.History.Undo();
        Assert.NotNull(undone);
        foreach (var change in undone.ChangesInApplyOrder())
        {
            layout.ApplyEdit(change, Math.Max(1, (layout.Locate(Math.Min(change.First, Math.Max(0, body.Count - 1)))?.Page ?? 0) + 1));
        }

        await layout.LayOutRestAsync(null, CancellationToken.None);
        Assert.Equal(start, ModelSnapshot.Of(model).ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
        LayoutOracle.AssertEqualToFull(layout, model, options, "after undo");
        var redone = editor.History.Redo();
        Assert.NotNull(redone);
        foreach (var change in redone.ChangesInApplyOrder())
        {
            layout.ApplyEdit(change, Math.Max(1, (layout.Locate(Math.Min(change.First, Math.Max(0, body.Count - 1)))?.Page ?? 0) + 1));
        }

        await layout.LayOutRestAsync(null, CancellationToken.None);
        Assert.Equal(after, ModelSnapshot.Of(model).ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
        LayoutOracle.AssertEqualToFull(layout, model, options, "after redo");
        Assert.True(atIndex > 0);
    }
}
