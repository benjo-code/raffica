using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Core.Text;
using Raffica.Core.Viewing;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Redacting;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Text;
using Raffica.Shell.Controls;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Redacting;

/// <summary>
/// A <see cref="RedactApplyFlow"/> with its own pieces in a headless window with a dialog host: a temp copy of a test file as the tab (opened with an
/// optional password, its redact mode on), a scripted file picker, a clock the test moves, and a recording of the rebuilds and of the paths that changed.
/// The marks are made the way the Find panel makes them. Nothing is written outside the temp folder.
/// </summary>
internal sealed class RedactApplyRig : IDisposable
{
    private readonly PrintHost _host = new();
    private readonly TempCopy _copy;
    private readonly PdfEngine _engine = new();

    private RedactApplyRig(TempCopy copy, PdfTab tab)
    {
        _copy = copy;
        Tab = tab;
        Flow = new RedactApplyFlow(
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
            Time,
            workspace: Workspace);
    }

    public Workspace Workspace { get; } = new(new CloseGuard(_ => Task.FromResult(CloseChoice.DontSave), _ => Task.FromResult(true)));

    /// <summary>Opens a file as another tab of the rig's workspace (the tab of the rig itself is not in it).</summary>
    public async Task<PdfTab> AddOtherAsync(string path)
    {
        var session = new ViewerSession(new PdfEngine());
        await session.OpenAsync(path, null);
        return Workspace.Add(session, path);
    }

    public PdfTab Tab { get; }

    public RedactApplyFlow Flow { get; }

    public ManualTime Time { get; } = new();

    public Queue<string?> Picks { get; } = new();

    /// <summary>Runs while the file picker is open (a test edits the plan there); null: nothing.</summary>
    public Func<Task>? BeforePick { get; set; }

    public List<(PdfTab Tab, int Page)> Rebuilds { get; } = [];

    public List<string> PathChanges { get; } = [];

    public int CurrentPage { get; set; }

    public Func<PdfTab, Task<bool>> SaveMarks { get; set; } = _ => Task.FromResult(true);

    public RedactSession Session => Tab.Redact;

    public string Path => _copy.Path;

    public string Folder => _copy.Folder;

    public DialogHost Host => _host.Host;

    public Window Window => _host.Window;

    public static async Task<RedactApplyRig> OpenAsync(string file = "redact-covered.pdf", string? password = null)
    {
        var copy = new TempCopy(file);
        var session = new ViewerSession(new PdfEngine());
        await session.OpenAsync(copy.Path, password);
        var tab = new PdfTab(session, copy.Path);
        tab.Redact.Enter();
        return new RedactApplyRig(copy, tab);
    }

    public string CopyOf(string file, string? name = null)
    {
        var target = System.IO.Path.Combine(Folder, name ?? file);
        File.Copy(TestData.Pdf(file), target, overwrite: true);
        return target;
    }

    /// <summary>Marks every match of a term (what Mark all of the Find panel does).</summary>
    public async Task<int> MarkTermAsync(string term)
    {
        using var find = new FindSession(Session);
        Assert.True(await find.RunAsync(term, default));
        find.Acknowledge();
        Assert.True(find.MarkAll().IsDone);
        return Session.Plan.Marks.Count;
    }

    public void MarkArea(int page, PageRect area) => Assert.True(Session.AddArea(page, area).IsDone);

    /// <summary>Waits for the card that has the part <paramref name="name"/> (the job cards of the steps before it go by) and returns it.</summary>
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

    /// <summary>Runs the apply, answers the Apply card with <paramref name="choice"/> (the original-file button is armed first) and returns the run.</summary>
    public async Task<Task<bool>> StartAsync(string choice = "PART_SaveAs", ApplyTarget target = ApplyTarget.Choose)
    {
        var run = Flow.RunAsync(Tab, target);
        var card = await CardWithAsync(choice);
        Time.Advance(ArmingLock.DelayMilliseconds + 100);
        Click(card, choice);
        return run;
    }

    /// <summary>The texts of the labels and ticks of a card.</summary>
    public static IReadOnlyList<string> TextsOf(Dialog card) =>
        [.. card.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? string.Empty), .. card.GetVisualDescendants().OfType<CheckBox>().Select(c => c.Content as string ?? string.Empty)];

    public static bool Has(Dialog dialog, string name) => dialog.GetVisualDescendants().OfType<Control>().Any(c => c.Name == name);

    public void Dispose()
    {
        foreach (var other in Workspace.Tabs.ToList())
        {
            other.Dispose();
        }

        _host.Dispose();
        Tab.Dispose();
        _copy.Dispose();
    }
}
