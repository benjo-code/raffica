using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Printing;
using Raffica.Shell.Controls;
using Raffica.TestSupport;

namespace Raffica.Pdf.Tests.Printing;

/// <summary>A window with a dialog host and a slot for the job card, a presenter over them, and tabs of test files.</summary>
internal sealed class PrintHost : IDisposable
{
    private readonly PdfEngine _engine = new();
    private readonly List<PdfTab> _tabs = [];

    public PrintHost()
    {
        Host = new DialogHost { Content = new Border() };
        Slot = new ContentControl();
        Window = new Window { Content = new DockPanel { Children = { Host } }, Width = 800, Height = 600 };
        Window.Show();
        Presenter = new PrintPresenter(new DialogQueue(Host), card => Slot.Content = card);
        Pump();
    }

    public Window Window { get; }

    public DialogHost Host { get; }

    public ContentControl Slot { get; }

    public PrintPresenter Presenter { get; }

    public static void Pump()
    {
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }
    }

    /// <summary>Opens a test file as a tab that is not in any workspace.</summary>
    public async Task<PdfTab> OpenAsync(string file, string? password = null)
    {
        var session = new ViewerSession(_engine);
        await session.OpenAsync(TestData.Pdf(file), password);
        var tab = new PdfTab(session, TestData.Pdf(file));
        _tabs.Add(tab);
        return tab;
    }

    /// <summary>Runs the dispatcher until <paramref name="condition"/> holds, for at most ten seconds.</summary>
    public static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The condition did not become true in time.");
            }

            Pump();
            await Task.Delay(10);
        }
    }

    public static T Find<T>(Control root, string name)
        where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    /// <summary>The text of the dialog that is open (its message), or null when none is.</summary>
    public string? MessageText => Host.ActiveDialog is { } dialog ? Find<TextBlock>(dialog, "PART_Message").Text : null;

    public void Dispose()
    {
        Window.Close();
        foreach (var tab in _tabs)
        {
            tab.Dispose();
        }
    }
}
