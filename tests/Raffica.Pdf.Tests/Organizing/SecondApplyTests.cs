using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Text;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Organizing;

/// <summary>An apply rebuilds the tab and forgets its organize session: the mode can be entered again over the written file and applied once more.</summary>
public sealed class SecondApplyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CommandDefinition Command(PdfWindow window, string id) =>
        window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id);

    private static async Task ApplyAsync(WindowFixture fixture, ManualTime time)
    {
        var apply = fixture.Window.ApplyActiveAsync();
        await PrintHost.UntilAsync(() => fixture.Window.Host.ActiveDialog is { } d && d.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "PART_SaveAs"));
        var card = fixture.Window.Host.ActiveDialog!;
        time.Advance(ArmingLock.DelayMilliseconds + 100);
        PrintHost.Find<Button>(card, "PART_SaveAs").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(await apply.WaitAsync(TimeSpan.FromSeconds(60), Ct));
        PrintHost.Pump();
    }

    [AvaloniaFact]
    public async Task TheModeCanBeEnteredAgainAfterAnApplyAndAppliedOnceMore()
    {
        var folder = Directory.CreateTempSubdirectory("raffica-second-").FullName;
        try
        {
            var first = Path.Combine(folder, "first.pdf");
            var second = Path.Combine(folder, "second.pdf");
            var target = first;
            var time = new ManualTime();
            using var fixture = WindowFixture.Create(time: time, savePicker: _ => Task.FromResult<string?>(target));
            var window = fixture.Window;
            var tab = await fixture.OpenAsync("pages-marked.pdf");
            await ViewFixture.WaitAsync(() => window.ViewOf(tab)?.FirstSharpPageShown == true);
            var created = 0;
            tab.OrganizeCreated += _ => created++;

            // The first round: rotate one page and apply under a new name.
            window.ToggleOrganize();
            await ViewFixture.WaitAsync(() => tab.OrganizeIfCreated is { IsActive: true });
            var firstSession = tab.Organize;
            Assert.Equal(1, created);
            tab.Organize.Selection.Select(1);
            Assert.True(tab.Organize.Rotate(90).IsDone);
            await ApplyAsync(fixture, time);
            Assert.Equal(first, tab.Path);
            Assert.False(tab.IsDirty);
            Assert.True(tab.OrganizeIfCreated is null);
            Assert.Equal(6, tab.Session.Document!.PageCount);

            // The second round over the written file: a new session, announced again, with the commands working.
            window.ToggleOrganize();
            await ViewFixture.WaitAsync(() => tab.OrganizeIfCreated is { IsActive: true });
            Assert.NotSame(firstSession, tab.Organize);
            Assert.Equal(2, created);
            Assert.True(tab.Organize.Plan.IsIdentity);
            Assert.False(tab.IsDirty);
            Assert.True(Command(window, PdfCommandIds.DiscardPages).IsEnabled);
            Assert.False(Command(window, PdfCommandIds.ApplyPages).IsEnabled);

            tab.Organize.Selection.Select(2);
            ViewFixture.Pump();
            Assert.True(Command(window, PdfCommandIds.DeletePages).IsEnabled);
            Assert.True(tab.Organize.Delete().IsDone);
            ViewFixture.Pump();
            Assert.True(tab.IsDirty);
            Assert.True(Command(window, PdfCommandIds.ApplyPages).IsEnabled);

            target = second;
            await ApplyAsync(fixture, time);
            Assert.Equal(second, tab.Path);
            Assert.False(tab.IsDirty);
            Assert.True(tab.OrganizeIfCreated is null);
            Assert.Equal(5, tab.Session.Document!.PageCount);
            Assert.True(File.Exists(first));
            Assert.True(File.Exists(second));
        }
        finally
        {
            for (var i = 0; i < 5; i++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }

            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
