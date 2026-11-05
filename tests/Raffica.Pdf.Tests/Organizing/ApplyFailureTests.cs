using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Core.Pages;
using Raffica.Core.Pdf;
using Raffica.Core.Saving;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Tests.Printing;
using Xunit;

namespace Raffica.Pdf.Tests.Organizing;

/// <summary>Every way the rewrite can end without a result: a card with the reason, the file as it was, the plan and the selection kept.</summary>
public sealed class ApplyFailureTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Func<PdfDocument, PdfEngine, RewriteRequest, IProgress<double>?, CancellationToken, Task<SaveResult>> Throwing(Exception ex) =>
        (_, _, _, _, _) => throw ex;

    // A plan with three steps and a selection; the pages are rotated, deleted and moved.
    private static (int Version, int[] Selection, int Pages) Prepare(ApplyRig rig)
    {
        var session = rig.Session;
        session.Selection.Set(6, [0]);
        session.Rotate(90);
        session.Selection.Set(6, [5]);
        session.Move(0);
        session.Selection.Set(6, [2, 3]);
        return (session.Version, [.. session.Selection.Positions], session.Plan.Slots.Count);
    }

    private static void AssertPlanKept(ApplyRig rig, (int Version, int[] Selection, int Pages) before)
    {
        Assert.True(rig.Session.IsActive);
        Assert.Equal(before.Version, rig.Session.Version);
        Assert.Equal(before.Selection, rig.Session.Selection.Positions);
        Assert.Equal(before.Pages, rig.Session.Plan.Slots.Count);
        Assert.False(rig.Session.Plan.IsIdentity);
        Assert.True(rig.Tab.IsDirty);
        Assert.Empty(rig.Rebuilds);
        Assert.Empty(rig.PathChanges);
    }

    private static async Task<string> FailureCardAsync(ApplyRig rig, Task<bool> run)
    {
        var card = await rig.CardWithAsync("PART_Ok");
        Assert.Equal(PdfStrings.ApplyFailedTitle, card.Title);
        var message = PrintHost.Find<TextBlock>(card, "PART_Message").Text!;
        ApplyRig.Click(card, "PART_Ok");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30)));
        PrintHost.Pump();
        Assert.Null(rig.Host.ActiveDialog);
        return message;
    }

    private static string[] TempFiles(string folder) => Directory.GetFiles(folder, ".pw-*.tmp");

    private static async Task<bool> FailsWithAsync(Exception ex, string expected)
    {
        using var rig = await ApplyRig.OpenAsync();
        var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);
        var before = Prepare(rig);
        var target = Path.Combine(rig.Folder, "out.pdf");
        rig.Picks.Enqueue(target);
        rig.Flow.Rewriter = Throwing(ex);

        var run = rig.Flow.RunAsync(rig.Tab);
        var apply = await rig.CardWithAsync("PART_SaveAs");
        ApplyRig.Click(apply, "PART_SaveAs");
        var message = await FailureCardAsync(rig, run);

        Assert.Contains(expected, message, StringComparison.Ordinal);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.False(File.Exists(target));
        AssertPlanKept(rig, before);
        return true;
    }

    [AvaloniaFact]
    public async Task EveryFailureKeepsTheFileAndThePlan()
    {
        // Every reason of Core that has a sentence, through the hook the Core tests use.
        foreach (var reason in Enum.GetValues<SaveFailureReason>().Where(r => r is not (SaveFailureReason.Canceled or SaveFailureReason.OriginalChanged or SaveFailureReason.SourceModified or SaveFailureReason.EncryptedNotSupported)))
        {
            Assert.True(await FailsWithAsync(new SaveFailureException(reason, "injected"), PdfStrings.ApplyChangesOpen), reason.ToString());
        }

        Assert.True(await FailsWithAsync(new SaveFailureException(SaveFailureReason.EncryptedNotSupported, "x"), PdfStrings.ApplyFailEncrypted));
        Assert.True(await FailsWithAsync(new SaveFailureException(SaveFailureReason.SourceModified, "x"), PdfStrings.ApplyFailSourceModified));
        Assert.True(await FailsWithAsync(new PageEditException(PageEditError.OutOfRange, "x"), PdfStrings.ApplyFailPlan));
        Assert.True(await FailsWithAsync(new PdfOpenException(PdfOpenError.NotAPdf), PdfStrings.ApplyFailOpen));
        Assert.True(await FailsWithAsync(new InvalidDataException("x"), PdfStrings.ApplyFailDamaged));

        // Whatever is not foreseen ends as a card too, never as an exception.
        Assert.True(await FailsWithAsync(new InvalidOperationException("surprise"), PdfStrings.ApplyFailOther));
        Assert.True(await FailsWithAsync(new IOException("gone"), PdfStrings.ApplyFailOther));
    }

    [AvaloniaFact]
    public async Task AFileThatChangedOnDiskSinceItWasOpenedIsRefusedByTheRealCheck()
    {
        using var rig = await ApplyRig.OpenAsync();
        var before = Prepare(rig);

        // Another program rewrote the file after it was opened.
        await File.AppendAllTextAsync(rig.Path, "\n", Ct);
        var changed = await File.ReadAllBytesAsync(rig.Path, Ct);
        var target = Path.Combine(rig.Folder, "out.pdf");
        rig.Picks.Enqueue(target);

        var run = rig.Flow.RunAsync(rig.Tab);
        ApplyRig.Click(await rig.CardWithAsync("PART_SaveAs"), "PART_SaveAs");
        var message = await FailureCardAsync(rig, run);

        Assert.Contains(PdfStrings.ApplyFailSourceModified, message, StringComparison.Ordinal);
        Assert.Contains("still open", message, StringComparison.Ordinal);
        Assert.Equal(changed, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.False(File.Exists(target));
        Assert.Empty(TempFiles(rig.Folder));
        AssertPlanKept(rig, before);
    }

    [AvaloniaFact]
    public async Task AnInsertedFileThatChangedSinceItWasInsertedIsNamed()
    {
        using var rig = await ApplyRig.OpenAsync();
        var other = rig.CopyOf("pages-marked.pdf", "other.pdf");
        Assert.True((await rig.Session.InsertFileAsync(other, null, "1-2", 6)).IsDone);
        var version = rig.Session.Version;
        await File.AppendAllTextAsync(other, "\n", Ct);
        var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);

        var run = rig.Flow.RunAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_Original");
        rig.Time.Advance(1000);
        ApplyRig.Click(card, "PART_Original");
        var message = await FailureCardAsync(rig, run);

        Assert.Contains("other.pdf", message, StringComparison.Ordinal);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.True(rig.Session.IsActive);
        Assert.Equal(version, rig.Session.Version);
        Assert.Equal(8, rig.Session.Plan.Slots.Count);
        Assert.Single(rig.Session.Inserted);
    }

    [AvaloniaFact]
    public async Task CancelBeforeTheReplaceLeavesTheTargetUntouched()
    {
        using var rig = await ApplyRig.OpenAsync();
        var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);
        var before = Prepare(rig);
        var target = Path.Combine(rig.Folder, "out.pdf");
        await File.WriteAllTextAsync(target, "existing", Ct);
        rig.Picks.Enqueue(target);
        var started = new TaskCompletionSource();
        rig.Flow.Rewriter = async (_, _, _, _, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("unreachable");
        };

        var run = rig.Flow.RunAsync(rig.Tab);
        ApplyRig.Click(await rig.CardWithAsync("PART_SaveAs"), "PART_SaveAs");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        var job = await rig.CardWithAsync("PART_Cancel");
        ApplyRig.Click(job, "PART_Cancel");

        // No card: a cancel is no failure.
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30)));
        PrintHost.Pump();
        Assert.Null(rig.Host.ActiveDialog);
        Assert.Equal("existing", await File.ReadAllTextAsync(target, Ct));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(rig.Path, Ct));
        AssertPlanKept(rig, before);
        Assert.False(rig.Flow.IsRunning);
    }

    [AvaloniaFact]
    public async Task AdoptFailureSaysTheFileIsTheRewrittenOne()
    {
        using var rig = await ApplyRig.OpenAsync();
        var before = Prepare(rig);
        var target = Path.Combine(rig.Folder, "written.pdf");
        rig.Picks.Enqueue(target);

        // The session goes away after the file is written, so it cannot take the new document over.
        rig.Flow.Rewriter = async (working, engine, request, progress, token) =>
        {
            var result = await DocumentSaver.RewriteAsync(working, engine, request, progress, token);
            rig.Tab.Session.Dispose();
            return result;
        };

        var run = rig.Flow.RunAsync(rig.Tab);
        ApplyRig.Click(await rig.CardWithAsync("PART_SaveAs"), "PART_SaveAs");
        var message = await FailureCardAsync(rig, run);

        Assert.Contains(target, message, StringComparison.Ordinal);
        Assert.Contains("rewritten one", message, StringComparison.Ordinal);
        Assert.DoesNotContain("unchanged", message, StringComparison.Ordinal);

        // The file at the target is the rewritten one; the plan and the selection stay, the tab is not rebuilt.
        await using var written = await new PdfEngine().OpenAsync(target, null, Ct);
        Assert.Equal(before.Pages, written.PageCount);
        AssertPlanKept(rig, before);
    }

    [AvaloniaFact]
    public async Task OnlyOneApplyRunsAtATime()
    {
        using var rig = await ApplyRig.OpenAsync();
        Prepare(rig);
        var run = rig.Flow.RunAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_Back");

        Assert.True(rig.Flow.IsRunning);
        Assert.False(await rig.Flow.RunAsync(rig.Tab));
        ApplyRig.Click(card, "PART_Back");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.False(rig.Flow.IsRunning);
    }

    [AvaloniaFact]
    public async Task AnIdentityPlanIsNotApplied()
    {
        using var rig = await ApplyRig.OpenAsync();

        Assert.False(await rig.Flow.RunAsync(rig.Tab));
        Assert.Null(rig.Host.ActiveDialog);
        Assert.False(rig.Flow.IsRunning);
    }

    [AvaloniaFact]
    public async Task NoTextOfAFailureSaysValidOrLegallyBinding()
    {
        using var rig = await ApplyRig.OpenAsync();
        var banned = new System.Text.RegularExpressions.Regex(@"\b(valid|gültig|legally binding|rechtsverbindlich)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var exceptions = new Exception[]
        {
            new SaveFailureException(SaveFailureReason.SourceModified, "x") { FilePath = rig.CopyOf("pages-marked.pdf", "x.pdf") },
            new SaveFailureException(SaveFailureReason.EncryptedNotSupported, "x"),
            new SaveFailureException(SaveFailureReason.DiskFull, "x"),
            new PageApplyAdoptException(rig.Path, null),
            new PageEditException(PageEditError.OutOfRange, "x"),
            new PdfOpenException(PdfOpenError.NotAPdf),
            new InvalidDataException("x"),
            new InvalidOperationException("x"),
        };

        foreach (var language in new[] { "en", "de" })
        {
            PdfStrings.Culture = System.Globalization.CultureInfo.GetCultureInfo(language);
            foreach (var ex in exceptions)
            {
                var (title, message) = Raffica.Pdf.Organizing.ApplyFlow.FailureText(rig.Tab, ex);
                Assert.False(string.IsNullOrWhiteSpace(message));
                Assert.False(banned.IsMatch(title + " " + message), message);
            }
        }
    }
}
