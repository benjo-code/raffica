using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Redaction;
using Raffica.Core.Text;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Redacting;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Text;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Raffica.Shell.Frame;
using Xunit;

namespace Raffica.Pdf.Tests.Redacting;

/// <summary>How an apply through the window ended.</summary>
internal enum ApplyOutcomeKind
{
    /// <summary>The result card was shown: the file is written and was checked.</summary>
    Written,

    /// <summary>The analysis refused the file (the refusal card): nothing was written.</summary>
    Refused,

    /// <summary>The check or the write failed (a card with the reason): nothing was written.</summary>
    Failed,
}

/// <summary>What an apply through the window ended with, with the texts of the card that ended it.</summary>
internal sealed record ApplyOutcome(ApplyOutcomeKind Kind, IReadOnlyList<string> Texts, IReadOnlyList<string> Names, bool Result)
{
    /// <summary>All the texts of the card in one string, for a search.</summary>
    public string All => string.Join('\n', Texts);
}

/// <summary>
/// The window of the PDF app with a real save over a temp copy of a test file and a scripted Save as picker: the redaction is driven the way the user does it
/// (the ribbon commands, the Find panel of the pane, the cards, the buttons), and the result is read from the written file. Nothing is written outside the temp folder.
/// </summary>
internal sealed class RedactUiRig : IDisposable
{
    private readonly TempCopy _copy;
    private readonly PickBox _pick;

    private RedactUiRig(WindowFixture fixture, TempCopy copy, PdfTab tab, PickBox pick, ManualTime time)
    {
        Fixture = fixture;
        _copy = copy;
        Tab = tab;
        _pick = pick;
        Time = time;
    }

    private sealed class PickBox
    {
        public string? Path { get; set; }
    }

    public WindowFixture Fixture { get; }

    public PdfWindow Window => Fixture.Window;

    public PdfTab Tab { get; }

    public ManualTime Time { get; }

    /// <summary>The temp copy of the file that is open.</summary>
    public string Path => _copy.Path;

    /// <summary>The folder of the temp copy (the written files go here).</summary>
    public string Folder => _copy.Folder;

    public RedactSession Session => Tab.Redact;

    /// <summary>What the last search and Mark all did, for the message of a failed assertion.</summary>
    public string Marked { get; private set; } = string.Empty;

    /// <summary>Where the next Save as goes; null: the user cancels the picker.</summary>
    public string? Pick
    {
        get => _pick.Path;
        set => _pick.Path = value;
    }

    public static async Task<RedactUiRig> OpenAsync(string file, bool realSave = true)
    {
        var copy = new TempCopy(file);
        var pick = new PickBox();
        var time = new ManualTime();
        var fixture = WindowFixture.Create(time: time, realSave: realSave, savePicker: _ => Task.FromResult(pick.Path));
        try
        {
            var tab = await fixture.Window.Flow.OpenAsync(copy.Path) ?? throw new InvalidOperationException(file + " did not open");
            await ViewFixture.WaitAsync(() => fixture.Window.ViewOf(tab)?.FirstSharpPageShown == true);
            return new RedactUiRig(fixture, copy, tab, pick, time);
        }
        catch
        {
            fixture.Dispose();
            copy.Dispose();
            throw;
        }
    }

