using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Raffica.Core.Pages;
using Raffica.Core.Pdf;
using Raffica.Core.Saving;
using Raffica.Core.Viewing;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Organizing;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Organizing;

/// <summary>Apply of a page plan: the card, the rewrite on a pool thread, the file that results and the tab that follows it.</summary>
public sealed class ApplyFlowTests
{
    private const string Password = "hôtel";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string[]> TextsAsync(string path, string? password = null)
    {
        await using var document = await new PdfEngine().OpenAsync(path, password, Ct);
        var texts = new string[document.PageCount];
        for (var i = 0; i < texts.Length; i++)
        {
            texts[i] = (await document.GetTextAsync(i, Ct)).Trim();
        }

        return texts;
    }

    private static async Task<int[]> RotationsAsync(string path)
    {
        await using var document = await new PdfEngine().OpenAsync(path, null, Ct);
        var rotations = new int[document.PageCount];
        for (var i = 0; i < rotations.Length; i++)
        {
            rotations[i] = await document.GetPageRotationAsync(i, Ct);
        }

        return rotations;
    }

    private static string Output(ApplyRig rig, string name = "out.pdf") => Path.Combine(rig.Folder, name);

    [AvaloniaFact]
    public async Task RotateDeleteMoveApplyWritesTheRightPagesInTheRightOrder()
    {
        using var rig = await ApplyRig.OpenAsync();
        var before = await TextsAsync(rig.Path);
        Assert.Equal(6, before.Length);
        var rotationsBefore = await RotationsAsync(rig.Path);
        var session = rig.Session;

        session.Selection.Set(6, [0]);
        Assert.True(session.Delete().IsDone); // T1 T2 T3 T4 T5
        session.Selection.Set(5, [4]);
        Assert.True(session.Move(0).IsDone); // T5 T1 T2 T3 T4
        session.Selection.Set(5, [1]);
        Assert.True(session.Rotate(90).IsDone);

        var target = Output(rig);
        rig.Picks.Enqueue(target);
        Assert.True(await rig.ApplyAsync());

        Assert.Equal([before[5], before[1], before[2], before[3], before[4]], await TextsAsync(target));
        var rotations = await RotationsAsync(target);
        Assert.Equal((rotationsBefore[1] + 90) % 360, rotations[1]);
        Assert.Equal(rotationsBefore[5], rotations[0]);
    }

