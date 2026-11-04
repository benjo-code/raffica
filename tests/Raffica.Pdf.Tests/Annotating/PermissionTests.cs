using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Annotating;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Annotating;

public sealed class PermissionTests
{
    // The r3 file forbids annotating to whoever opens it with the user password.
    private static async Task<PdfTab> OpenForbiddenAsync(WindowFixture window)
    {
        var session = new ViewerSession(new PdfEngine());
        await session.OpenAsync(TestData.Pdf("encrypted_hello_world_r3.pdf"), "hôtel");
        Assert.Equal(ViewerState.Ready, session.State);
        var tab = window.Window.Workspace.Add(session, TestData.Pdf("encrypted_hello_world_r3.pdf"));
        ViewFixture.Pump();
        await ViewFixture.WaitAsync(() => window.Window.AnnotateAllowedOf(tab) is not null);
        return tab;
    }

    [AvaloniaFact]
    public async Task ForbiddenDocumentRefusesTheModeWithACard()
    {
        using var window = WindowFixture.Create();
        var tab = await OpenForbiddenAsync(window);
        Assert.False(window.Window.AnnotateAllowedOf(tab));

        // A path around the disabled commands (the palette, a key) still ends in the refusal.
        window.Window.Tools.Choose(AnnotateTool.Highlight);
        ViewFixture.Pump();

        Assert.Equal(AnnotateTool.None, window.Window.Tools.Tool);
        var card = Assert.IsType<Dialog>(window.Window.Host.ActiveDialog);
        var texts = card.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(PdfStrings.AnnotateLockedReason, texts);
        Assert.False(window.Window.ModeIndicatorOf(tab)!.IsVisible);
    }

    [AvaloniaFact]
    public async Task SwitchingToAForbiddenTabEndsTheModeQuietly()
    {
        using var window = WindowFixture.Create();
        var forbidden = await OpenForbiddenAsync(window);
        var allowed = await window.OpenAsync("text-rich.pdf");
        await ViewFixture.WaitAsync(() => window.Window.AnnotateAllowedOf(allowed) is not null);
        Assert.True(window.Window.AnnotateAllowedOf(allowed));
        window.Window.Tools.Choose(AnnotateTool.Highlight);
        Assert.Equal(AnnotateTool.Highlight, window.Window.Tools.Tool);
        Assert.False(window.Window.Host.IsOpen);

        window.Window.Workspace.Select(forbidden);
        ViewFixture.Pump();

        Assert.Equal(AnnotateTool.None, window.Window.Tools.Tool);
        Assert.False(window.Window.Host.IsOpen);
    }
}