    public CommandDefinition Command(string id) =>
        Window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id);

    /// <summary>Starts the redact mode with the ribbon command.</summary>
    public async Task EnterModeAsync()
    {
        Command("redact.mode").Run!();
        await ViewFixture.WaitAsync(() => Tab.RedactIfCreated is { IsActive: true } && Window.RedactOf(Tab) is not null);
    }

    /// <summary>Searches a term in the Find panel of the pane and marks every match (Mark all). Returns how many marks the plan holds afterwards.</summary>
    public async Task<int> MarkTermAsync(string term)
    {
        var panel = OpenFind();
        panel.TermBox.Text = term;
        Assert.True(await panel.SearchAsync(), "the search did not run to the end");
        ViewFixture.Pump();
        Assert.NotEmpty(panel.Find.Hits);
        if (panel.AcknowledgeButton.IsVisible)
        {
            panel.AcknowledgeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        panel.MarkAllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ViewFixture.Pump();
        Marked = $"{panel.Find.Hits.Count} hits, {Session.Plan.Marks.Count} marks, {panel.Find.LastNotMarkable} not markable";
        return Session.Plan.Marks.Count;
    }

    /// <summary>Runs a pattern search of one kind in the Find panel and marks its matches; returns the texts of the matches.</summary>
    public async Task<IReadOnlyList<string>> MarkPatternAsync(RedactionPatternKind kind)
    {
        var panel = OpenFind();
        Assert.True(await panel.Find.RunPatternsAsync(new HashSet<RedactionPatternKind> { kind }), "the pattern search did not run to the end");
        ViewFixture.Pump();
        // Mark all leaves the matches whose checksum failed to their own button (a typo is a number too, but it is the user's call).
        var texts = panel.Find.Hits.Where(h => h.Certainty != PatternCertainty.ChecksumFailed).Select(h => h.Text).ToList();
        Assert.NotEmpty(texts);
        panel.Find.Acknowledge();
        Assert.True(panel.Find.MarkAll().IsDone);
        ViewFixture.Pump();
        return texts;
    }

    /// <summary>Marks an area (what the pointer makes when it drags a rectangle).</summary>
    public void MarkArea(int page, PageRect area)
    {
        Assert.True(Session.AddArea(page, area).IsDone);
        ViewFixture.Pump();
    }

    private Raffica.Pdf.Panes.RedactFindPanel OpenFind()
    {
        var pane = Window.RedactOf(Tab)!.Pane;
        if (!pane.IsFindOpen)
        {
            pane.ToggleFind();
        }

        return pane.FindPanel!;
    }

    private static bool Has(Dialog dialog, string name) => dialog.GetVisualDescendants().OfType<Control>().Any(c => c.Name == name);

    private Dialog? Open => Window.Host.ActiveDialog;

    /// <summary>Waits for a card that has one of the named parts (the job cards of the steps before it go by).</summary>
    public async Task<Dialog> CardWithAnyAsync(params string[] names)
    {
        await PrintHost.UntilAsync(() => Open is { } d && names.Any(n => Has(d, n)));
        return Open!;
    }

    public static IReadOnlyList<string> TextsOf(Dialog card) =>
        [.. card.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text ?? string.Empty), .. card.GetVisualDescendants().OfType<CheckBox>().Select(c => c.Content as string ?? string.Empty)];

    public static IReadOnlyList<string> NamesOf(Dialog card) =>
        [.. card.GetVisualDescendants().OfType<Control>().Select(c => c.Name ?? string.Empty).Where(n => n.Length > 0)];

    public static void Click(Dialog card, string name) =>
        PrintHost.Find<Button>(card, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>
    /// Runs Apply with the ribbon command and answers the cards: the pages that cannot be redacted in place are ticked (<paramref name="tickRescue"/>), Save as goes to
    /// <paramref name="output"/>, and the card that ends it is read and closed. Returns how it ended.
    /// </summary>
    public async Task<ApplyOutcome> ApplyAsync(string output, bool tickRescue = true, Action<Dialog>? onApplyCard = null)
    {
        Pick = output;
        Command("redact.apply").Run!();
        var first = await CardWithAnyAsync("PART_SaveAs", "PART_Back", "PART_Ok");
        if (!Has(first, "PART_SaveAs"))
        {
            return await EndAsync(first);
        }

        onApplyCard?.Invoke(first);
        if (tickRescue)
        {
            foreach (var box in first.GetVisualDescendants().OfType<CheckBox>().Where(c => c.Name?.StartsWith("PART_Rescue_", StringComparison.Ordinal) == true))
            {
                box.IsChecked = true;
            }

            PrintHost.Pump();
        }

        Click(first, "PART_SaveAs");
        PrintHost.Pump();
        var last = await CardWithAnyAsync("PART_Ok", "PART_Back");
        return await EndAsync(last);
    }

    // Reads the card, classifies it, closes it.
    private async Task<ApplyOutcome> EndAsync(Dialog card)
    {
        var texts = TextsOf(card);
        var names = NamesOf(card);
        var result = names.Contains("PART_Checked") || names.Contains("PART_NotChecked");
        var kind = result ? ApplyOutcomeKind.Written : names.Contains("PART_Reason") && !names.Contains("PART_Message") && names.Contains("PART_Intro")
            && (card.Title == PdfStrings.RedactRefusedTitle) ? ApplyOutcomeKind.Refused : ApplyOutcomeKind.Failed;
        Click(card, names.Contains("PART_Ok") ? "PART_Ok" : "PART_Back");
        PrintHost.Pump();
        await PrintHost.UntilAsync(() => Open is null || !ReferenceEquals(Open, card));
        await PrintHost.UntilAsync(() => !Window.Redacting.IsRunning);
        return new ApplyOutcome(kind, texts, names, result);
    }

    public void Dispose()
    {
        Fixture.Dispose();
        _copy.Dispose();
    }
}
