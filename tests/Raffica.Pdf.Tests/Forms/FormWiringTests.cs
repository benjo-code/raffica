using Avalonia.Headless.XUnit;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Forms;

public sealed class FormWiringTests
{
    [AvaloniaFact]
    public async Task ADocumentWithoutAFormGetsNoFormPartsAndNoOverlay()
    {
        using var window = WindowFixture.Create();
        var tab = await window.OpenAsync("text-rich.pdf");

        // Refreshing the command states (the rules ask about the form) must not create a session for a document without a form.
        Assert.Null(window.Window.FormsOf(tab));
        Assert.Null(window.Window.ActiveView!.FormOverlay);
        window.Window.Ribbon.RefreshCommandStates();
        ViewFixture.Pump();
        foreach (var id in new[] { PdfCommandIds.FormsNext, PdfCommandIds.FormsPrevious, PdfCommandIds.FormsNextRequired, PdfCommandIds.FormsHighlight })
        {
            Assert.False(window.Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id).IsEnabled);
        }

        Assert.Null(tab.FormsIfCreated);
        Assert.False(tab.Forms.HasForm);
    }

    [AvaloniaFact]
    public async Task ADocumentWithAFormGetsPartsThatGoWithTheTab()
    {
        using var window = WindowFixture.Create();
        var tab = await window.OpenAsync("forms-basic.pdf");
        var view = window.Window.ActiveView!;

        var parts = window.Window.FormsOf(tab);
        Assert.NotNull(parts);
        Assert.Same(parts.Markers, view.FormOverlay);

        Assert.True(await window.Window.Workspace.CloseAsync(tab));
        ViewFixture.Pump();
        Assert.Null(window.Window.FormsOf(tab));
        Assert.Null(view.FormOverlay);
    }

    [AvaloniaFact]
    public async Task ChoosingAToolHidesTheMarkersOfTheWindow()
    {
        using var window = WindowFixture.Create();
        var tab = await window.OpenAsync("forms-basic.pdf");
        var view = window.Window.ActiveView!;
        await ViewFixture.WaitAsync(() => view.FirstSharpPageShown);
        await ViewFixture.WaitAsync(() => tab.Forms.IsLoaded(0));
        var parts = window.Window.FormsOf(tab)!;
        parts.Focus.Set(tab.Forms.FieldsOn(0)[0].Key);
        Assert.NotEmpty(parts.Markers.Plan(view));

        window.Window.Tools.Choose(AnnotateTool.Select);
        ViewFixture.Pump();
        Assert.Empty(parts.Markers.Plan(view));

        window.Window.Tools.End();
        ViewFixture.Pump();
        Assert.NotEmpty(parts.Markers.Plan(view));
    }
}
