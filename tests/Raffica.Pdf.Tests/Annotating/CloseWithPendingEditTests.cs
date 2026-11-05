using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Raffica.Core.Annotations;
using Raffica.Core.Text;
using Raffica.Pdf.Tests.Viewing;
using Xunit;

namespace Raffica.Pdf.Tests.Annotating;

public sealed class CloseWithPendingEditTests
{
    [AvaloniaFact]
    public async Task ClosingRightAfterAMarkupStillAsks()
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var fixture = WindowFixture.Create();
        var tab = await fixture.Window.Flow.OpenAsync(copy.Path) ?? throw new InvalidOperationException("did not open");
        ViewFixture.Pump();
        Assert.False(tab.IsDirty);

        // Not awaited: the close finds the editor busy and the tab not yet marked dirty.
        var apply = tab.Annotations.Editor.ApplyMarkupAsync(0, AnnotationKind.Highlight, [new PageRect(10, 10, 100, 30)], new AnnotationColor(255, 255, 0), null, null);
        var closing = fixture.Window.Workspace.CloseAsync(tab);
        for (var i = 0; i < 400 && fixture.Window.Host.ActiveDialog is null; i++)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
            ViewFixture.Pump();
        }

        Assert.NotNull(fixture.Window.Host.ActiveDialog);
        var footer = Assert.IsType<StackPanel>(fixture.Window.Host.ActiveDialog!.Footer);
        Assert.Contains(footer.Children.OfType<Button>(), b => b.Name == "PART_DontSave");
        Assert.NotNull(await apply);
        Assert.True(tab.IsDirty);

        var cancel = footer.Children.OfType<Button>().Single(b => b.Name == "PART_Cancel");
        cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        ViewFixture.Pump();
        Assert.False(await closing);
    }

    [AvaloniaFact]
    public async Task ClosingWithAPendingDraftAsksOnceTheDraftEnds()
    {
        using var copy = new TempCopy("text-rich.pdf");
        using var fixture = WindowFixture.Create();
        var tab = await fixture.Window.Flow.OpenAsync(copy.Path) ?? throw new InvalidOperationException("did not open");
        ViewFixture.Pump();

        // A stroke is in flight: the close waits for it and neither asks nor finishes meanwhile.
        var stroke = tab.Annotations.BeginPending();
        var closing = fixture.Window.Workspace.CloseAsync(tab);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        ViewFixture.Pump();
        Assert.False(closing.IsCompleted);
        Assert.Null(fixture.Window.Host.ActiveDialog);

        // The stroke ends with a mark: now the tab is dirty and the close asks.
        Assert.NotNull(await AnnotatedFile.HighlightAsync(tab.Annotations));
        stroke.Dispose();
        for (var i = 0; i < 400 && fixture.Window.Host.ActiveDialog is null; i++)
        {
            await Task.Delay(25, TestContext.Current.CancellationToken);
            ViewFixture.Pump();
        }

        var footer = Assert.IsType<StackPanel>(fixture.Window.Host.ActiveDialog!.Footer);
        Assert.Contains(footer.Children.OfType<Button>(), b => b.Name == "PART_DontSave");
        footer.Children.OfType<Button>().Single(b => b.Name == "PART_Cancel").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        ViewFixture.Pump();
        Assert.False(await closing);
    }
}
