using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Core.Pages;
using Raffica.Core.Pdf;
using Raffica.Core.Saving;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Organizing;
using Raffica.Pdf.Tests.Printing;
using Xunit;

namespace Raffica.Pdf.Tests.Organizing;

/// <summary>Extract and split: the cards, the files that result (reopened with Core), the losses told before the write, and every way a write can end.</summary>
public sealed class SplitFlowTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<string[]> TextsAsync(string path)
    {
        await using var document = await new PdfEngine().OpenAsync(path, null, Ct);
        var texts = new string[document.PageCount];
        for (var i = 0; i < texts.Length; i++)
        {
            texts[i] = (await document.GetTextAsync(i, Ct)).Trim();
        }

        return texts;
    }

    private static async Task<int> PageCountAsync(string path)
    {
        await using var document = await new PdfEngine().OpenAsync(path, null, Ct);
        return document.PageCount;
    }

    private static string[] Pdfs(string folder) => Directory.GetFiles(folder, "*.pdf").Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToArray();

    // Types the range and presses Extract; returns the run (the flow goes on to the save dialog, the check and the result).
    private static async Task<Task<bool>> StartExtractAsync(SplitRig rig, string range)
    {
        var run = rig.Flow.ExtractAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_Range");
        PrintHost.Find<TextBox>(card, "PART_Range").Text = range;
        SplitRig.Click(card, "PART_Extract");
        return run;
    }

    // Chooses the mode of the split card, the folder, and presses Split.
    private static async Task<Task<bool>> StartSplitAsync(SplitRig rig, string folder, Action<Control, Func<string, Control>>? fill = null)
    {
        rig.FolderPicks.Enqueue(folder);
        var run = rig.Flow.SplitAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_Split");
        fill?.Invoke(card, name => PrintHost.Find<Control>(card, name));
        SplitRig.Click(card, "PART_ChooseFolder");
        var split = PrintHost.Find<Button>(card, "PART_Split");
        await PrintHost.UntilAsync(() => split.IsEnabled);
        SplitRig.Click(card, "PART_Split");
        return run;
    }

    private static void Choose(Func<string, Control> find, string mode, string? every = null, string? ranges = null)
    {
        ((RadioButton)find(mode)).IsChecked = true;
        if (every is not null)
        {
            ((TextBox)find("PART_Every")).Text = every;
        }

        if (ranges is not null)
        {
            ((TextBox)find("PART_Ranges")).Text = ranges;
        }
    }

    [AvaloniaFact]
    public async Task ExtractWritesTheRangeAsANewFileWithTheRightPageCountAndOrder()
    {
        using var rig = await SplitRig.OpenAsync();
        var before = await TextsAsync(rig.Path);
        var original = await File.ReadAllBytesAsync(rig.Path, Ct);
        var target = Path.Combine(rig.NewFolder(), "mine.pdf");
        rig.SavePicks.Enqueue(target);

        // 5 and 2-3 are given out of order: the new file keeps the order of the document.
        var run = await StartExtractAsync(rig, "5,2-3");
        var result = await rig.ResultAsync();

        Assert.True(File.Exists(target));
        Assert.Equal([before[1], before[2], before[4]], await TextsAsync(target));
        Assert.Contains(SplitRig.TextsOf(result), t => t == "1 file was written.");
        SplitRig.Click(result, "PART_Ok");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal(original, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.StartsWith("pages-marked-pages-5", rig.SuggestedNames.Single(), StringComparison.Ordinal);
        Assert.EndsWith(".pdf", rig.SuggestedNames.Single(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ExtractOfASignedFileListsTheSignatureLossBeforeWriting()
    {
        using var rig = await SplitRig.OpenAsync("signed-docmdp-1.pdf");
        var target = Path.Combine(rig.NewFolder(), "signed-part.pdf");
        rig.SavePicks.Enqueue(target);

        var run = await StartExtractAsync(rig, "1");
        var confirm = await rig.CardWithAsync("PART_Go");

        // The loss is told, and nothing was asked for or written yet.
        Assert.Contains(SplitRig.TextsOf(confirm), t => t is not null && t.Contains(PdfStrings.LossSignature, StringComparison.Ordinal));
        Assert.Empty(rig.SuggestedNames);
        Assert.False(File.Exists(target));

        SplitRig.Click(confirm, "PART_Go");
        var result = await rig.ResultAsync();
        Assert.True(File.Exists(target));
        Assert.Equal(1, await PageCountAsync(target));
        SplitRig.Click(result, "PART_Ok");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
    }

    [AvaloniaFact]
    public async Task BackOnTheLossCardWritesNothingAndNeverAsksForAFile()
    {
        using var rig = await SplitRig.OpenAsync("signed-docmdp-1.pdf");
        rig.SavePicks.Enqueue(Path.Combine(rig.NewFolder(), "never.pdf"));
        var run = await StartExtractAsync(rig, "1");
        await rig.PressAsync("PART_Back");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Empty(rig.SuggestedNames);
        Assert.Empty(Pdfs(Path.Combine(rig.Folder, "out")));
    }

    [AvaloniaFact]
    public async Task SplitEveryTwoPagesGivesTheRightPartsAndPageCounts()
    {
        using var rig = await SplitRig.OpenAsync();
        var before = await TextsAsync(rig.Path);
        var folder = rig.NewFolder();

        var run = await StartSplitAsync(rig, folder, (_, find) => Choose(find, "PART_ModeEvery", every: "2"));
        var result = await rig.ResultAsync();

        var files = Pdfs(folder);
        Assert.Equal(["pages-marked-p001-002.pdf", "pages-marked-p003-004.pdf", "pages-marked-p005-006.pdf"], files);
        Assert.Equal([before[0], before[1]], await TextsAsync(Path.Combine(folder, files[0])));
        Assert.Equal([before[2], before[3]], await TextsAsync(Path.Combine(folder, files[1])));
        Assert.Equal([before[4], before[5]], await TextsAsync(Path.Combine(folder, files[2])));
        Assert.Contains(SplitRig.TextsOf(result), t => t == "3 files were written.");
        SplitRig.Click(result, "PART_Ok");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
    }

    [AvaloniaFact]
    public async Task SplitByRangesGivesTheNamedParts()
    {
        using var rig = await SplitRig.OpenAsync();
        var before = await TextsAsync(rig.Path);
        var folder = rig.NewFolder();

        var run = await StartSplitAsync(rig, folder, (_, find) => Choose(find, "PART_ModeRanges", ranges: "1-2,4-6"));
        SplitRig.Click(await rig.ResultAsync(), "PART_Ok");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));

        Assert.Equal(["pages-marked-p001-002.pdf", "pages-marked-p004-006.pdf"], Pdfs(folder));
        Assert.Equal([before[3], before[4], before[5]], await TextsAsync(Path.Combine(folder, "pages-marked-p004-006.pdf")));
    }

    [AvaloniaFact]
    public async Task SplitByBookmarksUsesTheTopLevelOnly()
    {
        using var rig = await SplitRig.OpenAsync("outline.pdf");
        var folder = rig.NewFolder();
        await using (var document = await new PdfEngine().OpenAsync(rig.Path, null, Ct))
        {
            var outline = await document.GetOutlineAsync(Ct);
            var expected = DocumentSplitter.Plan(document.PageCount, SplitMode.Bookmarks(outline), "outline");
            var names = OutputNames.Unique(expected.Select(p => p.Stem), ".pdf", _ => false).Order(StringComparer.Ordinal).ToArray();
            Assert.True(expected.Count >= 2);

            var run = await StartSplitAsync(rig, folder, (_, find) => Choose(find, "PART_ModeBookmarks"));
            SplitRig.Click(await rig.ResultAsync(), "PART_Ok");
            Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));

            Assert.Equal(names, Pdfs(folder));
            foreach (var part in expected)
            {
                var name = OutputNames.Unique([part.Stem], ".pdf", _ => false)[0];
                Assert.Equal(part.Pages.Count, await PageCountAsync(Path.Combine(folder, name)));
            }
        }
    }

    [AvaloniaFact]
    public async Task BookmarkModeIsDisabledWithAReasonWithoutBookmarks()
    {
        using var rig = await SplitRig.OpenAsync("text-basic.pdf");
        var run = rig.Flow.SplitAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_Split");

        var bookmarks = PrintHost.Find<RadioButton>(card, "PART_ModeBookmarks");
        Assert.False(bookmarks.IsEnabled);
        Assert.Equal("This file has no bookmarks", PrintHost.Find<TextBlock>(card, "PART_BookmarksReason").Text);

        SplitRig.Click(card, "PART_Cancel");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
    }

    [AvaloniaFact]
    public async Task ExistingFilesAreNeverReplacedAndAreNamed()
    {
        using var rig = await SplitRig.OpenAsync();
        var folder = rig.NewFolder();
        var existing = Path.Combine(folder, "pages-marked-p003-004.pdf");
        await File.WriteAllBytesAsync(existing, [1, 2, 3], Ct);

        var run = await StartSplitAsync(rig, folder, (_, find) => Choose(find, "PART_ModeEvery", every: "2"));
        var result = await rig.ResultAsync();

        Assert.Contains(SplitRig.TextsOf(result), t => t is not null && t.Contains("pages-marked-p003-004.pdf", StringComparison.Ordinal) && t.Contains("Nothing was replaced", StringComparison.Ordinal));
        SplitRig.Click(result, "PART_Ok");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(existing, Ct));
        Assert.Equal(["pages-marked-p003-004.pdf"], Pdfs(folder));
    }

    [AvaloniaFact]
    public async Task ExtractReplacesAnExistingFileOnlyWhenTheSaveDialogChoseIt()
    {
        using var rig = await SplitRig.OpenAsync();
        var target = Path.Combine(rig.NewFolder(), "taken.pdf");
        await File.WriteAllBytesAsync(target, [9, 9], Ct);
        rig.SavePicks.Enqueue(target);

        var run = await StartExtractAsync(rig, "1");
        SplitRig.Click(await rig.ResultAsync(), "PART_Ok");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal(1, await PageCountAsync(target));
    }

    [AvaloniaFact]
    public async Task ExtractNeverReplacesTheOpenFile()
    {
        using var rig = await SplitRig.OpenAsync();
        var original = await File.ReadAllBytesAsync(rig.Path, Ct);
        rig.SavePicks.Enqueue(rig.Path);

        var run = await StartExtractAsync(rig, "1-2");
        var result = await rig.ResultAsync();
        Assert.Contains(SplitRig.TextsOf(result), t => t == PdfStrings.OutputFailTargetIsSource);
        SplitRig.Click(result, "PART_Ok");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal(original, await File.ReadAllBytesAsync(rig.Path, Ct));
    }

    [AvaloniaFact]
    public async Task PartFailureNamesThePartAndKeepsTheEarlierParts()
    {
        using var rig = await SplitRig.OpenAsync();
        var folder = rig.NewFolder();
        rig.Flow.Writer = async (engine, source, file, parts, directory, overwrite, progress, token, written) =>
        {
            // The first part is written for real, the second one fails on a full disk.
            await DocumentSplitter.WriteAsync(engine, source, file, [parts[0]], directory, overwrite, progress, token, written);
            var second = DocumentSplitter.TargetPaths(parts, directory)[1];
            throw new DocumentSplitException(DocumentSplitError.PartFailed, "x", [second], 1, new SaveFailureException(SaveFailureReason.DiskFull, "full"));
        };

        var run = await StartSplitAsync(rig, folder, (_, find) => Choose(find, "PART_ModeEvery", every: "2"));
        var result = await rig.ResultAsync();
        var texts = SplitRig.TextsOf(result);

        // The part and the cause are named, the part before it stays and is listed.
        Assert.Contains(texts, t => t is not null && t.Contains("File 2", StringComparison.Ordinal) && t.Contains("pages-marked-p003-004.pdf", StringComparison.Ordinal) && t.Contains(PdfStrings.SaveFailure(SaveFailureReason.DiskFull), StringComparison.Ordinal));
        Assert.Contains(texts, t => t == "pages-marked-p001-002.pdf");
        Assert.Equal(["pages-marked-p001-002.pdf"], Pdfs(folder));
        SplitRig.Click(result, "PART_Ok");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
    }

    [AvaloniaFact]
    public async Task CancelStopsBetweenPartsAndListsWhatWasWritten()
    {
        using var rig = await SplitRig.OpenAsync();
        var folder = rig.NewFolder();
        rig.Flow.Writer = (engine, source, file, parts, directory, overwrite, progress, token, written) =>
            DocumentSplitter.WriteAsync(
                engine, source, file, parts, directory, overwrite, new Cancelling(progress, () => written!.Count >= 1, rig.Flow.Cancel), token, written);

        var run = await StartSplitAsync(rig, folder, (_, find) => Choose(find, "PART_ModeEvery", every: "1"));
        var result = await rig.ResultAsync();

        var onDisk = Pdfs(folder);
        Assert.InRange(onDisk.Length, 1, 5);
        Assert.Contains(SplitRig.TextsOf(result), t => t == PdfStrings.OutputCanceled);
        foreach (var name in onDisk)
        {
            Assert.Contains(SplitRig.TextsOf(result), t => t == name);
            Assert.Equal(1, await PageCountAsync(Path.Combine(folder, name)));
        }

        SplitRig.Click(result, "PART_Ok");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
    }

    [Fact]
    public void HostileRangesAndModesEndInAReason()
    {
        foreach (var ranges in new[] { "0", "5-2", "abc", "1-", "", "7", "999999999" })
        {
            var plan = SplitFlow.PlanFor(6, SplitModeChoice.Ranges, "1", ranges, "doc", []);
            Assert.Null(plan.Parts);
            Assert.False(string.IsNullOrWhiteSpace(plan.Problem), ranges);
        }

        foreach (var every in new[] { "0", "-1", "", "x", "1.5", "99999999999999999999" })
        {
            var plan = SplitFlow.PlanFor(6, SplitModeChoice.Every, every, "all", "doc", []);
            Assert.Null(plan.Parts);
            Assert.False(string.IsNullOrWhiteSpace(plan.Problem), every);
        }

        // More parts than a split may have.
        var tooMany = string.Join(',', Enumerable.Repeat("1", DocumentSplitter.MaxParts + 1));
        var many = SplitFlow.PlanFor(6, SplitModeChoice.Ranges, "1", tooMany, "doc", []);
        Assert.Null(many.Parts);
        Assert.Contains(DocumentSplitter.MaxParts.ToString(CultureInfo.CurrentCulture), many.Problem, StringComparison.Ordinal);

        // No bookmarks: the reason is the one of the card.
        var none = SplitFlow.PlanFor(6, SplitModeChoice.Bookmarks, "1", "all", "doc", []);
        Assert.Equal(PdfStrings.SplitNoBookmarksReason, none.Problem);

        // The good ones still work.
        Assert.Equal(3, SplitFlow.PlanFor(6, SplitModeChoice.Every, "2", "all", "doc", []).Parts!.Count);
        Assert.Equal(2, SplitFlow.PlanFor(6, SplitModeChoice.Ranges, "1", "1-2,3", "doc", []).Parts!.Count);
    }

    [AvaloniaFact]
    public async Task NamesFromHostileBookmarkTitlesStayInsideTheFolder()
    {
        using var rig = await SplitRig.OpenAsync();
        var folder = rig.NewFolder();
        var titles = new[] { @"..\..\x", "CON", new string('A', 300), "<>:\"/\\|?*" };
        rig.Outline = titles.Select((title, i) => new PdfOutlineItem(title, i + 1, false, [])).ToList();
        var before = Directory.GetFileSystemEntries(rig.Folder).Order(StringComparer.Ordinal).ToArray();

        var run = await StartSplitAsync(rig, folder, (_, find) => Choose(find, "PART_ModeBookmarks"));
        SplitRig.Click(await rig.ResultAsync(), "PART_Ok");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));

        // Titles of 4 bookmarks on pages 2 to 5 give a front part and four parts, all directly in the folder; nothing else appeared outside it.
        Assert.Equal(5, Pdfs(folder).Length);

        // The device name and the raw titles are never a file name as they are; the long title is cut.
        Assert.DoesNotContain(Pdfs(folder), name => string.Equals(name, "CON.pdf", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Pdfs(folder), name => name.StartsWith("CON", StringComparison.OrdinalIgnoreCase) && name.Length == 7);
        Assert.DoesNotContain(Pdfs(folder), name => name.StartsWith(new string('A', 200), StringComparison.Ordinal));
        Assert.Contains(Pdfs(folder), name => name.Length > 7 && name.Contains("CON", StringComparison.OrdinalIgnoreCase) && name != "CON.pdf");
        Assert.Equal(before, Directory.GetFileSystemEntries(rig.Folder).Order(StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(Directory.GetFiles(rig.Folder, "*.pdf"), f => !string.Equals(f, rig.Path, StringComparison.OrdinalIgnoreCase));
        Assert.All(Directory.GetFiles(folder), f => Assert.True(Path.GetFileName(f).Length <= 200));
    }

    [AvaloniaFact]
    public async Task FoldersAndNamesWithSpacesNonAsciiAndLongPathsWork()
    {
        using var rig = await SplitRig.OpenAsync();
        var folder = rig.NewFolder("Ordner mit Leerzeichen und Ümläuten – 日本語 " + new string('x', 90));
        Assert.InRange(folder.Length, 120, 240);

        var run = await StartSplitAsync(rig, folder, (_, find) => Choose(find, "PART_ModeEvery", every: "3"));
        SplitRig.Click(await rig.ResultAsync(), "PART_Ok");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal(2, Pdfs(folder).Length);

        var target = Path.Combine(folder, "Seiten für Frau Müller 日本語.pdf");
        rig.SavePicks.Enqueue(target);
        var extract = await StartExtractAsync(rig, "2");
        SplitRig.Click(await rig.ResultAsync(), "PART_Ok");
        Assert.True(await extract.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal(1, await PageCountAsync(target));
    }

    [AvaloniaFact]
    public async Task OriginalFileIsUntouched()
    {
        using var rig = await SplitRig.OpenAsync();
        var original = await File.ReadAllBytesAsync(rig.Path, Ct);
        var folder = rig.NewFolder();

        var run = await StartSplitAsync(rig, folder, (_, find) => Choose(find, "PART_ModeEvery", every: "1"));
        SplitRig.Click(await rig.ResultAsync(), "PART_Ok");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal(original, await File.ReadAllBytesAsync(rig.Path, Ct));

        rig.SavePicks.Enqueue(Path.Combine(folder, "e.pdf"));
        var extract = await StartExtractAsync(rig, "all");
        SplitRig.Click(await rig.ResultAsync(), "PART_Ok");
        Assert.True(await extract.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal(original, await File.ReadAllBytesAsync(rig.Path, Ct));
    }

    [AvaloniaFact]
    public async Task ResultCardOffersOpenForASingleFile()
    {
        using var rig = await SplitRig.OpenAsync();
        var target = Path.Combine(rig.NewFolder(), "one.pdf");
        rig.SavePicks.Enqueue(target);
        var run = await StartExtractAsync(rig, "2");
        var result = await rig.ResultAsync();
        SplitRig.Click(result, "PART_Open");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal([target], rig.Opened);

        // A split offers nothing to open, and the list shows at most ten files and the count.
        var folder = rig.NewFolder("many");
        var split = await StartSplitAsync(rig, folder, (_, find) => Choose(find, "PART_ModeEvery", every: "1"));
        var card = await rig.ResultAsync();
        Assert.DoesNotContain(card.GetVisualDescendants().OfType<Button>(), b => b.Name == "PART_Open");
        SplitRig.Click(card, "PART_Ok");
        Assert.True(await split.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Single(rig.Opened);
    }

    [AvaloniaFact]
    public async Task ResultListShowsAtMostTenFilesAndTheCount()
    {
        using var rig = await SplitRig.OpenAsync("thousand-pages.pdf");
        var folder = rig.NewFolder();
        var run = await StartSplitAsync(rig, folder, (_, find) => Choose(find, "PART_ModeEvery", every: "100"));
        var card = await rig.ResultAsync();
        Assert.Equal(10, Pdfs(folder).Length);

        // Ten files: all shown. A split into more files shows ten and the rest.
        Assert.Equal(10, card.GetVisualDescendants().OfType<TextBlock>().Count(t => t.Name == "PART_File"));
        SplitRig.Click(card, "PART_Ok");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(60), Ct));

        var more = rig.NewFolder("more");
        var second = await StartSplitAsync(rig, more, (_, find) => Choose(find, "PART_ModeEvery", every: "50"));
        var again = await rig.ResultAsync();
        Assert.Equal(10, again.GetVisualDescendants().OfType<TextBlock>().Count(t => t.Name == "PART_File"));
        Assert.Contains(SplitRig.TextsOf(again), t => t == "and 10 more");
        Assert.Contains(SplitRig.TextsOf(again), t => t == "20 files were written.");
        SplitRig.Click(again, "PART_Ok");
        Assert.True(await second.WaitAsync(TimeSpan.FromSeconds(60), Ct));
    }

    [AvaloniaFact]
    public async Task BothAreRefusedWithAnOpenPlanOrUnsavedMarks()
    {
        using var rig = await SplitRig.OpenAsync();
        rig.Tab.Organize.Enter();
        var locked = rig.Flow.ExtractAsync(rig.Tab);
        var card = await rig.ResultAsync();
        Assert.Contains(SplitRig.TextsOf(card), t => t == PdfStrings.PageChangesLockedReason);
        SplitRig.Click(card, "PART_Ok");
        Assert.False(await locked.WaitAsync(TimeSpan.FromSeconds(30), Ct));

        var split = rig.Flow.SplitAsync(rig.Tab);
        var again = await rig.ResultAsync();
        Assert.Contains(SplitRig.TextsOf(again), t => t == PdfStrings.PageChangesLockedReason);
        SplitRig.Click(again, "PART_Ok");
        Assert.False(await split.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Empty(rig.SuggestedNames);
        Assert.Equal(0, rig.FolderAsked);

        // Unsaved marks lock them too: the files are cut from the file on disk.
        rig.Tab.Organize.End();
        using var pending = rig.Tab.Annotations.BeginUnresolved();
        var marks = rig.Flow.ExtractAsync(rig.Tab);
        var notice = await rig.CardWithAsync("PART_Ok");
        Assert.Contains(SplitRig.TextsOf(notice), t => t == PdfStrings.SaveMarksFirstReason);
        SplitRig.Click(notice, "PART_Ok");
        Assert.False(await marks.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        var marksSplit = rig.Flow.SplitAsync(rig.Tab);
        var again2 = await rig.CardWithAsync("PART_Ok");
        Assert.Contains(SplitRig.TextsOf(again2), t => t == PdfStrings.SaveMarksFirstReason);
        SplitRig.Click(again2, "PART_Ok");
        Assert.False(await marksSplit.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Empty(rig.SuggestedNames);
        Assert.Equal(0, rig.FolderAsked);
    }

    [AvaloniaFact]
    public async Task TheEngineIsResolvedOnTheUiThread()
    {
        using var rig = await SplitRig.OpenAsync();
        var folder = rig.NewFolder();
        var run = await StartSplitAsync(rig, folder, (_, find) => Choose(find, "PART_ModeEvery", every: "3"));
        SplitRig.Click(await rig.ResultAsync(), "PART_Ok");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.NotEmpty(rig.EngineOnUiThread);
        Assert.All(rig.EngineOnUiThread, onUi => Assert.True(onUi));
    }

    [AvaloniaFact]
    public async Task ThePartFailureCausesHaveTheirOwnWording()
    {
        using var rig = await SplitRig.OpenAsync();
        var folder = rig.NewFolder();
        var causes = new Exception[] { new PdfOpenException(PdfOpenError.NotAPdf), new InvalidDataException("x"), new IOException("x") };
        foreach (var cause in causes)
        {
            var text = SplitFlow.FailureText(new DocumentSplitException(DocumentSplitError.PartFailed, "x", [Path.Combine(folder, "a.pdf")], 0, cause));
            Assert.NotEqual(PdfStrings.OutputFailOther, text);
            Assert.DoesNotContain("{", text, StringComparison.Ordinal);
        }
    }

    // A draft that ends during the flush is a mark made before the locks are read: the flow does not refuse because of a nudge that was about to finish.
    [AvaloniaFact]
    public async Task OpenDraftIsFlushedBeforeTheLocksAreRead()
    {
        using var rig = await SplitRig.OpenAsync();
        var pending = rig.Tab.Annotations.BeginPending();
        var run = rig.Flow.ExtractAsync(rig.Tab);
        pending.Dispose();

        // Without the flush the refusal "Save the marks first" would have been told at once.
        var card = await rig.CardWithAsync("PART_Range");
        Assert.DoesNotContain(SplitRig.TextsOf(card), t => t == PdfStrings.SaveMarksFirstReason);
        SplitRig.Click(card, "PART_Cancel");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
    }

    // The outline is read without a job card: a mode that starts meanwhile refuses the split before its card.
    [AvaloniaFact]
    public async Task ModeStartedDuringTheOutlineReadRefusesTheSplit()
    {
        using var rig = await SplitRig.OpenAsync();
        var gate = new TaskCompletionSource();
        rig.OutlineGate = gate.Task;
        var run = rig.Flow.SplitAsync(rig.Tab);

        rig.Tab.Organize.Enter();
        gate.SetResult();

        var notice = await rig.CardWithAsync("PART_Ok");
        Assert.Contains(SplitRig.TextsOf(notice), t => t == PdfStrings.PageChangesLockedReason);
        SplitRig.Click(notice, "PART_Ok");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal(0, rig.FolderAsked);
        Assert.False(rig.Flow.IsRunning);
    }

    // The range card, the check and the save dialog take time: the locks are read again before anything is written.
    [AvaloniaFact]
    public async Task ModeStartedWhileTheRangeCardIsOpenRefusesTheExtractBeforeTheWrite()
    {
        using var rig = await SplitRig.OpenAsync();
        var target = System.IO.Path.Combine(rig.NewFolder(), "extracted.pdf");
        rig.SavePicks.Enqueue(target);
        var run = rig.Flow.ExtractAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_Range");

        rig.Tab.Organize.Enter();
        SplitRig.Click(card, "PART_Extract");

        var notice = await rig.ResultAsync();
        Assert.Contains(SplitRig.TextsOf(notice), t => t == PdfStrings.PageChangesLockedReason);
        SplitRig.Click(notice, "PART_Ok");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.False(File.Exists(target));
        Assert.Empty(rig.SuggestedNames);
    }

    [Fact]
    public void NoTextSaysValidOrLegallyBinding()
    {
        var banned = new Regex(@"\b(valid|gültig|legally binding|rechtsverbindlich)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var keys = typeof(PdfStrings).GetProperties()
            .Where(p => p.Name.StartsWith("Extract", StringComparison.Ordinal) || p.Name.StartsWith("Split", StringComparison.Ordinal) || p.Name.StartsWith("Output", StringComparison.Ordinal))
            .Select(p => p.Name)
            .ToList();
        Assert.True(keys.Count >= 40);
        foreach (var culture in new[] { "en", "de" })
        {
            var info = CultureInfo.GetCultureInfo(culture);
            Assert.All(keys, key => Assert.False(banned.IsMatch(PdfStrings.GetIn(key, info)), key + " " + culture));
        }
    }

    // Cancels the flow once the condition holds (after the first part is written), from the PDFium thread as the user's Cancel would.
    private sealed class Cancelling(IProgress<double>? inner, Func<bool> condition, Action cancel) : IProgress<double>
    {
        public void Report(double value)
        {
            inner?.Report(value);
            if (condition())
            {
                cancel();
            }
        }
    }
}
