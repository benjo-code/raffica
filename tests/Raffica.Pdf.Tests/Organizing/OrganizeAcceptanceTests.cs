using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Raffica.Core.Pages;
using Raffica.Core.Pdf;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Organizing;
using Raffica.Pdf.Tests.Printing;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Organizing;

/// <summary>
/// The closing checks of milestone 2.4: the flows of the window (insert file, apply, extract, split) run end to end on real files in temp folders,
/// and every result is reopened with Core's engine as an independent reader of the bytes that were written.
/// </summary>
public sealed class OrganizeAcceptanceTests
{
    private const string Password = "hôtel";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Written(int Count, string[] Texts, int[] Rotations, IReadOnlyList<PdfPageSize> Sizes, IReadOnlyList<PdfOutlineItem> Outline, bool IsEncrypted);

    private static async Task<Written> ReadAsync(string path, string? password = null)
    {
        await using var document = await new PdfEngine().OpenAsync(path, password, Ct);
        var texts = new string[document.PageCount];
        var rotations = new int[document.PageCount];
        var sizes = new List<PdfPageSize>();
        for (var i = 0; i < texts.Length; i++)
        {
            texts[i] = (await document.GetTextAsync(i, Ct)).Trim();
            rotations[i] = await document.GetPageRotationAsync(i, Ct);
            sizes.Add(await document.GetPageSizeAsync(i, Ct));
        }

        return new Written(texts.Length, texts, rotations, sizes, await document.GetOutlineAsync(Ct), (await document.GetPrintInfoAsync(Ct)).IsEncrypted);
    }

    private static string Output(ApplyRig rig, string name = "out.pdf") => Path.Combine(rig.Folder, name);

    private static InsertFileFlow InsertFlow(string path, string range, InsertPosition position, string? password = null) =>
        new(
            () => Task.FromResult<string?>(path),
            (_, _) => Task.FromResult(password),
            (_, _, _) => Task.FromResult<InsertChoice?>(new InsertChoice(range, position)),
            _ => Task.CompletedTask,
            () => new PdfEngine());

    private static IEnumerable<(string Title, int? Page)> Flatten(IReadOnlyList<PdfOutlineItem> items)
    {
        foreach (var item in items)
        {
            yield return (item.Title, item.PageIndex);
            foreach (var child in Flatten(item.Children))
            {
                yield return child;
            }
        }
    }

    [AvaloniaFact]
    public async Task MergeThroughInsertFileHasTheRightPageCountAndOrder()
    {
        using var rig = await ApplyRig.OpenAsync();
        var main = await ReadAsync(rig.Path);
        var hello = await ReadAsync(TestData.Pdf("hello_world.pdf"));
        var third = rig.CopyOf("print-mixed.pdf", "third.pdf");
        var thirdRead = await ReadAsync(third);
        Assert.Equal(6, main.Count);
        Assert.True(thirdRead.Count >= 3);

        // The pages taken from the third file must be told apart by their text (and by their size), or the tail could not fail.
        Assert.Equal(thirdRead.Count, thirdRead.Texts.Distinct().Count());
        Assert.All(thirdRead.Texts[1..3], t => Assert.NotEmpty(t));
        Assert.NotEmpty(main.Outline);

        // hello_world.pdf goes after page 3 (the selection), pages 2 to 3 of the third file at the end.
        rig.Session.Selection.Set(6, [2]);
        Assert.True((await InsertFlow(rig.CopyOf("hello_world.pdf"), "all", InsertPosition.AfterSelection).RunAsync(rig.Tab))?.IsDone);
        Assert.True((await InsertFlow(third, "2-3", InsertPosition.AtEnd).RunAsync(rig.Tab))?.IsDone);
        Assert.Equal(6 + hello.Count + 2, rig.Session.Plan.Slots.Count);

        var target = Output(rig, "merged.pdf");
        rig.Picks.Enqueue(target);
        Assert.True(await rig.ApplyAsync());

        var merged = await ReadAsync(target);
        Assert.Equal(6 + hello.Count + 2, merged.Count);
        string[] expected = [.. main.Texts[..3], .. hello.Texts, .. main.Texts[3..], thirdRead.Texts[1], thirdRead.Texts[2]];
        Assert.Equal(expected, merged.Texts);
        Assert.Equal([thirdRead.Sizes[1], thirdRead.Sizes[2]], [merged.Sizes[^2], merged.Sizes[^1]]);

        // The bookmarks of the main file stay and follow their pages: the page a bookmark points at still holds the text it held.
        Assert.Equal(Flatten(main.Outline).Select(i => i.Title), Flatten(merged.Outline).Select(i => i.Title));
        foreach (var (before, after) in Flatten(main.Outline).Zip(Flatten(merged.Outline)))
        {
            Assert.NotNull(after.Page);
            Assert.Equal(main.Texts[before.Page!.Value], merged.Texts[after.Page.Value]);
        }
    }