    [AvaloniaFact]
    public async Task SaveAsLeavesTheOriginalUntouchedAndTheTabFollowsTheNewFile()
    {
        using var rig = await ApplyRig.OpenAsync();
        var original = await File.ReadAllBytesAsync(rig.Path, Ct);
        rig.Session.Selection.Set(6, [0]);
        rig.Session.Delete();
        var target = Output(rig, "copy.pdf");
        rig.Picks.Enqueue(target);
        rig.CurrentPage = 3;

        Assert.True(await rig.ApplyAsync());

        Assert.Equal(original, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.Equal(target, rig.Tab.Path);
        Assert.Equal("copy.pdf", rig.Tab.Title);
        Assert.Equal([target], rig.PathChanges);
        Assert.Equal(5, rig.Tab.Session.Document!.PageCount);

        // The tab is rebuilt once, at the page the plan follows (page 4 of 6 became page 3 of 5), and the plan has ended and the tab is clean.
        Assert.Equal([(rig.Tab, 2)], rig.Rebuilds);
        Assert.False(rig.Session.IsActive);
        Assert.False(rig.Tab.IsDirty);
        PrintHost.Pump();
        Assert.Null(rig.Host.ActiveDialog);
    }

    [AvaloniaFact]
    public async Task TheOriginalIsUnchangedUntilTheRewriterReturnsAndReplacedAfterwards()
    {
        using var rig = await ApplyRig.OpenAsync();
        var original = await File.ReadAllBytesAsync(rig.Path, Ct);
        var before = await TextsAsync(rig.Path);
        rig.Session.Selection.Set(6, [2]);
        rig.Session.Delete();

        // Nothing is written to the target before the rewriter returns: while it runs the original is byte for byte as it was.
        byte[]? during = null;
        rig.Flow.Rewriter = async (working, engine, request, progress, token) =>
        {
            during = await File.ReadAllBytesAsync(rig.Path, token);
            return await DocumentSaver.RewriteAsync(working, engine, request, progress, token);
        };

        Assert.True(await rig.ApplyAsync("PART_Original"));

        Assert.Equal(original, during);
        Assert.NotEqual(original, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.Equal([before[0], before[1], before[3], before[4], before[5]], await TextsAsync(rig.Path));
        Assert.Equal(rig.Path, rig.Tab.Path);
        Assert.Empty(rig.PathChanges);
        Assert.Equal(5, rig.Tab.Session.Document!.PageCount);
        Assert.Single(rig.Rebuilds);
        Assert.False(rig.Tab.IsDirty);
    }

    [AvaloniaFact]
    public async Task ApplyRunsOffTheUiThread()
    {
        using var rig = await ApplyRig.OpenAsync();
        rig.Session.Selection.Set(6, [0]);
        rig.Session.Rotate(90);
        bool? onUi = null;
        var started = new TaskCompletionSource();
        using var release = new ManualResetEventSlim();
        rig.Flow.Rewriter = (working, engine, request, progress, token) =>
        {
            onUi = Dispatcher.UIThread.CheckAccess();
            started.SetResult();

            // Blocks the thread it runs on: on the UI thread this would freeze the window and the probe below could not run.
            release.Wait(token);
            return DocumentSaver.RewriteAsync(working, engine, request, progress, token);
        };
        rig.Picks.Enqueue(Output(rig));

        var run = rig.Flow.RunAsync(rig.Tab);
        ApplyRig.Click(await rig.CardWithAsync("PART_SaveAs"), "PART_SaveAs");
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);

        // The UI thread is free while the rewriter blocks: a dispatcher probe posted now runs.
        var probe = Dispatcher.UIThread.InvokeAsync(() => true);
        Assert.True(await probe.GetTask().WaitAsync(TimeSpan.FromSeconds(10), Ct));
        release.Set();
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));

