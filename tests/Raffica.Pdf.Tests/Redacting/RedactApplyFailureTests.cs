using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Core.Redaction;
using Raffica.Core.Saving;
using Raffica.Core.Text;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Redacting;
using Raffica.Pdf.Tests.Printing;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Redacting;

/// <summary>Every way the redaction can end without a result: a card with the reason, the file as it was, the plan kept, nothing left behind.</summary>
public sealed class RedactApplyFailureTests
{
    private const string Term = "Zephyrstraße 17";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IEnumerable<TextBlock> Reasons(Dialog card) => card.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "PART_Reason");

    private static RedactionApplyResult Never() => throw new InvalidOperationException("unreachable");

    // The card that ends a failed run: its title and sentence; closes it and checks the run ended with false and no card is left.
    private static async Task<(string Title, string Message)> FailureCardAsync(RedactApplyRig rig, Task<bool> run)
    {
        var card = await rig.CardWithAsync("PART_Ok");
        var message = PrintHost.Find<TextBlock>(card, "PART_Message").Text!;
        var title = card.Title ?? string.Empty;
        RedactApplyRig.Click(card, "PART_Ok");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        PrintHost.Pump();
        Assert.Null(rig.Host.ActiveDialog);
        return (title, message);
    }

    // The state that every failure must leave: the file as it was, the plan and the mode kept, nothing rebuilt, nothing left in the folder.
    private static async Task AssertKeptAsync(RedactApplyRig rig, byte[] bytes, int version, int marks, params string[] expectedFiles)
    {
        Assert.Equal(bytes, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.True(rig.Session.IsActive);
        Assert.Equal(version, rig.Session.Version);
        Assert.Equal(marks, rig.Session.Plan.Marks.Count);
        Assert.Empty(rig.Rebuilds);
        Assert.Empty(rig.PathChanges);
        Assert.False(rig.Flow.IsRunning);
        Assert.Equal(expectedFiles.Append(rig.Path).Order(), Directory.GetFiles(rig.Folder, "*", SearchOption.AllDirectories).Order());
        Assert.Equal(rig.Path, rig.Tab.Path);
    }

    private static async Task<(string Title, string Message)> RunAsync(RedactApplyRig rig, string target)
    {
        rig.Picks.Enqueue(target);
        var run = rig.Flow.RunAsync(rig.Tab);
        RedactApplyRig.Click(await rig.CardWithAsync("PART_SaveAs"), "PART_SaveAs");
        return await FailureCardAsync(rig, run);
    }

    [AvaloniaFact]
    public async Task EveryFailureKeepsTheFileAndThePlan()
    {
        // 1. The file changed on disk after it was opened: the analysis notices it; there is no way to overwrite it.
        using (var rig = await RedactApplyRig.OpenAsync())
        {
            await rig.MarkTermAsync(Term);
            File.AppendAllText(rig.Path, "\n% another program wrote this\n");
            var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);
            var version = rig.Session.Version;
            var marks = rig.Session.Plan.Marks.Count;
            var run = rig.Flow.RunAsync(rig.Tab);
            var (title, message) = await FailureCardAsync(rig, run);

            Assert.Equal("Redactions not applied", title);
            Assert.Equal("The file changed on disk since it was opened, so it cannot be redacted from here. The file is unchanged and the marks are still open.", message);
            await AssertKeptAsync(rig, bytes, version, marks);
        }

        // 2. The target exists elsewhere and is not replaced: its content stays as it is (the card says why, from the table of the save failures).
        using (var rig = await RedactApplyRig.OpenAsync())
        {
            await rig.MarkTermAsync(Term);
            var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);
            var version = rig.Session.Version;
            var marks = rig.Session.Plan.Marks.Count;
            var target = Path.Combine(rig.Folder, "existing.pdf");
            await File.WriteAllTextAsync(target, "existing", Ct);
            rig.Flow.Applier = (_, _, _, _, _) => throw new SaveFailureException(SaveFailureReason.TargetExists, "exists");

            var (_, message) = await RunAsync(rig, target);

            Assert.Equal(PdfStrings.SaveFailure(SaveFailureReason.TargetExists) + " The marks are still open.", message);
            Assert.Equal("existing", await File.ReadAllTextAsync(target, Ct));
            await AssertKeptAsync(rig, bytes, version, marks, target);
        }

        // 3. The disk is full.
        using (var rig = await RedactApplyRig.OpenAsync())
        {
            await rig.MarkTermAsync(Term);
            var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);
            var version = rig.Session.Version;
            var marks = rig.Session.Plan.Marks.Count;
            rig.Flow.Applier = (_, _, _, _, _) => throw new SaveFailureException(SaveFailureReason.DiskFull, "full");

            var (_, message) = await RunAsync(rig, Path.Combine(rig.Folder, "out.pdf"));

            Assert.Equal(PdfStrings.SaveFailure(SaveFailureReason.DiskFull) + " The marks are still open.", message);
            Assert.Contains("unchanged", message, StringComparison.Ordinal);
            await AssertKeptAsync(rig, bytes, version, marks);
        }

        // 4. A real write that fails: the folder of the target does not exist.
        using (var rig = await RedactApplyRig.OpenAsync())
        {
            await rig.MarkTermAsync(Term);
            var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);
            var version = rig.Session.Version;
            var marks = rig.Session.Plan.Marks.Count;

            var (title, message) = await RunAsync(rig, Path.Combine(rig.Folder, "missing", "out.pdf"));

            Assert.Equal("Redactions not applied", title);
            Assert.Contains("The marks are still open.", message, StringComparison.Ordinal);
            await AssertKeptAsync(rig, bytes, version, marks);
        }

        // 5. Something nobody foresaw ends as a card too, never as an exception in a command handler.
        using (var rig = await RedactApplyRig.OpenAsync())
        {
            await rig.MarkTermAsync(Term);
            var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);
            var version = rig.Session.Version;
            var marks = rig.Session.Plan.Marks.Count;
            rig.Flow.Applier = (_, _, _, _, _) => throw new InvalidOperationException("boom with " + Term);

            var (_, message) = await RunAsync(rig, Path.Combine(rig.Folder, "out.pdf"));

            Assert.Equal("The redactions could not be applied. The file is unchanged and the marks are still open.", message);
            Assert.DoesNotContain("boom", message, StringComparison.Ordinal);
            await AssertKeptAsync(rig, bytes, version, marks);
        }
    }

    [AvaloniaFact]
    public async Task UserCancelIsNoFailureAndLeavesNoCard()
    {
        using var rig = await RedactApplyRig.OpenAsync();
        await rig.MarkTermAsync(Term);
        var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);
        var version = rig.Session.Version;
        var marks = rig.Session.Plan.Marks.Count;
        var target = Path.Combine(rig.Folder, "out.pdf");
        rig.Picks.Enqueue(target);
        var started = new TaskCompletionSource();
        rig.Flow.Applier = async (_, _, _, _, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return Never();
        };

        var run = rig.Flow.RunAsync(rig.Tab);
        RedactApplyRig.Click(await rig.CardWithAsync("PART_SaveAs"), "PART_SaveAs");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        var job = await rig.CardWithAsync("PART_Cancel");
        Assert.Contains("Applying the redactions to redact-covered.pdf", RedactApplyRig.TextsOf(job));
        RedactApplyRig.Click(job, "PART_Cancel");

        // No card: a cancel is no failure.
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        PrintHost.Pump();
        Assert.Null(rig.Host.ActiveDialog);
        await AssertKeptAsync(rig, bytes, version, marks);
    }

    [AvaloniaFact]
    public async Task ACheckFindingNamesKindAndPageButNeverTheText()
    {
        // The fonts fixture has a line that the search does not find; the check does, so nothing is written (the same ending as the command line).
        using var rig = await RedactApplyRig.OpenAsync("redact-fonts.pdf");
        await rig.MarkTermAsync(Term);
        var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);
        var version = rig.Session.Version;
        var marks = rig.Session.Plan.Marks.Count;
        var target = Path.Combine(rig.Folder, "out.pdf");
        rig.Picks.Enqueue(target);

        var run = rig.Flow.RunAsync(rig.Tab);
        RedactApplyRig.Click(await rig.CardWithAsync("PART_SaveAs"), "PART_SaveAs");
        var card = await rig.CardWithAsync("PART_Reason");

        Assert.Equal("Redaction stopped by the check", card.Title);
        Assert.Equal("The written file did not pass the check, so nothing was written. The file is unchanged and the marks are still open.", PrintHost.Find<TextBlock>(card, "PART_Intro").Text);
        Assert.Equal("• A removed text is still in the page text of the result (page 1).", PrintHost.Find<TextBlock>(card, "PART_Reason").Text);
        Assert.DoesNotContain("Zephyr", string.Join('\n', RedactApplyRig.TextsOf(card)), StringComparison.Ordinal);
        RedactApplyRig.Click(card, "PART_Back");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        await AssertKeptAsync(rig, bytes, version, marks);
    }

    [AvaloniaFact]
    public async Task ACheckThatCouldNotFinishIsNeverAPass()
    {
        using var rig = await RedactApplyRig.OpenAsync();
        await rig.MarkTermAsync(Term);
        var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);
        var version = rig.Session.Version;
        var marks = rig.Session.Plan.Marks.Count;
        rig.Flow.Applier = (_, _, _, _, _) => throw new SaveFailureException(SaveFailureReason.RedactionFailed, "no check") { Details = null };
        rig.Picks.Enqueue(Path.Combine(rig.Folder, "out.pdf"));

        var run = rig.Flow.RunAsync(rig.Tab);
        RedactApplyRig.Click(await rig.CardWithAsync("PART_SaveAs"), "PART_SaveAs");
        var card = await rig.CardWithAsync("PART_Intro");

        Assert.Equal("The check of the written file could not be finished, so nothing was written. The file is unchanged and the marks are still open.", PrintHost.Find<TextBlock>(card, "PART_Intro").Text);
        Assert.Empty(Reasons(card));
        RedactApplyRig.Click(card, "PART_Back");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        await AssertKeptAsync(rig, bytes, version, marks);
    }

    [AvaloniaFact]
    public async Task EveryKindOfFindingIsWordedOnTheCard()
    {
        using var rig = await RedactApplyRig.OpenAsync();
        await rig.MarkTermAsync(Term);
        var findings = Enum.GetValues<RedactionFindingKind>().Select(kind => new RedactionFinding(kind, 0, "English detail with Zephyrstraße 17")).ToList();
        rig.Flow.Applier = (_, _, _, _, _) => throw new SaveFailureException(SaveFailureReason.RedactionFailed, "found") { Details = new RedactionCheck(findings, []) };
        rig.Picks.Enqueue(Path.Combine(rig.Folder, "out.pdf"));

        var run = rig.Flow.RunAsync(rig.Tab);
        RedactApplyRig.Click(await rig.CardWithAsync("PART_SaveAs"), "PART_SaveAs");
        var card = await rig.CardWithAsync("PART_Reason");

        // One sentence per kind of finding (the file bytes and the unremovable content are the same kind), none with the detail of Core.
        var reasons = Reasons(card).Select(t => t.Text!).ToList();
        Assert.Equal(findings.Select(f => "• " + RedactWording.Text(f)).Distinct().Order(), reasons.Order());
        Assert.DoesNotContain(reasons, r => r.Contains("English detail", StringComparison.Ordinal) || r.Contains("Zephyr", StringComparison.Ordinal));
        RedactApplyRig.Click(card, "PART_Back");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
    }

    [AvaloniaFact]
    public async Task AdoptFailureSaysTheFileIsTheRedactedOne()
    {
        using var rig = await RedactApplyRig.OpenAsync();
        await rig.MarkTermAsync(Term);
        var version = rig.Session.Version;
        var marks = rig.Session.Plan.Marks.Count;
        var target = Path.Combine(rig.Folder, "written.pdf");
        rig.Picks.Enqueue(target);

        // The session goes away after the file is written and checked, so it cannot take the new document over.
        rig.Flow.Applier = (session, engine, request, progress, token) =>
            RedactionApplier.ApplyAsync(session, engine, request with { Rewritten = _ => rig.Tab.Session.Dispose() }, progress, token);

        var run = rig.Flow.RunAsync(rig.Tab);
        RedactApplyRig.Click(await rig.CardWithAsync("PART_SaveAs"), "PART_SaveAs");
        var (title, message) = await FailureCardAsync(rig, run);

        Assert.Equal("Redactions not applied", title);
        Assert.Equal($"The redactions were written, but the tab could not take the file over. The file at {target} is the redacted one: open it from disk to continue.", message);
        Assert.True(File.Exists(target));
        Assert.True(rig.Session.IsActive);
        Assert.Equal(version, rig.Session.Version);
        Assert.Empty(rig.Rebuilds);
    }

    [AvaloniaFact]
    public async Task ThePlanEditedWhileThePickerIsOpenWritesNothing()
    {
        using var rig = await RedactApplyRig.OpenAsync();
        await rig.MarkTermAsync(Term);
        var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);
        var before = rig.Session.Plan.Marks.Count;
        rig.BeforePick = () =>
        {
            rig.MarkArea(0, new PageRect(1, 1, 20, 20));
            return Task.CompletedTask;
        };
        var target = Path.Combine(rig.Folder, "out.pdf");
        rig.Picks.Enqueue(target);

        var run = rig.Flow.RunAsync(rig.Tab);
        RedactApplyRig.Click(await rig.CardWithAsync("PART_SaveAs"), "PART_SaveAs");
        var notice = await rig.CardWithAsync("PART_Message");
        Assert.Equal(PdfStrings.RedactPlanChanged, PrintHost.Find<TextBlock>(notice, "PART_Message").Text);
        RedactApplyRig.Click(notice, "PART_Ok");

        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.False(File.Exists(target));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.Equal(before + 1, rig.Session.Plan.Marks.Count);
    }
}
