using Avalonia.Headless.XUnit;
using Raffica.Core.Annotations;
using Raffica.Core.Pdf;
using Raffica.Core.Text;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Ocr;
using Raffica.Pdf.Tests.TextEditing;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Pdf.Tests.Commands;

/// <summary>
/// The annotate tool is the window's, the other modes are the tab's: a tool chosen in another tab (or through the marks pane) must not run
/// together with Redact, Edit text or a text recognition of the tab that is shown.
/// </summary>
public sealed class StrayToolTests
{
    private static CommandDefinition Command(PdfWindow window, string id) =>
        window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id);

    [AvaloniaFact]
    public async Task AToolChosenInAnotherTabEndsWhenTheRedactTabIsShownAgain()
    {
        using var f = WindowFixture.Create();
        var a = await f.OpenAsync("pages-marked.pdf");
        await ViewFixture.WaitAsync(() => f.Window.ViewOf(a)?.FirstSharpPageShown == true);
        Command(f.Window, "redact.mode").Run!();
        ViewFixture.Pump();
        Assert.True(a.Redact.IsActive);

        var b = await f.OpenAsync("text-basic.pdf");
        await ViewFixture.WaitAsync(() => f.Window.ViewOf(b)?.FirstSharpPageShown == true);
        f.Window.Tools.Choose(AnnotateTool.Highlight);
        Assert.Equal(AnnotateTool.Highlight, f.Window.Tools.Tool);

        f.Window.Workspace.Select(a);
        ViewFixture.Pump();
        Assert.Equal(AnnotateTool.None, f.Window.Tools.Tool);
        Assert.True(a.Redact.IsActive);
    }

    [AvaloniaFact]
    public async Task AToolChosenInAnotherTabEndsWhenTheTextEditTabIsShownAgain()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.EnterModeAsync();

        var b = await rig.Fixture.OpenAsync("text-basic.pdf");
        await ViewFixture.WaitAsync(() => rig.Window.ViewOf(b)?.FirstSharpPageShown == true);
        rig.Window.Tools.Choose(AnnotateTool.Highlight);
        Assert.Equal(AnnotateTool.Highlight, rig.Window.Tools.Tool);

        rig.Window.Workspace.Select(rig.Tab);
        ViewFixture.Pump();
        Assert.Equal(AnnotateTool.None, rig.Window.Tools.Tool);
        Assert.True(rig.Tab.TextEdit.IsActive);
    }

    [AvaloniaFact]
    public async Task AToolChosenInAnotherTabEndsWhileTheOcrRunOfTheShownTabGoes()
    {
        using var rig = await OcrRig.OpenAsync();
        rig.Engines!.HoldAt(1);
        var run = rig.Window.StartOcrRunAsync(rig.Tab, new Raffica.Core.Ocr.OcrOptions("eng", Jobs: 1));
        await rig.UntilPagesAsync(1);
        Assert.True(rig.Tab.OcrIfCreated!.IsActive);

        var b = await rig.Fixture.OpenAsync("text-basic.pdf");
        await ViewFixture.WaitAsync(() => rig.Window.ViewOf(b)?.FirstSharpPageShown == true);
        rig.Window.Tools.Choose(AnnotateTool.Highlight);
        Assert.Equal(AnnotateTool.Highlight, rig.Window.Tools.Tool);

        rig.Window.Workspace.Select(rig.Tab);
        ViewFixture.Pump();
        Assert.Equal(AnnotateTool.None, rig.Window.Tools.Tool);

        rig.Window.CancelOcr();
        rig.Engines.Release();
        var card = await rig.CardWithAsync("PART_Discard");
        OcrRig.Click(card, "PART_Discard");
        await run;
    }

    [AvaloniaFact]
    public async Task AMarksPaneClickInTheTextEditModeChoosesNoTool()
    {
        using var rig = await TextEditRig.OpenAsync("text-edit-standard.pdf");
        await rig.Tab.Annotations.Editor.ApplyDrawingAsync(0, DrawingGeometry.Rectangle(new PageRect(40, 40, 120, 90), 2), new AnnotationColor(220, 38, 38), null, null);
        rig.Window.ToggleMarksPane();
        ViewFixture.Pump();
        var pane = rig.Window.MarksOf(rig.Tab)!;
        await ViewFixture.WaitAsync(() => pane.RowCount > 0);
        var entry = Enumerable.Range(0, pane.RowCount).Select(i => pane.EntryAt(i)).First(e => e is not null)!;

        await rig.EnterModeAsync();
        await pane.ChooseAsync(entry);
        ViewFixture.Pump();

        Assert.Equal(AnnotateTool.None, rig.Window.Tools.Tool);
        Assert.True(rig.Tab.TextEdit.IsActive);
    }

    // Defence in depth: the command is disabled in the redact mode, and a call that gets around it (a key, the palette) starts nothing.
    [AvaloniaFact]
    public async Task AForcedOrganizeCallWhileRedactIsOnStartsNothing()
    {
        using var f = WindowFixture.Create();
        var tab = await f.OpenAsync("pages-marked.pdf");
        await ViewFixture.WaitAsync(() => f.Window.ViewOf(tab)?.FirstSharpPageShown == true);
        Command(f.Window, "redact.mode").Run!();
        ViewFixture.Pump();
        Assert.True(tab.Redact.IsActive);

        f.Window.ToggleOrganize();
        await Task.Delay(100, TestContext.Current.CancellationToken);
        ViewFixture.Pump();

        Assert.True(tab.OrganizeIfCreated is not { IsActive: true });
        Assert.True(tab.Redact.IsActive);
    }
}
