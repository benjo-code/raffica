using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Core.Viewing;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Organizing;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Printing;
using Raffica.Shell.Controls;
using Raffica.TestSupport;

namespace Raffica.Pdf.Tests.Organizing;

/// <summary>
/// A <see cref="SplitFlow"/> with its own pieces in a headless window with a dialog host: a temp copy of a test file as the tab, scripted pickers
/// (save path, folder), a record of the files the flow asked to open, and helpers that wait for the card with a button, type into its fields and
/// press it. Nothing is written outside the temp folder.
/// </summary>
internal sealed class SplitRig : IDisposable
{
    private readonly PrintHost _host = new();
    private readonly TempCopy _copy;
    private readonly PdfEngine _engine = new();

    private SplitRig(TempCopy copy, PdfTab tab)
    {
        _copy = copy;
        Tab = tab;
        Flow = new SplitFlow(
            new Raffica.Pdf.Dialogs.DialogQueue(_host.Host),
            (_, name) =>
            {
                SuggestedNames.Add(name);
                return Task.FromResult(SavePicks.Count > 0 ? SavePicks.Dequeue() : null);
            },
            _ =>
            {
                FolderAsked++;
                return Task.FromResult(FolderPicks.Count > 0 ? FolderPicks.Dequeue() : null);
            },
            () =>
            {
                EngineOnUiThread.Add(Avalonia.Threading.Dispatcher.UIThread.CheckAccess());
                return _engine;
            },
            _ => CurrentPage,
            async (t, token) =>
            {
                if (OutlineGate is { } gate)
                {
                    await gate;
                }

                return Outline is { } custom ? custom : await t.Session.Document!.GetOutlineAsync(token);
            },
            path =>
            {
                Opened.Add(path);
                return Task.CompletedTask;
            });
    }

    public PdfTab Tab { get; }

    public SplitFlow Flow { get; }

    public Queue<string?> SavePicks { get; } = new();

    public Queue<string?> FolderPicks { get; } = new();

    public List<string> SuggestedNames { get; } = [];

    public List<string> Opened { get; } = [];

    public int FolderAsked { get; private set; }

    /// <summary>For each time the flow asked for the engine: whether that was on the UI thread.</summary>
    public List<bool> EngineOnUiThread { get; } = [];

    public int CurrentPage { get; set; }

    /// <summary>When set, the read of the outline waits for it (the user can act during a read that has no job card).</summary>
    public Task? OutlineGate { get; set; }

    /// <summary>An outline no file has (hostile titles); null: the outline of the document.</summary>
    public IReadOnlyList<PdfOutlineItem>? Outline { get; set; }

    public string Path => _copy.Path;

    public string Folder => _copy.Folder;

    public DialogHost Host => _host.Host;

    public static async Task<SplitRig> OpenAsync(string file = "pages-marked.pdf")
    {
        var copy = new TempCopy(file);
        var session = new ViewerSession(new PdfEngine());
        await session.OpenAsync(copy.Path, null);
        return new SplitRig(copy, new PdfTab(session, copy.Path));
    }

    /// <summary>A new empty folder inside the temp folder of the rig.</summary>
    public string NewFolder(string name = "out")
    {
        var folder = System.IO.Path.Combine(Folder, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Waits for a card that has a control with the name (a job card goes by) and returns it.</summary>
    public async Task<Dialog> CardWithAsync(string name)
    {
        try
        {
            await PrintHost.UntilAsync(() => _host.Host.ActiveDialog is { } d && d.GetVisualDescendants().OfType<Control>().Any(c => c.Name == name));
        }
        catch (TimeoutException)
        {
            var now = _host.Host.ActiveDialog is { } a ? a.Title + ": " + string.Join(" / ", TextsOf(a)) : "no card";
            throw new TimeoutException("No card with " + name + "; active: " + now);
        }

        return _host.Host.ActiveDialog!;
    }

    /// <summary>
    /// Waits for the result card (OK or Open). A confirm card with the loss list (Go) that comes first is confirmed: most test files have bookmarks
    /// or page labels, which every new file loses.
    /// </summary>
    public async Task<Dialog> ResultAsync()
    {
        while (true)
        {
            await PrintHost.UntilAsync(() => _host.Host.ActiveDialog is { } d && d.GetVisualDescendants().OfType<Button>().Any(b => b.Name is "PART_Ok" or "PART_Go"));
            var card = _host.Host.ActiveDialog!;
            if (card.GetVisualDescendants().OfType<Button>().Any(b => b.Name == "PART_Go"))
            {
                Click(card, "PART_Go");
                PrintHost.Pump();
                continue;
            }

            return card;
        }
    }

    public static void Click(Dialog card, string name) =>
        PrintHost.Find<Button>(card, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    public async Task<Dialog> PressAsync(string name)
    {
        var card = await CardWithAsync(name);
        Click(card, name);
        PrintHost.Pump();
        return card;
    }

    public static IReadOnlyList<string?> TextsOf(Dialog card) => card.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();

    public void Dispose()
    {
        _host.Dispose();
        Tab.Dispose();
        _copy.Dispose();
    }
}
