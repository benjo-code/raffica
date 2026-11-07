using System.Text.RegularExpressions;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Core.Ocr;
using Raffica.Ocr;
using Raffica.Pdf.Commands;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Frame;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Ocr;

public sealed class OcrRunFlowTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OcrOptions Options(int jobs = 1, OcrMode mode = OcrMode.Skip) => new("eng", mode, Jobs: jobs);

    private static CommandDefinition Command(PdfWindow window, string id) =>
        window.Ribbon.Definition!.Tabs.SelectMany(t => t.Groups).SelectMany(g => g.Commands).Single(c => c.Id == id);

    // Runs to the report card and closes it.
    private static async Task RunToTheEndAsync(OcrRig rig, OcrOptions? options = null)
    {
        var run = rig.Window.StartOcrRunAsync(rig.Tab, options ?? Options());
        var card = await rig.CardWithAsync("PART_Ok");
        OcrRig.Click(card, "PART_Ok");
        await run;
    }

    [AvaloniaFact]
    public async Task ScanBecomesSearchable()
    {
        // The real engine over the shipped language data: the words of ocr-words.tsv are in the text of the page afterwards, and a search finds one.
        using var rig = await OcrRig.OpenAsync(fake: false);
        await RunToTheEndAsync(rig, new OcrOptions("eng", Jobs: 2, Pages: new HashSet<int> { 0 }));

        var text = await rig.PageTextAsync(0);
        var known = OcrFixtures.WordsOf("ocr-scan.pdf", 0);
        Assert.NotEmpty(known);
        var found = known.Count(word => text.Contains(word.Text, StringComparison.Ordinal));
        // The engine is not exact; most of the known words must be there.
        Assert.True(found * 10 >= known.Count * 8, $"{found} of {known.Count} known words were found");
        Assert.Contains("Invoice", text, StringComparison.Ordinal);

        using var search = new Raffica.Pdf.Text.SearchSession(rig.Tab, () => 0);
        search.Start("Invoice", default);
        await search.Completion.WaitAsync(TimeSpan.FromSeconds(60), Ct);
        ViewFixture.Pump();
        Assert.NotEmpty(search.Hits);
        Assert.True(rig.Tab.IsDirty);
    }

    [AvaloniaFact]
    public void LibraryHelperFindsTheLibraryOrFailsWhenRequired()
    {
        // Require() fails with RAFFICA_REQUIRE_OCR=1 and skips without it when the library is missing; where it is there, it makes the library available.
        OcrTestLibrary.Require();
        Assert.True(TesseractEngineFactory.IsLibraryAvailable);
    }

    [AvaloniaFact]
    public async Task ProductionWindowSetsOcrRunStarter()
    {
        // Otherwise the feature ships dead: the Recognize command stays off for ever. A window made the way the app makes it has the starter.
        using var f = WindowFixture.Create();
        Assert.NotNull(f.Window.OcrRunStarter);
        f.Window.OcrLibraryProbe = () => true;
        var tab = await f.OpenAsync("ocr-scan.pdf");
        await ViewFixture.WaitAsync(() => f.Window.ViewOf(tab)?.FirstSharpPageShown == true);
        Assert.True(Command(f.Window, "ocr.run").IsEnabled);
        Assert.False(f.Window.OcrFlowCreated);
    }

    [AvaloniaFact]
    public async Task TheCardStartsTheRealRun()
    {
        using var rig = await OcrRig.OpenAsync();
        Directory.CreateDirectory(Path.Combine(rig.Fixture.Folder, "tessdata"));
        File.WriteAllBytes(Path.Combine(rig.Fixture.Folder, "tessdata", "eng.traineddata"), new byte[16]);

        var recognize = rig.Window.RecognizeAsync();
        var card = await rig.CardWithAsync("PART_Recognize");
        OcrRig.Click(card, "PART_Recognize");
        var report = await rig.CardWithAsync("PART_Ok");
        Assert.Contains(PdfStrings.OcrReportTitle, OcrRig.TextsOf(report));
        OcrRig.Click(report, "PART_Ok");
        await recognize;
        await PrintHost.UntilAsync(() => !rig.Window.OcrFlow.IsRunning);

        Assert.Contains(FakeEngineFactory.Secret, await rig.PageTextAsync(0), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task RunIsOneUnsavedHistoryStepAndUndoable()
    {
        using var rig = await OcrRig.OpenAsync();
        Assert.False(rig.Tab.IsDirty);
        await RunToTheEndAsync(rig, Options(jobs: 2));

        Assert.True(rig.Tab.IsDirty);
        for (var page = 0; page < 3; page++)
        {
            Assert.Contains(FakeEngineFactory.Secret, await rig.PageTextAsync(page), StringComparison.Ordinal);
        }

        // Three pages, one step: one Undo takes them all back, one Redo puts them back.
        Assert.True(rig.Tab.Annotations.CanUndo);
        await rig.Tab.Annotations.UndoAsync();
        Assert.False(rig.Tab.Annotations.CanUndo);
        Assert.True(rig.Tab.Annotations.CanRedo);
        for (var page = 0; page < 3; page++)
        {
            Assert.DoesNotContain(FakeEngineFactory.Secret, await rig.PageTextAsync(page), StringComparison.Ordinal);
        }

        await rig.Tab.Annotations.RedoAsync();
        Assert.Contains(FakeEngineFactory.Secret, await rig.PageTextAsync(2), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task CancelWithFinishedPagesAsksKeepOrDiscardDefaultDiscard()
    {
        // Discard: the file is as it was.
        using (var rig = await OcrRig.OpenAsync())
        {
            rig.Engines!.HoldAt(1);
            var run = rig.Window.StartOcrRunAsync(rig.Tab, Options());
            await rig.UntilPagesAsync(1);
            rig.Window.CancelOcr();
            rig.Engines.Release();

            var card = await rig.CardWithAsync("PART_Keep");
            Assert.Equal(PdfStrings.OcrKeepTitle, card.Title);
            // Discard is the default: the primary button, and what Esc answers.
            Assert.Contains("primary", card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Discard").Classes);
            Assert.DoesNotContain("primary", card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Keep").Classes);
            Assert.False(rig.Tab.IsDirty);
            OcrRig.Click(card, "PART_Discard");
            await run;

            Assert.False(rig.Tab.IsDirty);
            Assert.False(rig.Tab.Annotations.CanUndo);
            Assert.DoesNotContain(FakeEngineFactory.Secret, await rig.PageTextAsync(0), StringComparison.Ordinal);
            Assert.False(rig.Tab.OcrIfCreated!.IsActive);
        }

        // Esc is Discard as well.
        using (var rig = await OcrRig.OpenAsync())
        {
            rig.Engines!.HoldAt(1);
            var run = rig.Window.StartOcrRunAsync(rig.Tab, Options());
            await rig.UntilPagesAsync(1);
            rig.Window.CancelOcr();
            rig.Engines.Release();
            await rig.CardWithAsync("PART_Keep");
            rig.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            await run;
            Assert.False(rig.Tab.IsDirty);
        }

        // Keep: the finished pages are written (as many as the button says) and the report follows.
        using (var rig = await OcrRig.OpenAsync())
        {
            rig.Engines!.HoldAt(1);
            var run = rig.Window.StartOcrRunAsync(rig.Tab, Options());
            await rig.UntilPagesAsync(1);
            rig.Window.CancelOcr();
            rig.Engines.Release();

            var card = await rig.CardWithAsync("PART_Keep");
            var kept = int.Parse(Regex.Match((string)card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Keep").Content!, @"\d+").Value, System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(kept, 1, 2);
            OcrRig.Click(card, "PART_Keep");
            var report = await rig.CardWithAsync("PART_Ok");
            Assert.Equal(PdfStrings.OcrReportCanceledTitle, report.Title);
            OcrRig.Click(report, "PART_Ok");
            await run;

            Assert.True(rig.Tab.IsDirty);
            var written = 0;
            for (var page = 0; page < 3; page++)
            {
                written += (await rig.PageTextAsync(page)).Contains(FakeEngineFactory.Secret, StringComparison.Ordinal) ? 1 : 0;
            }

            Assert.Equal(kept, written);
        }
    }

    [AvaloniaFact]
    public async Task HeavyWorkStartsOnlyAfterTheFirstSharpPage()
    {
        // The window is not shown yet, so no page is sharp: the probe has not been asked, and the run neither starts nor is offered.
        using var f = WindowFixture.Create(show: false);
        var calls = new List<bool>();
        f.Window.OcrLibraryProbe = () => true;
        f.Window.OcrEngineFactory = _ => new FakeEngineFactory();
        f.Window.ScanProbe = (_, _, _) =>
        {
            calls.Add(f.Window.ViewOf(f.Window.Workspace.Active!)?.FirstSharpPageShown == true);
            return Task.FromResult(false);
        };
        var tab = await f.OpenAsync("ocr-scan.pdf");
        var view = f.Window.ViewOf(tab);
        Assert.NotNull(view);
        Assert.False(view.FirstSharpPageShown);
        Assert.Empty(calls);
        Assert.False(Command(f.Window, "ocr.run").IsEnabled);
        var early = f.Window.StartOcrRunAsync(tab, Options());
        await PrintHost.UntilAsync(() => f.Window.Host.ActiveDialog is not null);
        // The window is not shown, so the card is not in the visual tree: read its parts directly.
        var note = f.Window.Host.ActiveDialog!;
        Assert.Equal(PdfStrings.OcrNotStartedReason, ((TextBlock)((StackPanel)note.Content!).Children[0]).Text);
        ((Button)((StackPanel)note.Footer!).Children[0]).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await early;
        Assert.Null(tab.OcrIfCreated);
        Assert.False(f.Window.OcrFlowCreated);

        f.Window.Show();
        await ViewFixture.WaitAsync(() => view.FirstSharpPageShown);
        await PrintHost.UntilAsync(() => calls.Count > 0);
        Assert.Equal([true], calls);
        Assert.True(Command(f.Window, "ocr.run").IsEnabled);
    }

    [AvaloniaFact]
    public async Task LocksWhileRunning()
    {
        using var rig = await OcrRig.OpenAsync();
        var window = rig.Window;
        rig.Engines!.HoldAt(1);
        var run = window.StartOcrRunAsync(rig.Tab, Options());
        await rig.UntilPagesAsync(1);

        string[] locked =
        [
            "annotate.mode", "annotate.highlight", "annotate.freehand", "annotate.textBox", "annotate.sign", "forms.next", "pages.organize",
            "redact.mode", "text.edit", "edit.undo", "edit.redo", "file.save", "file.saveAs", "file.print",
            "pages.extract", "pages.split", "pages.exportImages", "ocr.run", "ocr.languages",
        ];
        foreach (var id in locked)
        {
            var command = Command(window, id);
            Assert.False(command.IsEnabled, id);
            Assert.Equal(PdfStrings.ModeOcrReason, command.CurrentDisabledReason);
        }

        // Reading stays: zooming, paging, searching and Cancel itself.
        foreach (var id in new[] { "zoom.in", "zoom.out", "find.search", "pages.next", "layout.single", "ocr.cancel" })
        {
            Assert.True(Command(window, id).IsEnabled, id);
        }

        var before = window.ActiveView!.Zoom;
        Command(window, "zoom.in").Run!();
        ViewFixture.Pump();
        Assert.True(window.ActiveView.Zoom > before);

        // The run is not modal: no card is open, the badge says what runs and is a live region, the status bar counts the pages.
        Assert.Null(rig.Card);
        var badge = window.OcrBadgeOf(rig.Tab);
        Assert.NotNull(badge);
        Assert.True(badge.IsVisible);
        Assert.True(window.BadgeHostOf(rig.Tab)!.IsVisible);
        var text = badge.GetVisualDescendants().OfType<TextBlock>().Single(b => b.Name == "PART_Text");
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(text));
        Assert.Equal(PdfStrings.Fill(PdfStrings.OcrHintFormat, 1, 3), window.Status.Hint);
        Assert.Equal(PdfStrings.Fill(PdfStrings.OcrBadgeProgressFormat, 1, 3), badge.Message);

        rig.Engines.Release();
        var report = await rig.CardWithAsync("PART_Ok");
        // The end is said too (the sentence of the badge), and the mode is still on until the card is closed.
        Assert.Equal(PdfStrings.Fill(PdfStrings.OcrAnnounceDoneFormat, 3), badge.Message);
        OcrRig.Click(report, "PART_Ok");
        await run;

        Assert.False(rig.Tab.OcrIfCreated!.IsActive);
        Assert.Null(window.Status.Hint);
        Assert.True(Command(window, "annotate.highlight").IsEnabled);
        Assert.True(Command(window, "pages.organize").IsEnabled);
        Assert.True(Command(window, "file.save").IsEnabled);
        Assert.True(Command(window, "edit.undo").IsEnabled);
    }

    [AvaloniaFact]
    public async Task CloseCancelsTheRun()
    {
        var time = new ManualTimers();
        using var rig = await OcrRig.OpenAsync(time: time);
        var original = await File.ReadAllBytesAsync(rig.Path, Ct);
        rig.Engines!.HoldAt(1);
        var run = rig.Window.StartOcrRunAsync(rig.Tab, Options());
        await rig.UntilPagesAsync(1);

        // Keep going keeps the tab and the run.
        var closing = rig.Window.Workspace.CloseAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_Discard");
        Assert.Equal(PdfStrings.UnsavedOcrTitle, card.Title);
        OcrRig.Click(card, "PART_Cancel");
        Assert.False(await closing);
        Assert.Contains(rig.Tab, rig.Window.Workspace.Tabs);
        Assert.True(rig.Tab.OcrIfCreated!.IsActive);

        // Stop recognition: the run ends, no page is kept and nothing more is asked, the tab is closed.
        closing = rig.Window.Workspace.CloseAsync(rig.Tab);
        card = await rig.CardWithAsync("PART_Discard");
        time.Advance(ArmingLock.DelayMilliseconds);
        OcrRig.Click(card, "PART_Discard");
        await PrintHost.UntilAsync(() => rig.Window.OcrFlow.RunningTab is not null && rig.Tab.OcrIfCreated!.CancelRequested);
        rig.Engines.Release();
        Assert.True(await closing.WaitAsync(TimeSpan.FromSeconds(20), Ct));
        await run;

        Assert.DoesNotContain(rig.Tab, rig.Window.Workspace.Tabs);
        Assert.False(rig.Window.OcrFlow.IsRunning);
        Assert.Null(rig.Card);
        Assert.Equal(original, await File.ReadAllBytesAsync(rig.Path, Ct));
    }

    [AvaloniaFact]
    public async Task MissingLibraryOrLanguageFailsTheWholeRunWithOneReason()
    {
        // The library: one card with its reason, nothing written, the mode ends, no page of the run is tried.
        using (var rig = await OcrRig.OpenAsync())
        {
            rig.Window.OcrEngineFactory = _ => new FakeEngineFactory { CreateFailure = new OcrEngineException(OcrEngineError.LibraryMissing, "no library") };
            var run = rig.Window.StartOcrRunAsync(rig.Tab, Options());
            var card = await rig.CardWithAsync("PART_Ok");
            Assert.Equal(PdfStrings.OcrFailTitle, card.Title);
            Assert.Equal("Text recognition is not part of this installation.", card.GetVisualDescendants().OfType<TextBlock>().Single(b => b.Name == "PART_Message").Text);
            OcrRig.Click(card, "PART_Ok");
            await run;
            Assert.Null(rig.Card);
            Assert.False(rig.Tab.IsDirty);
            Assert.False(rig.Tab.OcrIfCreated!.IsActive);
        }

        // A language without a file (found while the factory is made): the reason names it.
        using (var rig = await OcrRig.OpenAsync())
        {
            rig.Window.OcrEngineFactory = _ => throw new TessdataMissingException(["deu"], ["eng"]);
            var run = rig.Window.StartOcrRunAsync(rig.Tab, Options());
            var card = await rig.CardWithAsync("PART_Ok");
            Assert.Contains("deu", card.GetVisualDescendants().OfType<TextBlock>().Single(b => b.Name == "PART_Message").Text, StringComparison.Ordinal);
            OcrRig.Click(card, "PART_Ok");
            await run;
            Assert.Null(rig.Card);
            Assert.False(rig.Tab.IsDirty);
        }

        // The engine that refuses at its first page (a damaged language file) is the same whole-run failure, with its own words.
        using (var rig = await OcrRig.OpenAsync())
        {
            rig.Window.OcrEngineFactory = _ => new FakeEngineFactory(_ => throw new OcrEngineException(OcrEngineError.DataCorrupt, "damaged"));
            var run = rig.Window.StartOcrRunAsync(rig.Tab, Options());
            var card = await rig.CardWithAsync("PART_Ok");
            Assert.Equal(PdfStrings.OcrFailDataCorrupt, card.GetVisualDescendants().OfType<TextBlock>().Single(b => b.Name == "PART_Message").Text);
            OcrRig.Click(card, "PART_Ok");
            await run;
            Assert.Null(rig.Card);
            Assert.False(rig.Tab.IsDirty);
        }
    }

    [AvaloniaFact]
    public async Task SavedFileKeepsTheOriginalBytesAsPrefix()
    {
        using var rig = await OcrRig.OpenAsync(realSave: true);
        var original = await File.ReadAllBytesAsync(rig.Path, Ct);
        await RunToTheEndAsync(rig, Options(jobs: 2));
        Assert.True(rig.Tab.IsDirty);

        // Save through the command: the incremental save through SaveController adds to the file, the old bytes stay where they were.
        Command(rig.Window, "file.save").Run!();
        await PrintHost.UntilAsync(() => !rig.Tab.IsDirty && !rig.Window.Host.IsOpen);
        var saved = await File.ReadAllBytesAsync(rig.Path, Ct);
        Assert.True(saved.Length > original.Length);
        Assert.True(saved.AsSpan(0, original.Length).SequenceEqual(original), "the original bytes are not the beginning of the saved file");

        // And the saved file has the text.
        await using var reopened = await new Raffica.Core.Pdf.PdfEngine().OpenAsync(rig.Path, cancellationToken: Ct);
        var text = await reopened.GetPageTextAsync(0, Ct);
        Assert.Contains(FakeEngineFactory.Secret, text.GetText(0, text.Chars.Count), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task StartupAndAnOrdinaryTabCreateNothingOfTheRun()
    {
        using var f = WindowFixture.Create();
        f.Window.OcrLibraryProbe = () => true;
        var tab = await f.OpenAsync("pages-marked.pdf");
        await ViewFixture.WaitAsync(() => f.Window.ViewOf(tab)?.FirstSharpPageShown == true);
        Assert.Null(tab.OcrIfCreated);
        Assert.False(f.Window.OcrFlowCreated);
        Assert.Null(f.Window.OcrBadgeOf(tab));
    }

    [AvaloniaFact]
    public async Task CancelBeforeTheFirstPageKeepsNothingAndAsksNothing()
    {
        using var rig = await OcrRig.OpenAsync();
        var original = await File.ReadAllBytesAsync(rig.Path, Ct);
        using var gate = new ManualResetEventSlim(false);
        // The engines are not ready yet (the factory is made on a pool thread): Cancel arrives before any page was touched.
        rig.Window.OcrEngineFactory = _ =>
        {
            gate.Wait(TimeSpan.FromSeconds(20));
            return rig.Engines!;
        };
        var run = rig.Window.StartOcrRunAsync(rig.Tab, Options());
        await PrintHost.UntilAsync(() => rig.Tab.OcrIfCreated?.IsActive == true);
        rig.Window.CancelOcr();
        gate.Set();
        await run;

        Assert.Null(rig.Card);
        Assert.False(rig.Tab.IsDirty);
        Assert.False(rig.Tab.OcrIfCreated!.IsActive);
        Assert.Equal(0, rig.Engines!.Calls);
        Assert.Equal(original, await File.ReadAllBytesAsync(rig.Path, Ct));
    }

    [AvaloniaFact]
    public async Task CancelTwiceIsHarmlessAndAsksOnce()
    {
        using var rig = await OcrRig.OpenAsync();
        rig.Engines!.HoldAt(1);
        var run = rig.Window.StartOcrRunAsync(rig.Tab, Options());
        await rig.UntilPagesAsync(1);
        rig.Window.CancelOcr();
        rig.Window.CancelOcr();
        Command(rig.Window, "ocr.cancel").Run!();
        rig.Engines.Release();

        var card = await rig.CardWithAsync("PART_Keep");
        OcrRig.Click(card, "PART_Discard");
        await run;
        // One question, no second one behind it.
        Assert.Null(rig.Card);
        Assert.False(rig.Tab.IsDirty);
        Assert.False(rig.Window.OcrFlow.IsRunning);
    }

    [AvaloniaFact]
    public async Task AbandonedRunDropsEverythingAndShowsNoCard()
    {
        using var rig = await OcrRig.OpenAsync();
        var original = await File.ReadAllBytesAsync(rig.Path, Ct);
        rig.Engines!.HoldAt(1);
        var run = rig.Window.StartOcrRunAsync(rig.Tab, Options());
        await rig.UntilPagesAsync(1);

        // The window is going: the run is stopped, nothing of it is kept and no card is shown for it.
        rig.Window.OcrFlow.Abandon();
        rig.Engines.Release();
        await run;
        Assert.Null(rig.Card);
        Assert.False(rig.Tab.IsDirty);
        Assert.False(rig.Window.OcrFlow.IsRunning);
        Assert.Equal(original, await File.ReadAllBytesAsync(rig.Path, Ct));
    }

    [AvaloniaFact]
    public async Task RedoReplacesTheLayerAndSkipPagesAreListed()
    {
        using var rig = await OcrRig.OpenAsync();
        await RunToTheEndAsync(rig, Options(jobs: 2));
        Assert.Contains(FakeEngineFactory.Secret, await rig.PageTextAsync(0), StringComparison.Ordinal);

        // Skip: the pages that have the layer are left alone, each with its reason in the report.
        rig.Engines!.Behavior = _ => new OcrRecognition([new OcrWord("Quokkaone", 100, 100, 300, 150, 90)]);
        var again = rig.Window.StartOcrRunAsync(rig.Tab, Options(jobs: 2));
        var report = await rig.CardWithAsync("PART_Ok");
        Assert.Equal(3, report.GetVisualDescendants().OfType<TextBlock>().Count(b => b.Name == "PART_Line" && b.Text!.StartsWith("Page ", StringComparison.Ordinal)));
        OcrRig.Click(report, "PART_Ok");
        await again;
        Assert.DoesNotContain("Quokkaone", await rig.PageTextAsync(0), StringComparison.Ordinal);

        // Redo: the layer of Raffica is replaced.
        var redo = rig.Window.StartOcrRunAsync(rig.Tab, Options(jobs: 2, mode: OcrMode.Redo));
        report = await rig.CardWithAsync("PART_Ok");
        OcrRig.Click(report, "PART_Ok");
        await redo;
        var text = await rig.PageTextAsync(0);
        Assert.Contains("Quokkaone", text, StringComparison.Ordinal);
        Assert.DoesNotContain(FakeEngineFactory.Secret, text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task AnotherModeInTheWayEndsInANoteWithItsReason()
    {
        using var rig = await OcrRig.OpenAsync();
        Command(rig.Window, "redact.mode").Run!();
        ViewFixture.Pump();
        Assert.True(rig.Tab.Redact.IsActive);

        var start = rig.Window.StartOcrRunAsync(rig.Tab, Options());
        var card = await rig.CardWithAsync("PART_Ok");
        Assert.Contains(PdfStrings.ModeRedactReason, OcrRig.TextsOf(card));
        OcrRig.Click(card, "PART_Ok");
        await start;
        Assert.False(rig.Window.OcrFlowCreated);
    }

    [AvaloniaFact]
    public async Task RecognizeCardDoesNotOpenWhileARunGoes()
    {
        using var rig = await OcrRig.OpenAsync();
        rig.Engines!.HoldAt(1);
        var run = rig.Window.StartOcrRunAsync(rig.Tab, Options());
        await rig.UntilPagesAsync(1);
        await rig.Window.RecognizeAsync();
        Assert.Null(rig.Card);

        rig.Engines.Release();
        var report = await rig.CardWithAsync("PART_Ok");
        OcrRig.Click(report, "PART_Ok");
        await run;
    }

    [AvaloniaFact]
    public async Task StatusHintOfAnotherOwnerIsLeftAlone()
    {
        using var rig = await OcrRig.OpenAsync();
        rig.Engines!.HoldAt(1);
        var run = rig.Window.StartOcrRunAsync(rig.Tab, Options());
        await rig.UntilPagesAsync(1);
        Assert.Equal(PdfStrings.Fill(PdfStrings.OcrHintFormat, 1, 3), rig.Window.Status.Hint);

        // Somebody else (the field codes) took the cell: the end of the run does not clear what is not ours.
        rig.Window.Status.Hint = "Another hint";
        rig.Engines.Release();
        var report = await rig.CardWithAsync("PART_Ok");
        OcrRig.Click(report, "PART_Ok");
        await run;
        Assert.Equal("Another hint", rig.Window.Status.Hint);
    }

    [AvaloniaFact]
    public async Task CancelAfterTheLastPageIsStillACancel()
    {
        using var rig = await OcrRig.OpenAsync();
        // The Cancel arrives after the runner returned its result and before the flow looks at it: the pages are asked about, never written silently.
        rig.Window.OcrFlow.RunReturned = rig.Window.CancelOcr;
        var run = rig.Window.StartOcrRunAsync(rig.Tab, Options(jobs: 2));
        var card = await rig.CardWithAsync("PART_Keep");
        Assert.Equal(PdfStrings.OcrKeepTitle, card.Title);
        Assert.False(rig.Tab.IsDirty);
        OcrRig.Click(card, "PART_Discard");
        await run;
        Assert.Null(rig.Card);
        Assert.False(rig.Tab.IsDirty);
        Assert.DoesNotContain(FakeEngineFactory.Secret, await rig.PageTextAsync(0), StringComparison.Ordinal);

        // Keep writes all three pages and says it was canceled.
        var again = rig.Window.StartOcrRunAsync(rig.Tab, Options(jobs: 2));
        card = await rig.CardWithAsync("PART_Keep");
        OcrRig.Click(card, "PART_Keep");
        var report = await rig.CardWithAsync("PART_Ok");
        Assert.Equal(PdfStrings.OcrReportCanceledTitle, report.Title);
        OcrRig.Click(report, "PART_Ok");
        await again;
        Assert.Contains(FakeEngineFactory.Secret, await rig.PageTextAsync(2), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task CardsNameTheDocument()
    {
        using var rig = await OcrRig.OpenAsync();
        var title = rig.Tab.Title;
        Assert.False(string.IsNullOrEmpty(title));
        rig.Window.OcrFlow.RunReturned = rig.Window.CancelOcr;
        var run = rig.Window.StartOcrRunAsync(rig.Tab, Options(jobs: 2));
        var keep = await rig.CardWithAsync("PART_Keep");
        Assert.Contains(title, keep.GetVisualDescendants().OfType<TextBlock>().Single(b => b.Name == "PART_Message").Text, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Fixture.Folder, string.Join('\n', OcrRig.TextsOf(keep)), StringComparison.Ordinal);
        OcrRig.Click(keep, "PART_Keep");
        var report = await rig.CardWithAsync("PART_Ok");
        Assert.Equal(PdfStrings.Fill(PdfStrings.OcrReportFileFormat, title), report.GetVisualDescendants().OfType<TextBlock>().Single(b => b.Name == "PART_File").Text);
        Assert.DoesNotContain(rig.Fixture.Folder, string.Join('\n', OcrRig.TextsOf(report)), StringComparison.Ordinal);
        OcrRig.Click(report, "PART_Ok");
        await run;
    }
}
