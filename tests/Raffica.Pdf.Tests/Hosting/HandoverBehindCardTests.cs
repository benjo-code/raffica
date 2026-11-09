using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Tests.Viewing;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Hosting;

public sealed class HandoverBehindCardTests
{
    private static void Pump() => ViewFixture.Pump();

    private static async Task WaitAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(25);
            Pump();
        }

        Assert.True(condition());
    }

    private static void Click(PdfWindow window, string name)
    {
        var button = window.Host.ActiveDialog!.GetVisualDescendants().OfType<Button>().First(b => b.Name == name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
    }

    [AvaloniaFact]
    public async Task AHandedOverFileWaitsWhileACardIsOpenAndOpensWhenTheCardIsGone()
    {
        using var fixture = WindowFixture.Create();
        var window = fixture.Window;
        var path = TestData.Pdf("hello_world.pdf");
        window.ShowCrashCard(fixture.Folder);
        Pump();
        Assert.True(window.Host.IsOpen);

        window.OnHandover([path]);
        await WaitAsync(() => window.PendingHandover.Count == 1);
        await Task.Delay(100);
        Pump();

        Assert.Empty(window.Workspace.Tabs);
        Assert.True(window.Host.IsOpen);

        window.Host.Close();
        await WaitAsync(() => window.Workspace.Tabs.Count == 1);
        Assert.Equal(Path.GetFullPath(path), window.Workspace.Tabs[0].Path);
        Assert.Empty(window.PendingHandover);
    }

    [AvaloniaFact]
    public async Task ATabThatOpensBehindACardDoesNotTakeTheFocusFromIt()
    {
        using var fixture = WindowFixture.Create();
        var window = fixture.Window;
        window.ShowCrashCard(fixture.Folder);
        Pump();

        await fixture.OpenAsync("hello_world.pdf");
        for (var i = 0; i < 5; i++)
        {
            await Task.Delay(20);
            Pump();
        }

        var focused = window.FocusManager!.GetFocusedElement();
        Assert.IsNotType<Raffica.Pdf.Viewing.PageView>(focused);
        Assert.True(window.Host.IsOpen);
    }

    [AvaloniaFact]
    public async Task TheCloseQuestionWaitsBehindAnOpenCardInsteadOfAnsweringCancel()
    {
        using var fixture = WindowFixture.Create();
        var window = fixture.Window;
        var tab = await fixture.OpenAsync("hello_world.pdf");
        window.ShowCrashCard(fixture.Folder);
        Pump();

        var question = window.AskPlanAsync(tab);
        Pump();

        // Not answered, and the card that was open is still the one on screen.
        Assert.False(question.IsCompleted);
        Assert.True(window.Host.IsOpen);

        // The card is dismissed: the question appears, and the user's answer is the answer.
        window.Host.Close();
        await WaitAsync(() => window.Host.IsOpen);
        Assert.False(question.IsCompleted);
        Click(window, "PART_Cancel");
        Assert.Equal(CloseChoice.Cancel, await question.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

        // And the other answer too, so the queue did not just drop the card.
        var second = window.AskPlanAsync(tab);
        await WaitAsync(() => window.Host.IsOpen);
        Click(window, "PART_Discard");
        Assert.Equal(CloseChoice.DontSave, await second.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [AvaloniaFact]
    public async Task AHandoverDuringTheCloseQuestionOpensAfterTheQuestionIsAnswered()
    {
        using var fixture = WindowFixture.Create();
        var window = fixture.Window;
        var tab = await fixture.OpenAsync("hello_world.pdf");
        var path = TestData.Pdf("text-rich.pdf");

        // The window asks about the tab (as its close does); a file arrives while the card is open.
        var question = window.AskPlanAsync(tab);
        await WaitAsync(() => window.Host.IsOpen);
        window.OnHandover([path]);
        await WaitAsync(() => window.PendingHandover.Count == 1);
        Assert.Single(window.Workspace.Tabs);

        Click(window, "PART_Cancel");
        Assert.Equal(CloseChoice.Cancel, await question);

        await WaitAsync(() => window.Workspace.Tabs.Count == 2);
    }
}
