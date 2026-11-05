using Avalonia.Headless.XUnit;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Viewing;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Commands;

/// <summary>When Save, Save as and Print are enabled in the ribbon of the window (the rules wired through <c>PdfWindow.CreateRules</c>).</summary>
public sealed class SaveCommandTests
{
    private static bool IsEnabled(WindowFixture window, string id) =>
        window.Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id).IsEnabled;

    [AvaloniaFact]
    public async Task SaveFollowsTheDirtyStateAndSaveAsNeedsOnlyADocument()
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var window = WindowFixture.Create(realSave: true);
        Assert.False(IsEnabled(window, PdfCommandIds.Save));
        Assert.False(IsEnabled(window, PdfCommandIds.SaveAs));

        var tab = await window.Window.Flow.OpenAsync(copy.Path) ?? throw new InvalidOperationException("did not open");
        ViewFixture.Pump();

        Assert.False(IsEnabled(window, PdfCommandIds.Save));
        Assert.True(IsEnabled(window, PdfCommandIds.SaveAs));

        Assert.NotNull(await AnnotatedFile.HighlightAsync(tab.Annotations));
        await tab.Annotations.FlushAsync();
        ViewFixture.Pump();

        Assert.True(IsEnabled(window, PdfCommandIds.Save));
        Assert.True(IsEnabled(window, PdfCommandIds.SaveAs));
    }

    [AvaloniaFact]
    public async Task SaveSaveAsAndPrintAreDisabledWhileASaveRunsAndRefreshWhenItEnds()
    {
        using var copy = new TempCopy("text-rich.pdf");
        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        using var window = WindowFixture.Create(saveRunner: async (_, _, _, _, token) =>
        {
            started.SetResult();
            await gate.Task.WaitAsync(token);
            throw new OperationCanceledException();
        });
        var tab = await window.Window.Flow.OpenAsync(copy.Path) ?? throw new InvalidOperationException("did not open");
        ViewFixture.Pump();
        Assert.NotNull(await AnnotatedFile.HighlightAsync(tab.Annotations));
        await tab.Annotations.FlushAsync();
        ViewFixture.Pump();
        Assert.True(IsEnabled(window, PdfCommandIds.Save));
        Assert.True(IsEnabled(window, PdfCommandIds.Print));

        var saving = window.Window.SaveActiveAsync();
        await started.Task;
        ViewFixture.Pump();

        Assert.False(IsEnabled(window, PdfCommandIds.Save));
        Assert.False(IsEnabled(window, PdfCommandIds.SaveAs));
        Assert.False(IsEnabled(window, PdfCommandIds.Print));

        gate.SetResult();
        Assert.False(await saving);
        ViewFixture.Pump();

        // The save ended as a cancel: the tab is still dirty, and the commands are back at once.
        Assert.True(IsEnabled(window, PdfCommandIds.Save));
        Assert.True(IsEnabled(window, PdfCommandIds.SaveAs));
        Assert.True(IsEnabled(window, PdfCommandIds.Print));
    }

    [AvaloniaFact]
    public async Task TheRibbonRefreshesRightAfterASaveCompletes()
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var window = WindowFixture.Create(realSave: true);
        var tab = await window.Window.Flow.OpenAsync(copy.Path) ?? throw new InvalidOperationException("did not open");
        ViewFixture.Pump();
        Assert.NotNull(await AnnotatedFile.HighlightAsync(tab.Annotations));
        await tab.Annotations.FlushAsync();
        ViewFixture.Pump();
        Assert.True(IsEnabled(window, PdfCommandIds.Save));
        Assert.True(IsEnabled(window, PdfCommandIds.Undo));

        Assert.True(await window.Window.SaveActiveAsync());

        // No pump in between: the state is right when the save returns.
        Assert.False(IsEnabled(window, PdfCommandIds.Save));
        Assert.False(IsEnabled(window, PdfCommandIds.Undo));
        Assert.True(IsEnabled(window, PdfCommandIds.SaveAs));
        Assert.True(IsEnabled(window, PdfCommandIds.Print));
    }
}
