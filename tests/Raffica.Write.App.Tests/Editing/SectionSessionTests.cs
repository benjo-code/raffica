using System.Xml.Linq;
using Avalonia.Headless.XUnit;
using Raffica.Write.Documents;
using Raffica.Write.Docx;
using Raffica.Write.Editing;
using Raffica.Write.Layout;
using Raffica.Write.Model;
using Xunit;

namespace Raffica.WriteApp.Tests.Editing;

/// <summary>The session side of a section change: a result with <see cref="EditResult.Sections"/> (and so an undo or redo of one) starts the layout over instead of applying block changes.</summary>
public sealed class SectionSessionTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    // What a page-setup command will do: the width of the final section changes, the step is recorded with the clones. Test-only, until the real commands exist.
    private static EditResult WidenFinalSection(TextEditor editor, WordDocument model, TextPosition caret)
    {
        var revision = editor.History.BeginOperation();
        var live = model.Body.FinalSection!;
        var old = live.Clone();
        live.Get(W + "pgSz")!.SetAttributeValue(W + "w", "5000");
        model.Edits.Raise();
        var change = new SectionChange([new SectionSwap(live, old, live.Clone())], null, null);
        model.Edits.Attribute([.. change.CarrierChanges(model)]);
        var result = new EditResult(caret, [], Sections: change);
        editor.History.Record(result, new EditHistory.Point(0, 0), revision, null);
        return result;
    }

    [AvaloniaFact]
    public async Task ASectionResultResectionsTheLayoutAndSoDoUndoAndRedo()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var tab = await window.Flow.NewDocumentAsync(PaperSize.A4, CancellationToken.None);
        WindowFixture.Pump();
        Assert.NotNull(tab);
        tab.Runner.Start();
        await WindowFixture.WaitAsync(() => tab.Runner.FullModelReady || tab.Runner.IsFaulted);
        Assert.True(tab.Runner.FullModelReady);
        var session = tab.EnsureEditor();
        var model = tab.Docx.Model;
        var layout = tab.Runner.PageLayout;
        var original = layout.Page(0).Width;
        var restarts = layout.Stats.Restarts;
        var caret = session.Selection!.Value.Start;

        var result = session.Apply(e => WidenFinalSection(e, model, caret), 0);

        Assert.NotNull(result?.Sections);
        Assert.True(layout.Stats.Restarts > restarts);
        Assert.True(tab.IsDirty);
        Assert.Equal(250f, layout.Page(0).Width);
        Assert.True(session.CanUndo);

        restarts = layout.Stats.Restarts;
        Assert.NotNull(session.Apply(e => e.History.Undo(), 0)?.Sections);
        Assert.True(layout.Stats.Restarts > restarts);
        Assert.Equal(original, layout.Page(0).Width);

        restarts = layout.Stats.Restarts;
        Assert.NotNull(session.Apply(e => e.History.Redo(), 0)?.Sections);
        Assert.True(layout.Stats.Restarts > restarts);
        Assert.Equal(250f, layout.Page(0).Width);
    }
}
