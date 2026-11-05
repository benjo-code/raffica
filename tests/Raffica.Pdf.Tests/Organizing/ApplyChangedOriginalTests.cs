using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Raffica.Core.Pdf;
using Raffica.Core.Saving;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Tests.Printing;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Organizing;

/// <summary>
/// The questions after another file stood at the path of the original (OriginalChanged), and the guards around the confirmed plan: the plan that is
/// written is the plan that was shown, and a job dialog that cannot be made never turns a written file into a failure.
/// </summary>
public sealed class ApplyChangedOriginalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (int Version, int Pages) Prepare(ApplyRig rig)
    {
        rig.Session.Selection.Set(6, [0]);
        rig.Session.Rotate(90);
        rig.Session.Selection.Set(6, [5]);
        rig.Session.Move(0);
        return (rig.Session.Version, rig.Session.Plan.Slots.Count);
    }

    // The first attempt fails with OriginalChanged, the later ones are written for real; the requests that reached the hook are recorded.
    private static Func<PdfDocument, PdfEngine, RewriteRequest, IProgress<double>?, CancellationToken, Task<SaveResult>> ChangedThenReal(List<RewriteRequest> requests) =>
        (working, engine, request, progress, token) =>
        {
            requests.Add(request);
            return requests.Count == 1
                ? throw new SaveFailureException(SaveFailureReason.OriginalChanged, "injected")
                : DocumentSaver.RewriteAsync(working, engine, request, progress, token);
        };

    private static async Task<Dialog> ChooseOriginalAsync(ApplyRig rig)
    {
        var card = await rig.CardWithAsync("PART_Original");
        rig.Time.Advance(1000);
        ApplyRig.Click(card, "PART_Original");
        return await rig.CardWithAsync("PART_Overwrite");
    }

    [AvaloniaFact]
    public async Task OverwriteRetriesWithTheConfirmedFlag()
    {
        using var rig = await ApplyRig.OpenAsync();
        Prepare(rig);
        var requests = new List<RewriteRequest>();
        rig.Flow.Rewriter = ChangedThenReal(requests);

        var run = rig.Flow.RunAsync(rig.Tab);
        var changed = await ChooseOriginalAsync(rig);
        Assert.Equal(PdfStrings.ChangedTitle, changed.Title);
        rig.Time.Advance(1000);
        ApplyRig.Click(changed, "PART_Overwrite");

        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal([false, true], requests.Select(r => r.OverwriteChangedOriginal).ToArray());
        Assert.Equal(rig.Path, requests[1].TargetPath);
        Assert.Single(rig.Rebuilds);
        Assert.False(rig.Session.IsActive);
    }

    [AvaloniaFact]
    public async Task SaveAsAnswerOfTheChangedOriginalCardGoesToThePickerAndSwitchesTheTarget()
    {
        using var rig = await ApplyRig.OpenAsync();
        Prepare(rig);
        var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);
        var copy = Path.Combine(rig.Folder, "copy.pdf");
        rig.Picks.Enqueue(copy);
        var requests = new List<RewriteRequest>();
        rig.Flow.Rewriter = ChangedThenReal(requests);

        var run = rig.Flow.RunAsync(rig.Tab);
        var changed = await ChooseOriginalAsync(rig);
        ApplyRig.Click(changed, "PART_SaveAs");

        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal([rig.Path, copy], requests.Select(r => r.TargetPath).ToArray());
        Assert.False(requests[1].OverwriteChangedOriginal);
        Assert.Equal(copy, rig.Tab.Path);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.True(File.Exists(copy));
    }

    [AvaloniaTheory]
    [InlineData("PART_Cancel")]
    [InlineData("PART_SaveAs")]
    public async Task BackOrACanceledPickerOnTheChangedOriginalCardWritesNothingAndKeepsThePlan(string button)
    {
        // The picker has no answer queued, so "Save as" ends as a canceled picker.
        using var rig = await ApplyRig.OpenAsync();
        var (version, pages) = Prepare(rig);
        var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);
        var requests = new List<RewriteRequest>();
        rig.Flow.Rewriter = ChangedThenReal(requests);

        var run = rig.Flow.RunAsync(rig.Tab);
        var changed = await ChooseOriginalAsync(rig);
        ApplyRig.Click(changed, button);

        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        PrintHost.Pump();
        Assert.Null(rig.Host.ActiveDialog);
        Assert.Single(requests);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.True(rig.Session.IsActive);
        Assert.Equal(version, rig.Session.Version);
        Assert.Equal(pages, rig.Session.Plan.Slots.Count);
        Assert.Empty(rig.Rebuilds);
    }

    [AvaloniaFact]
    public async Task OriginalChangedWithASaveAsChoiceGivesTheGenericCard()
    {
        using var rig = await ApplyRig.OpenAsync();
        Prepare(rig);
        var target = Path.Combine(rig.Folder, "out.pdf");
        rig.Picks.Enqueue(target);
        var requests = new List<RewriteRequest>();
        rig.Flow.Rewriter = ChangedThenReal(requests);

        var run = rig.Flow.RunAsync(rig.Tab);
        ApplyRig.Click(await rig.CardWithAsync("PART_SaveAs"), "PART_SaveAs");
        var card = await rig.CardWithAsync("PART_Ok");

        Assert.Equal(PdfStrings.ApplyFailedTitle, card.Title);
        Assert.Contains(PdfStrings.SaveFailure(SaveFailureReason.OriginalChanged), PrintHost.Find<TextBlock>(card, "PART_Message").Text, StringComparison.Ordinal);
        ApplyRig.Click(card, "PART_Ok");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Single(requests);
        Assert.False(File.Exists(target));
        Assert.True(rig.Session.IsActive);
    }

    [AvaloniaFact]
    public async Task APlanEditedWhileThePickerIsOpenIsNotWritten()
    {
        using var rig = await ApplyRig.OpenAsync();
        Prepare(rig);
        var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);
        var target = Path.Combine(rig.Folder, "out.pdf");
        rig.Picks.Enqueue(target);
        var rewrites = 0;
        rig.Flow.Rewriter = (working, engine, request, progress, token) =>
        {
            rewrites++;
            return DocumentSaver.RewriteAsync(working, engine, request, progress, token);
        };
        rig.BeforePick = () =>
        {
            rig.Session.Selection.Set(rig.Session.Plan.Slots.Count, [0]);
            rig.Session.Rotate(180);
            return Task.CompletedTask;
        };

        var run = rig.Flow.RunAsync(rig.Tab);
        ApplyRig.Click(await rig.CardWithAsync("PART_SaveAs"), "PART_SaveAs");
        var notice = await rig.CardWithAsync("PART_Ok");
        var editedVersion = rig.Session.Version;

        Assert.Contains(PdfStrings.ApplyPlanChanged, ApplyRig.TextsOf(notice));
        ApplyRig.Click(notice, "PART_Ok");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal(0, rewrites);
        Assert.False(File.Exists(target));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.True(rig.Session.IsActive);
        Assert.Equal(editedVersion, rig.Session.Version);
        Assert.Empty(rig.Rebuilds);
    }

    [AvaloniaFact]
    public async Task AJobDialogThatCannotBeMadeNeverTurnsASuccessIntoAFailure()
    {
        using var rig = await ApplyRig.OpenAsync();
        Prepare(rig);
        var target = Path.Combine(rig.Folder, "out.pdf");
        rig.Picks.Enqueue(target);
        var made = 0;
        rig.Flow.JobDialog = _ =>
        {
            made++;
            throw new InvalidOperationException("no dialog");
        };

        Assert.True(await rig.ApplyAsync());

        Assert.True(made >= 1);
        Assert.True(File.Exists(target));
        Assert.Single(rig.Rebuilds);
        Assert.False(rig.Session.IsActive);
    }
}
