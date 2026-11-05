using Avalonia.Headless.XUnit;
using Raffica.Pdf.Annotating;
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

        Assert.Null(window.Window.FormsOf(tab));
        Assert.Null(window.Window.ActiveView!.FormOverlay);
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
