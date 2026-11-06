using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Organizing;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Text;
using Raffica.Shell.Controls;
using Raffica.TestSupport;

namespace Raffica.Pdf.Tests.Organizing;

/// <summary>
/// An <see cref="ApplyFlow"/> with its own pieces in a headless window with a dialog host: a temp copy of a test file as the tab (opened with an
/// optional password), a scripted file picker, a clock the test moves, a recording of the rebuilds and of the paths that changed, and helpers that
/// wait for the card with a button and press it. Nothing is written outside the temp folder.
/// </summary>
internal sealed class ApplyRig : IDisposable
{
    private readonly PrintHost _host = new();
    private readonly TempCopy _copy;
    private readonly PdfEngine _engine = new();

    private ApplyRig(TempCopy copy, PdfTab tab)
    {
        _copy = copy;
        Tab = tab;
        Flow = new ApplyFlow(
            new DialogQueue(_host.Host),
            t => SaveMarks(t),
            async _ =>
            {
                if (BeforePick is { } before)
                {
                    await before();
                }

                return Picks.Count > 0 ? Picks.Dequeue() : null;
            },
            () => _engine,
            _ => CurrentPage,
            (t, page) => Rebuilds.Add((t, page)),
            t => PathChanges.Add(t.Path!),
            null,
            null,
            Time);
    }

    public PdfTab Tab { get; }

    public ApplyFlow Flow { get; }

    public ManualTime Time { get; } = new();

    public Queue<string?> Picks { get; } = new();

    /// <summary>Runs while the file picker is open (a test edits the plan there); null: nothing.</summary>
    public Func<Task>? BeforePick { get; set; }

    public List<(PdfTab Tab, int Page)> Rebuilds { get; } = [];

    public List<string> PathChanges { get; } = [];

    public int CurrentPage { get; set; }

    public Func<PdfTab, Task<bool>> SaveMarks { get; set; } = _ => Task.FromResult(true);

    public OrganizeSession Session => Tab.Organize;

    public string Path => _copy.Path;

    public string Folder => _copy.Folder;

    public DialogHost Host => _host.Host;

    public static async Task<ApplyRig> OpenAsync(string file = "pages-marked.pdf", string? password = null)
    {
        var copy = new TempCopy(file);
        var session = new ViewerSession(new PdfEngine());
        await session.OpenAsync(copy.Path, password);
        var tab = new PdfTab(session, copy.Path);
        tab.Organize.Enter();
        return new ApplyRig(copy, tab);
    }

    public string CopyOf(string file, string? name = null)
    {
        var target = System.IO.Path.Combine(Folder, name ?? file);
        File.Copy(TestData.Pdf(file), target, overwrite: true);
        return target;
    }

    /// <summary>Waits for the card that has the button <paramref name="name"/> (the job cards of the steps before it go by) and returns it.</summary>
    public async Task<Dialog> CardWithAsync(string name)
    {
        await PrintHost.UntilAsync(() => _host.Host.ActiveDialog is { } d && Has(d, name));
        return _host.Host.ActiveDialog!;
    }

    /// <summary>Presses the button of a card (the click event, as the other card tests do).</summary>
    public static void Click(Dialog card, string name) =>
        PrintHost.Find<Button>(card, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>Waits for the card with the button, presses it and returns the card that was shown.</summary>
    public async Task<Dialog> PressAsync(string name)
    {
        var card = await CardWithAsync(name);
        Click(card, name);
        PrintHost.Pump();
        return card;
    }

    /// <summary>Runs the apply, answers the Apply card with <paramref name="choice"/> (the original-file button is armed first) and returns the result.</summary>
    public async Task<bool> ApplyAsync(string choice = "PART_SaveAs", ApplyTarget target = ApplyTarget.Choose)
    {
        var run = Flow.RunAsync(Tab, target);
        var card = await CardWithAsync(choice);
        Time.Advance(ArmingLock.DelayMilliseconds + 100);
        Click(card, choice);
        return await run.WaitAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>The texts of the labels of the open card.</summary>
    public static IReadOnlyList<string?> TextsOf(Dialog card) => card.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

    private static bool Has(Dialog dialog, string name) => dialog.GetVisualDescendants().OfType<Button>().Any(b => b.Name == name);

    public void Dispose()
    {
        _host.Dispose();
        Tab.Dispose();
        _copy.Dispose();
    }
}