    [AvaloniaFact]
    public async Task SplitEveryNAndByRangesAndByBookmarksReopenWithTheRightPageCounts()
    {
        // Every 4 pages of the six: 4 and 2.
        using (var rig = await SplitRig.OpenAsync())
        {
            var before = await ReadAsync(rig.Path);
            var folder = rig.NewFolder();
            var run = await StartSplitAsync(rig, folder, find => Choose(find, "PART_ModeEvery", every: "4"));
            SplitRig.Click(await rig.ResultAsync(), "PART_Ok");
            Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));

            var files = Pdfs(folder);
            Assert.Equal(2, files.Length);
            var first = await ReadAsync(Path.Combine(folder, files[0]));
            var second = await ReadAsync(Path.Combine(folder, files[1]));
            Assert.Equal(before.Texts[..4], first.Texts);
            Assert.Equal(before.Texts[4..], second.Texts);
        }

        // By ranges, with a gap: pages 3 and 4 are in no part.
        using (var rig = await SplitRig.OpenAsync())
        {
            var before = await ReadAsync(rig.Path);
            var folder = rig.NewFolder();
            var run = await StartSplitAsync(rig, folder, find => Choose(find, "PART_ModeRanges", ranges: "1-2,5-6"));
            SplitRig.Click(await rig.ResultAsync(), "PART_Ok");
            Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));

            var files = Pdfs(folder);
            Assert.Equal(2, files.Length);
            Assert.Equal(before.Texts[..2], (await ReadAsync(Path.Combine(folder, files[0]))).Texts);
            Assert.Equal(before.Texts[4..], (await ReadAsync(Path.Combine(folder, files[1]))).Texts);
        }

        // By the top-level bookmarks of pages-marked.pdf (pages 1, 3 and 5): three parts of two pages, with their own text.
        using (var rig = await SplitRig.OpenAsync())
        {
            var before = await ReadAsync(rig.Path);
            Assert.Equal([0, 2, 4], before.Outline.Select(i => i.PageIndex!.Value));
            var folder = rig.NewFolder();
            var run = await StartSplitAsync(rig, folder, find => Choose(find, "PART_ModeBookmarks"));
            SplitRig.Click(await rig.ResultAsync(), "PART_Ok");
            Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));

            // The names follow the bookmark titles; the parts are told apart by their text.
            var parts = new List<string[]>();
            foreach (var file in Pdfs(folder))
            {
                parts.Add((await ReadAsync(Path.Combine(folder, file))).Texts);
            }

            Assert.Equal(3, parts.Count);
            Assert.Equivalent(new[] { before.Texts[..2], before.Texts[2..4], before.Texts[4..] }, parts, strict: false);
        }

        // By the top-level bookmarks of outline.pdf: each part reopens with the page count Core's plan gives it, under its own name.
        using (var rig = await SplitRig.OpenAsync("outline.pdf"))
        {
            var folder = rig.NewFolder();
            await using var document = await new PdfEngine().OpenAsync(rig.Path, null, Ct);
            var expected = DocumentSplitter.Plan(document.PageCount, SplitMode.Bookmarks(await document.GetOutlineAsync(Ct)), "outline");
            Assert.True(expected.Count >= 2);
            var names = OutputNames.Unique(expected.Select(p => p.Stem), ".pdf", _ => false);
            var run = await StartSplitAsync(rig, folder, find => Choose(find, "PART_ModeBookmarks"));
            SplitRig.Click(await rig.ResultAsync(), "PART_Ok");
            Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));

            Assert.Equal(names.Order(StringComparer.Ordinal), Pdfs(folder));
            Assert.Equal(document.PageCount, expected.Sum(p => p.Pages.Count));
            for (var k = 0; k < expected.Count; k++)
            {
                Assert.Equal(expected[k].Pages.Count, (await ReadAsync(Path.Combine(folder, names[k]))).Count);
            }
        }
    }

    [AvaloniaFact]
    public async Task ExtractReopensWithTheRangeOnly()
    {
        using var rig = await SplitRig.OpenAsync();
        var before = await ReadAsync(rig.Path);
        var target = Path.Combine(rig.NewFolder(), "part.pdf");
        rig.SavePicks.Enqueue(target);

        var run = rig.Flow.ExtractAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_Range");
        PrintHost.Find<TextBox>(card, "PART_Range").Text = "2,4-5";
        SplitRig.Click(card, "PART_Extract");
        SplitRig.Click(await rig.ResultAsync(), "PART_Ok");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));

        var extracted = await ReadAsync(target);
        Assert.Equal(3, extracted.Count);
        Assert.Equal([before.Texts[1], before.Texts[3], before.Texts[4]], extracted.Texts);
        Assert.DoesNotContain(before.Texts[0], extracted.Texts);
    }

    [AvaloniaFact]
    public async Task RotateDeleteMoveDuplicateBlankAreAllInOneApply()
    {
        using var rig = await ApplyRig.OpenAsync();
        var before = await ReadAsync(rig.Path);
        var session = rig.Session;

        session.Selection.Set(6, [0]);
        Assert.True(session.Delete().IsDone); // 1 2 3 4 5
        session.Selection.Set(5, [1]);
        Assert.True(session.Duplicate().IsDone); // 1 2 2' 3 4 5
        session.Selection.Set(6, [5]);
        Assert.True(session.Move(0).IsDone); // 5 1 2 2' 3 4
        session.Selection.Set(6, [1]);
        Assert.True(session.Rotate(90).IsDone);
        session.Selection.Set(6, [3]);
        Assert.True(session.Rotate(180).IsDone);
        Assert.True(session.InsertBlank(2, new BlankPage(300, 400)).IsDone); // 5 1 B 2 2' 3 4
        Assert.Equal(7, session.Plan.Slots.Count);

        var target = Output(rig);
        rig.Picks.Enqueue(target);
        Assert.True(await rig.ApplyAsync());

        var written = await ReadAsync(target);
        Assert.Equal(7, written.Count);
        Assert.Equal([before.Texts[5], before.Texts[1], string.Empty, before.Texts[2], before.Texts[2], before.Texts[3], before.Texts[4]], written.Texts);
        Assert.Equal(
            [before.Rotations[5], (before.Rotations[1] + 90) % 360, 0, before.Rotations[2], (before.Rotations[2] + 180) % 360, before.Rotations[3], before.Rotations[4]],
            written.Rotations);
        Assert.Equal(300, written.Sizes[2].Width, 0.5);
        Assert.Equal(400, written.Sizes[2].Height, 0.5);
    }

    [AvaloniaFact]
    public async Task OriginalBytesAreUnchangedAfterSaveAs()
    {
        using var rig = await ApplyRig.OpenAsync();
        var original = await File.ReadAllBytesAsync(rig.Path, Ct);
        var fixture = await File.ReadAllBytesAsync(TestData.Pdf("pages-marked.pdf"), Ct);
        var fixtureTexts = (await ReadAsync(rig.Path)).Texts;
        rig.Session.Selection.Set(6, [0, 1]);
        Assert.True(rig.Session.Delete().IsDone);
        rig.Session.Selection.Set(4, [3]);
        Assert.True(rig.Session.Rotate(270).IsDone);
        var target = Output(rig, "other.pdf");
        rig.Picks.Enqueue(target);

        Assert.True(await rig.ApplyAsync());

        Assert.Equal(original, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.Equal(fixture, await File.ReadAllBytesAsync(TestData.Pdf("pages-marked.pdf"), Ct));
        var written = await ReadAsync(target);
        Assert.Equal(4, written.Count);
        Assert.Equal(fixtureTexts[2..], written.Texts);
    }

    [AvaloniaFact]
    public async Task UndoToTheIdentityAppliesNothingAndWritesNothing()
    {
        using var rig = await ApplyRig.OpenAsync();
        var original = await File.ReadAllBytesAsync(rig.Path, Ct);
        var filesBefore = Directory.GetFileSystemEntries(rig.Folder).Order(StringComparer.Ordinal).ToArray();
        rig.Session.Selection.Set(6, [0]);
        Assert.True(rig.Session.Rotate(90).IsDone);
        Assert.True(rig.Session.Delete().IsDone);
        Assert.True(rig.Tab.IsDirty);
        rig.Session.Undo();
        rig.Session.Undo();
        Assert.True(rig.Session.Plan.IsIdentity);
        Assert.False(rig.Tab.IsDirty);
        rig.Picks.Enqueue(Output(rig));

        Assert.False(await rig.Flow.RunAsync(rig.Tab));

        PrintHost.Pump();
        Assert.Null(rig.Host.ActiveDialog);
        Assert.Empty(rig.Rebuilds);
        Assert.Empty(rig.PathChanges);
        Assert.Single(rig.Picks); // the save picker was never consulted
        Assert.Equal(original, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.Equal(filesBefore, Directory.GetFileSystemEntries(rig.Folder).Order(StringComparer.Ordinal).ToArray());
    }

    [AvaloniaFact]
    public async Task SignedFileApplyShowsTheLossAndWritesAFileThatOpens()
    {
        using var rig = await ApplyRig.OpenAsync("signed-docmdp-2.pdf");
        var original = await File.ReadAllBytesAsync(rig.Path, Ct);
        var before = await ReadAsync(rig.Path);
        rig.Session.Selection.Set(rig.Session.Plan.Slots.Count, [0]);
        Assert.True(rig.Session.Rotate(90).IsDone);
        var target = Output(rig);
        rig.Picks.Enqueue(target);

        var run = rig.Flow.RunAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_SaveAs");
        var line = Assert.Single(ApplyRig.TextsOf(card), t => t is not null && t.Contains("digitally signed", StringComparison.Ordinal));
        Assert.Contains("broken", line, StringComparison.Ordinal);
        Assert.Contains("change level 2", line, StringComparison.Ordinal);
        Assert.False(File.Exists(target));
        ApplyRig.Click(card, "PART_SaveAs");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));

        var written = await ReadAsync(target);
        Assert.Equal(before.Count, written.Count);

        // The page is turned, so the engine may read its lines in another order: the same lines are there.
        Assert.Equal(Lines(before.Texts[0]), Lines(written.Texts[0]));
        Assert.Equal(["Signed document", "Structure dummy, no real signature."], Lines(written.Texts[0]));
        Assert.Equal((before.Rotations[0] + 90) % 360, written.Rotations[0]);
        Assert.Equal(original, await File.ReadAllBytesAsync(rig.Path, Ct));
    }

    [AvaloniaFact]
    public async Task EncryptedFileStaysEncryptedAfterApply()
    {
        using var rig = await ApplyRig.OpenAsync("encrypted_hello_world_r3.pdf", Password);
        var before = await ReadAsync(rig.Path, Password);
        Assert.True(before.IsEncrypted);
        var plain = rig.CopyOf("pages-marked.pdf");
        var plainRead = await ReadAsync(plain);

        // A page of a plain file goes in after the encrypted file's own pages, and its first page is turned.
        Assert.True((await InsertFlow(plain, "2", InsertPosition.AtEnd).RunAsync(rig.Tab))?.IsDone);
        rig.Session.Selection.Set(rig.Session.Plan.Slots.Count, [0]);
        Assert.True(rig.Session.Rotate(90).IsDone);
        var target = Output(rig);
        rig.Picks.Enqueue(target);

        Assert.True(await rig.ApplyAsync());

        var written = await ReadAsync(target, Password);
        Assert.True(written.IsEncrypted);
        Assert.Equal(before.Count + 1, written.Count);
        Assert.Equal([.. before.Texts, plainRead.Texts[1]], written.Texts);
        Assert.Equal((before.Rotations[0] + 90) % 360, written.Rotations[0]);
        await Assert.ThrowsAsync<PdfOpenException>(async () => await new PdfEngine().OpenAsync(target, null, Ct));
    }

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Order(StringComparer.Ordinal).ToArray();

    private static string[] Pdfs(string folder) => Directory.GetFiles(folder, "*.pdf").Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToArray();

    // The split card: choose the mode and its field, pick the folder, press Split.
    private static async Task<Task<bool>> StartSplitAsync(SplitRig rig, string folder, Action<Func<string, Control>> fill)
    {
        rig.FolderPicks.Enqueue(folder);
        var run = rig.Flow.SplitAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_Split");
        fill(name => PrintHost.Find<Control>(card, name));
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
}