        Assert.False(onUi);
    }

    [AvaloniaFact]
    public async Task ProgressReachesTheJobCardAndNeverGoesBack()
    {
        using var rig = await ApplyRig.OpenAsync();
        rig.Session.Selection.Set(6, [0]);
        rig.Session.Rotate(90);
        var gate = new TaskCompletionSource();
        rig.Flow.Rewriter = async (working, engine, request, progress, token) =>
        {
            progress!.Report(0.6);
            progress.Report(0.2);
            await gate.Task.WaitAsync(token);
            return await DocumentSaver.RewriteAsync(working, engine, request, progress, token);
        };
        rig.Picks.Enqueue(Output(rig));

        var run = rig.Flow.RunAsync(rig.Tab);
        var apply = await rig.CardWithAsync("PART_SaveAs");
        ApplyRig.Click(apply, "PART_SaveAs");

        var job = await rig.CardWithAsync("PART_Cancel");
        var card = PrintHost.Find<SaveJobCard>(job, "PART_SaveJob");
        // Core scales the rewriter's progress into the second part of the bar: 0.6 becomes 0.3 + 0.6 * 0.7; the later 0.2 must not pull it back.
        await PrintHost.UntilAsync(() => card.Fraction >= 0.72 - 1e-6);
        var seen = card.Fraction;
        for (var i = 0; i < 20; i++)
        {
            PrintHost.Pump();
            await Task.Delay(5, Ct);
            Assert.True(card.Fraction >= seen);
            seen = card.Fraction;
        }


        gate.SetResult();
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [AvaloniaFact]
    public async Task DirtyMarksAreSavedFirstOnAgreement()
    {
        using var rig = await ApplyRig.OpenAsync("text-rich.pdf");
        Assert.NotNull(await AnnotatedFile.HighlightAsync(rig.Tab.Annotations));
        await rig.Tab.Annotations.FlushAsync();
        Assert.True(rig.Tab.Annotations.Editor.IsDirty);
        rig.Session.Selection.Set(rig.Session.Plan.Slots.Count, [0]);
        Assert.True(rig.Session.Rotate(90).IsDone);
        var saves = 0;
        rig.SaveMarks = async tab =>
        {
            saves++;

            // The normal incremental save of the editor (what SaveController runs), into the file of the tab.
            await tab.Annotations.Editor.SaveAsync(new PdfEngine(), tab.Path!, false, new Progress<double>(), Ct);
            tab.Annotations.Refresh();
            return true;
        };
        rig.Picks.Enqueue(Output(rig));

        var run = rig.Flow.RunAsync(rig.Tab);
        var question = await rig.CardWithAsync("PART_SaveFirst");
        Assert.Equal(0, saves);
        ApplyRig.Click(question, "PART_SaveFirst");
        var card = await rig.CardWithAsync("PART_SaveAs");
        ApplyRig.Click(card, "PART_SaveAs");

        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(1, saves);

        // The mark is in the written file.
        await using var written = await new PdfEngine().OpenAsync(Output(rig), null, Ct);
        Assert.Contains(await written.GetAnnotationsAsync(0, Ct), a => a.Kind == Raffica.Core.Annotations.AnnotationKind.Highlight);
        Assert.False(rig.Tab.IsDirty);
    }

    // Save as is asked to keep the open file: the save-first card must say plainly that the open file is changed (the plain Apply card keeps its wording).
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveFirstCardNamesTheChangedFileOnTheSaveAsPathOnly(bool changedWording)
    {
        using var rig = await ApplyRig.OpenAsync("text-rich.pdf");
        Assert.NotNull(await AnnotatedFile.HighlightAsync(rig.Tab.Annotations));
        await rig.Tab.Annotations.FlushAsync();
        rig.Session.Selection.Set(rig.Session.Plan.Slots.Count, [0]);
        Assert.True(rig.Session.Rotate(90).IsDone);

        var run = rig.Flow.RunAsync(rig.Tab, changedWording ? ApplyTarget.SaveAs : ApplyTarget.Choose);
        var question = await rig.CardWithAsync("PART_SaveFirst");
        var message = PrintHost.Find<TextBlock>(question, "PART_Message").Text;

        var title = rig.Tab.Title;
        Assert.Equal(PdfStrings.Fill(changedWording ? PdfStrings.SaveFirstSaveAsMessage : PdfStrings.SaveFirstMessage, title), message);
        if (changedWording)
        {
            // Both languages name the open file and say that it is changed.
            foreach (var language in new[] { "en", "de" })
            {
                var text = PdfStrings.Fill(PdfStrings.GetIn("SaveFirstSaveAsMessage", System.Globalization.CultureInfo.GetCultureInfo(language)), "x.pdf");
                Assert.Contains("x.pdf", text, StringComparison.Ordinal);
                Assert.Contains(language == "en" ? "is changed" : "geändert", text, StringComparison.Ordinal);
            }
        }

        ApplyRig.Click(question, "PART_Back");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    // After the marks were written into the file, a failed rewrite must not claim that the file is unchanged.
    [AvaloniaFact]
    public async Task AFailureAfterSaveFirstDoesNotSayTheFileIsUnchanged()
    {
        using var rig = await ApplyRig.OpenAsync("text-rich.pdf");
        Assert.NotNull(await AnnotatedFile.HighlightAsync(rig.Tab.Annotations));
        await rig.Tab.Annotations.FlushAsync();
        rig.Session.Selection.Set(rig.Session.Plan.Slots.Count, [0]);
        Assert.True(rig.Session.Rotate(90).IsDone);
        rig.SaveMarks = async tab =>
        {
            await tab.Annotations.Editor.SaveAsync(new PdfEngine(), tab.Path!, false, new Progress<double>(), Ct);
            tab.Annotations.Refresh();
            return true;
        };
        rig.Flow.Rewriter = (_, _, _, _, _) => throw new InvalidOperationException("boom");
        rig.Picks.Enqueue(Output(rig));

        var run = rig.Flow.RunAsync(rig.Tab);
        ApplyRig.Click(await rig.CardWithAsync("PART_SaveFirst"), "PART_SaveFirst");
        ApplyRig.Click(await rig.CardWithAsync("PART_SaveAs"), "PART_SaveAs");
        var failure = await rig.CardWithAsync("PART_Ok");
        var texts = ApplyRig.TextsOf(failure);

        Assert.Contains(texts, t => t is not null && t.Contains(PdfStrings.ApplyStaysAfterSave, StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t is not null && t.Contains(PdfStrings.ApplyStays, StringComparison.Ordinal));
        ApplyRig.Click(failure, "PART_Ok");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.True(rig.Session.IsActive);

        // The next run starts again from "nothing saved first": an identity-free failure without a save-first says "unchanged".
        Assert.Contains(PdfStrings.ApplyStays, ApplyFlow.FailureText(rig.Tab, new InvalidOperationException("x")).Message, StringComparison.Ordinal);
        Assert.Contains(PdfStrings.ApplyStaysAfterSave, ApplyFlow.FailureText(rig.Tab, new InvalidOperationException("x"), savedFirst: true).Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task BackOnTheSaveFirstCardKeepsEverything()
    {
        using var rig = await ApplyRig.OpenAsync("text-rich.pdf");
        Assert.NotNull(await AnnotatedFile.HighlightAsync(rig.Tab.Annotations));
        await rig.Tab.Annotations.FlushAsync();
        rig.Session.Selection.Set(rig.Session.Plan.Slots.Count, [0]);
        rig.Session.Rotate(90);
        var version = rig.Session.Version;
        var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);
        var saves = 0;
        rig.SaveMarks = _ =>
        {
            saves++;
            return Task.FromResult(true);
        };

        var run = rig.Flow.RunAsync(rig.Tab);
        await rig.PressAsync("PART_Back");

        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(0, saves);
        Assert.True(rig.Session.IsActive);
        Assert.False(rig.Session.Plan.IsIdentity);
        Assert.Equal(version, rig.Session.Version);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.True(rig.Tab.Annotations.Editor.IsDirty);
        Assert.Empty(rig.Rebuilds);
    }

    [AvaloniaFact]
    public async Task AFailedSaveFirstStopsTheFlowAndTheDirtyStateStays()
    {
        using var rig = await ApplyRig.OpenAsync("text-rich.pdf");
        Assert.NotNull(await AnnotatedFile.HighlightAsync(rig.Tab.Annotations));
        await rig.Tab.Annotations.FlushAsync();
        rig.Session.Selection.Set(rig.Session.Plan.Slots.Count, [0]);
        rig.Session.Rotate(90);
        rig.SaveMarks = _ => Task.FromResult(false);

        var run = rig.Flow.RunAsync(rig.Tab);
        await rig.PressAsync("PART_SaveFirst");

        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.True(rig.Session.IsActive);
        Assert.True(rig.Tab.IsDirty);
        Assert.Empty(rig.Rebuilds);
    }

    [AvaloniaFact]
    public async Task UnresolvedDraftRefusesTheApply()
    {
        using var rig = await ApplyRig.OpenAsync();
        rig.Session.Selection.Set(6, [0]);
        rig.Session.Rotate(90);
        using var draft = rig.Tab.Annotations.BeginUnresolved();

        var run = rig.Flow.RunAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_Ok");

        Assert.Contains(PdfStrings.ApplyOpenEdit, ApplyRig.TextsOf(card));
        ApplyRig.Click(card, "PART_Ok");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.True(rig.Session.IsActive);
        Assert.Empty(rig.Rebuilds);
    }

    [AvaloniaTheory]
    [InlineData("signed-docmdp-1.pdf", "1")]
    [InlineData("signed-docmdp-2.pdf", "2")]
    [InlineData("signed-docmdp-3.pdf", "3")]
    [InlineData("signed-docmdp-none.pdf", null)]
    public async Task SignedFileListsTheSignatureLoss(string file, string? level)
    {
        using var rig = await ApplyRig.OpenAsync(file);
        rig.Session.Selection.Set(rig.Session.Plan.Slots.Count, [0]);
        Assert.True(rig.Session.Rotate(90).IsDone);

        var run = rig.Flow.RunAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_SaveAs");
        var texts = ApplyRig.TextsOf(card);

        var line = Assert.Single(texts, t => t is not null && t.Contains("digitally signed", StringComparison.Ordinal));
        Assert.Contains("broken", line, StringComparison.Ordinal);
        Assert.DoesNotContain("valid ", line, StringComparison.Ordinal);
        if (level is null)
        {
            Assert.DoesNotContain("change level", line, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains($"change level {level}", line, StringComparison.Ordinal);
        }

        ApplyRig.Click(card, "PART_Back");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [AvaloniaFact]
    public async Task EncryptedInsertIntoPlainIsRefused()
    {
        using var rig = await ApplyRig.OpenAsync();
        var encrypted = rig.CopyOf("encrypted_hello_world_r3.pdf");
        Assert.True((await rig.Session.InsertFileAsync(encrypted, Password, "all", 6)).IsDone);
        var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);

        var run = rig.Flow.RunAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_Back");

        // Only the reason and Back; nothing to write.
        Assert.Equal(PdfStrings.ApplyRefusedTitle, card.Title);
        Assert.DoesNotContain(card.GetVisualDescendants().OfType<Button>(), b => b.Name is "PART_SaveAs" or "PART_Original");
        Assert.Contains(ApplyRig.TextsOf(card), t => t is not null && t.Contains("encrypted_hello_world_r3.pdf", StringComparison.Ordinal));
        ApplyRig.Click(card, "PART_Back");

        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.True(rig.Session.IsActive);
        Assert.Equal(7, rig.Session.Plan.Slots.Count);
    }

    [AvaloniaFact]
    public async Task EncryptedPrimaryStaysEncrypted()
    {
        using var rig = await ApplyRig.OpenAsync("encrypted_hello_world_r3.pdf", Password);
        var other = rig.CopyOf("pages-marked.pdf");
        Assert.True((await rig.Session.InsertFileAsync(other, null, "1-2", 1)).IsDone);
        var target = Output(rig);
        rig.Picks.Enqueue(target);

        var run = rig.Flow.RunAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_SaveAs");
        Assert.Equal(PdfStrings.ApplyTitle, card.Title);
        ApplyRig.Click(card, "PART_SaveAs");

        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30)));
        await using var written = await new PdfEngine().OpenAsync(target, Password, Ct);
        Assert.True((await written.GetPrintInfoAsync(Ct)).IsEncrypted);
        Assert.Equal(3, written.PageCount);
        await Assert.ThrowsAsync<PdfOpenException>(async () => await new PdfEngine().OpenAsync(target, null, Ct));
    }

    [AvaloniaFact]
    public async Task DuplicatedFormPagesAndFieldNameCollisionsAreListed()
    {
        using var rig = await ApplyRig.OpenAsync("forms-basic.pdf");
        var count = rig.Session.Plan.Slots.Count;
        rig.Session.Selection.Set(count, [0]);
        Assert.True(rig.Session.Duplicate().IsDone);
        var other = rig.CopyOf("forms-basic.pdf", "forms-other.pdf");
        Assert.True((await rig.Session.InsertFileAsync(other, null, "all", rig.Session.Plan.Slots.Count)).IsDone);

        var run = rig.Flow.RunAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_SaveAs");
        var texts = ApplyRig.TextsOf(card);

        Assert.Contains(texts, t => t is not null && t.Contains("Copied pages have form fields", StringComparison.Ordinal));
        Assert.Contains(texts, t => t is not null && t.Contains("Inserted pages have form fields", StringComparison.Ordinal));
        Assert.Contains(texts, t => t is not null && t.Contains("Form fields of the same name come from different files", StringComparison.Ordinal));
        Assert.Contains(texts, t => t is not null && t.Contains("pages", StringComparison.Ordinal) && t.Contains("of the new file", StringComparison.Ordinal));

        ApplyRig.Click(card, "PART_Back");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    [AvaloniaFact]
    public async Task ApplyThenTheTabShowsTheNewPagesAtTheFollowedPage()
    {
        using var copy = new TempCopy("pages-marked.pdf");
        using var windows = WindowFixture.Create(realSave: true, savePicker: _ => Task.FromResult<string?>(null));
        var tab = await windows.Window.Flow.OpenAsync(copy.Path) ?? throw new InvalidOperationException("did not open");
        ViewFixture.Pump();
        var view = windows.Window.ViewOf(tab)!;
        view.SetLayout(PageLayoutMode.SinglePage);
        view.GoToPage(3);
        ViewFixture.Pump();
        await ViewFixture.WaitAsync(() => view.CurrentPage == 3);

        windows.Window.ToggleOrganize();
        await ViewFixture.WaitAsync(() => tab.OrganizeIfCreated is { IsActive: true });
        tab.Organize.Selection.Set(6, [0]);
        Assert.True(tab.Organize.Delete().IsDone);
        Assert.True(tab.IsDirty);

        // The Apply command; the card is answered with "Save in the original file" (armed by the real clock).
        var run = windows.Window.ApplyActiveAsync();
        var card = await WaitForCardAsync(windows.Window, "PART_Original");
        await Task.Delay(ArmingLockMilliseconds);
        ApplyRig.Click(card, "PART_Original");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30)));
        ViewFixture.Pump();

        var rebuilt = windows.Window.ViewOf(tab)!;
        Assert.NotSame(view, rebuilt);
        Assert.Equal(5, rebuilt.PageCount);
        Assert.Equal(2, rebuilt.CurrentPage);
        Assert.False(tab.IsDirty);
        Assert.Null(tab.OrganizeIfCreated);
        Assert.Equal(5, tab.Session.Document!.PageCount);
    }

    [AvaloniaFact]
    public async Task SaveWithAnOpenPlanOpensTheApplyCardAndWithoutOneBehavesAsBefore()
    {
        using var copy = new TempCopy("pages-marked.pdf");
        var saved = 0;
        using var windows = WindowFixture.Create(saveRunner: (_, _, _, _, _) =>
        {
            saved++;
            return Task.FromException<Raffica.Core.Annotations.AnnotationSaveResult>(new OperationCanceledException());
        });
        var tab = await windows.Window.Flow.OpenAsync(copy.Path) ?? throw new InvalidOperationException("did not open");
        ViewFixture.Pump();

        // An identity plan: Save changes nothing (the tab is clean) and no card appears.
        tab.Organize.Enter();
        Assert.True(await windows.Window.SaveActiveAsync());
        Assert.Null(windows.Window.Host.ActiveDialog);

        tab.Organize.Selection.Set(6, [0]);
        tab.Organize.Rotate(90);
        var run = windows.Window.SaveActiveAsync();
        var card = await WaitForCardAsync(windows.Window, "PART_SaveAs");
        Assert.Equal(PdfStrings.ApplyTitle, card.Title);
        Assert.Equal(0, saved);
        ApplyRig.Click(card, "PART_Back");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30)));

        // Save as asks the same card but offers no original-file button.
        var runAs = windows.Window.SaveActiveAsAsync();
        var cardAs = await WaitForCardAsync(windows.Window, "PART_SaveAs");
        Assert.DoesNotContain(cardAs.GetVisualDescendants().OfType<Button>(), b => b.Name == "PART_Original");
        ApplyRig.Click(cardAs, "PART_Back");
        Assert.False(await runAs.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal(0, saved);
    }

    private const int ArmingLockMilliseconds = ArmingLock.DelayMilliseconds + 150;

    private static async Task<Dialog> WaitForCardAsync(PdfWindow window, string button)
    {
        await PrintHost.UntilAsync(() => window.Host.ActiveDialog is { } d && d.GetVisualDescendants().OfType<Button>().Any(b => b.Name == button));
        return window.Host.ActiveDialog!;
    }
}
