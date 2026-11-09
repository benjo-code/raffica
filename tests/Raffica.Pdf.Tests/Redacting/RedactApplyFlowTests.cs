using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Core.Text;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Printing;
using Raffica.Shell.Controls;
using Xunit;

namespace Raffica.Pdf.Tests.Redacting;

/// <summary>Apply of redactions: the card with the analysis, the way out, the rescue of a page that cannot be redacted in place, the result and the tab that follows the file.</summary>
public sealed class RedactApplyFlowTests
{
    private const string Term = "Zephyrstraße 17";

    // A harmless area on the first page of redact-everywhere.pdf: what makes the page fail is the tiling pattern it paints with, not the area.
    private static readonly PageRect HarmlessArea = new(15, 45, 200, 65);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IReadOnlyList<string> Lines(Dialog card, string section) =>
        [.. PrintHost.Find<StackPanel>(card, section).GetVisualDescendants().OfType<TextBlock>().Where(t => t.Name == "PART_Line").Select(t => t.Text!.TrimStart('•', ' '))];

    private static bool HasSection(Dialog card, string section) => card.GetVisualDescendants().OfType<StackPanel>().Any(p => p.Name == section);

    private static async Task<Dialog> ApplyCardAsync(RedactApplyRig rig, Task<bool>? hold = null)
    {
        _ = hold;
        var run = rig.Flow.RunAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_SaveAs");
        Assert.False(run.IsCompleted);
        return card;
    }

    [AvaloniaFact]
    public async Task AnalysisIsShownBeforeAnythingIsWritten()
    {
        // Removed counts and what was removed whole instead of precisely, by page.
        using (var fonts = await RedactApplyRig.OpenAsync("redact-fonts.pdf"))
        {
            await fonts.MarkTermAsync(Term);
            fonts.MarkArea(0, new PageRect(15, 130, 300, 160));
            var bytes = await File.ReadAllBytesAsync(fonts.Path, Ct);
            var card = await ApplyCardAsync(fonts);

            Assert.Equal("Apply redactions", card.Title);
            Assert.Equal("9 marks on 1 page", PrintHost.Find<TextBlock>(card, "PART_Summary").Text);
            var removed = Lines(card, "PART_Removed");
            var line = Assert.Single(removed);
            Assert.StartsWith("Page 1: Characters removed: ", line, StringComparison.Ordinal);
            Assert.Contains("Text objects split: ", line, StringComparison.Ordinal);
            Assert.Equal("Removed whole instead of precisely", PrintHost.Find<TextBlock>(card, "PART_Whole_Heading").Text);
            Assert.All(Lines(card, "PART_Whole"), l => Assert.Matches(@"^Page 1: (a text object|an image|a path|a form object) ", l));

            // Nothing is written while the card is open: the file is as it was and no other file exists.
            Assert.Equal(bytes, await File.ReadAllBytesAsync(fonts.Path, Ct));
            Assert.Equal([fonts.Path], Directory.GetFiles(fonts.Folder, "*", SearchOption.AllDirectories));
            Assert.Empty(fonts.Rebuilds);
            RedactApplyRig.Click(card, "PART_Back");
        }

        // The cleanup by field name only, the losses with counts, and what is not cleaned.
        using (var rig = await RedactApplyRig.OpenAsync("redact-everywhere-nopattern.pdf"))
        {
            await rig.MarkTermAsync(Term);
            rig.MarkArea(0, new PageRect(248, 40, 290, 62));
            var card = await ApplyCardAsync(rig);

            var cleaned = Lines(card, "PART_Cleaned");
            Assert.Contains("Information field Author", cleaned);
            Assert.Contains("Information field Subject", cleaned);
            Assert.Contains(cleaned, l => l.StartsWith("Bookmarks: up to ", StringComparison.Ordinal));
            Assert.Contains(cleaned, l => l.StartsWith("Attached files: ", StringComparison.Ordinal));
            var all = string.Join('\n', RedactApplyRig.TextsOf(card));
            Assert.DoesNotContain("Quellmann", all, StringComparison.Ordinal);
            Assert.DoesNotContain("Zephyr", all, StringComparison.Ordinal);
            Assert.Contains(Lines(card, "PART_Lost"), l => l.StartsWith("Form fields are no longer reliably fillable (", StringComparison.Ordinal));
            var notCleaned = Lines(card, "PART_NotCleaned");
            Assert.Contains("Metadata and private data (/Metadata, /PieceInfo) of form objects and image objects that come with the pages.", notCleaned);
            Assert.Contains("Actions of annotations (links and other actions) that come with the pages.", notCleaned);
            Assert.Contains(notCleaned, l => l.StartsWith("Annotations outside the marked areas", StringComparison.Ordinal));
            Assert.Contains(notCleaned, l => l.StartsWith("The values of form fields stay", StringComparison.Ordinal));
            Assert.Equal("The file is written anew. This cannot be undone, and no backup copy is made. To keep the original, use \"Save as\".", PrintHost.Find<TextBlock>(card, "PART_Warning").Text);
            RedactApplyRig.Click(card, "PART_Back");
        }

        // The paperclip notes: their files stay and the card says how many.
        using (var rig = await RedactApplyRig.OpenAsync("redact-attachments.pdf"))
        {
            await rig.MarkTermAsync(Term);
            var card = await ApplyCardAsync(rig);

            Assert.Contains("File attachments of annotations (paperclip notes): up to 1. Their embedded files come with the pages and stay in the file.", Lines(card, "PART_NotCleaned"));
            Assert.Contains(Lines(card, "PART_Cleaned"), l => l.StartsWith("Attached files: ", StringComparison.Ordinal));
            RedactApplyRig.Click(card, "PART_Back");
        }
    }

    [AvaloniaFact]
    public async Task AnalysisIsShownInGerman()
    {
        using var rig = await RedactApplyRig.OpenAsync("redact-covered.pdf");
        await rig.MarkTermAsync(Term);
        var before = PdfStrings.Culture;
        PdfStrings.Culture = System.Globalization.CultureInfo.GetCultureInfo("de");
        try
        {
            var card = await ApplyCardAsync(rig);
            Assert.Equal("Schwärzungen anwenden", card.Title);
            Assert.Equal("Entfernte Zeichen: ", Regex.Match(Lines(card, "PART_Removed")[0], "Entfernte Zeichen: ").Value);
            Assert.Equal("Speichern unter…", PrintHost.Find<Button>(card, "PART_SaveAs").Content);
            RedactApplyRig.Click(card, "PART_Back");
        }
        finally
        {
            PdfStrings.Culture = before;
        }
    }

    [AvaloniaFact]
    public async Task SaveAsIsDefaultAndOriginalIsArmedForFourHundredMilliseconds()
    {
        using var rig = await RedactApplyRig.OpenAsync();
        await rig.MarkTermAsync(Term);
        var before = await File.ReadAllBytesAsync(rig.Path, Ct);
        var run = rig.Flow.RunAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_SaveAs");

        // "Save as…" is the primary button, the original is never the default and is locked until the delay has passed.
        Assert.Contains("primary", PrintHost.Find<Button>(card, "PART_SaveAs").Classes);
        var original = PrintHost.Find<Button>(card, "PART_Original");
        Assert.DoesNotContain("primary", original.Classes);
        Assert.False(original.IsEnabled);
        RedactApplyRig.Click(card, "PART_Original");
        PrintHost.Pump();
        Assert.False(run.IsCompleted);
        Assert.Same(card, rig.Host.ActiveDialog);

        rig.Time.Advance(ArmingLock.DelayMilliseconds - 50);
        RedactApplyRig.Click(card, "PART_Original");
        PrintHost.Pump();
        Assert.Same(card, rig.Host.ActiveDialog);

        // Armed: the file at the path of the open document is replaced by the redacted one.
        rig.Time.Advance(100);
        RedactApplyRig.Click(card, "PART_Original");
        var result = await rig.CardWithAsync("PART_Checked");
        RedactApplyRig.Click(result, "PART_Ok");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(60), Ct));

        Assert.NotEqual(before, await File.ReadAllBytesAsync(rig.Path, Ct));
        await using var written = await new PdfEngine().OpenAsync(rig.Path, null, Ct);
        Assert.DoesNotContain(Term, await written.GetTextAsync(0, Ct), StringComparison.Ordinal);
        Assert.Empty(rig.PathChanges);
        Assert.Equal(rig.Path, rig.Tab.Path);

        // Save as is the only way out when the user asked for it.
        using var other = await RedactApplyRig.OpenAsync();
        await other.MarkTermAsync(Term);
        var second = other.Flow.RunAsync(other.Tab, ApplyTarget.SaveAs);
        var saveAsOnly = await other.CardWithAsync("PART_SaveAs");
        Assert.False(RedactApplyRig.Has(saveAsOnly, "PART_Original"));
        RedactApplyRig.Click(saveAsOnly, "PART_Back");
        Assert.False(await second.WaitAsync(TimeSpan.FromSeconds(30), Ct));
    }

    [AvaloniaFact]
    public async Task BackAndEscLeaveEverythingAsItWas()
    {
        using var rig = await RedactApplyRig.OpenAsync();
        await rig.MarkTermAsync(Term);
        var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);
        var version = rig.Session.Version;

        var run = rig.Flow.RunAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_Back");
        RedactApplyRig.Click(card, "PART_Back");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));

        run = rig.Flow.RunAsync(rig.Tab);
        card = await rig.CardWithAsync("PART_Back");
        rig.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        PrintHost.Pump();
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.Equal(version, rig.Session.Version);
        Assert.True(rig.Session.IsActive);
        Assert.Empty(rig.Rebuilds);
        Assert.Equal([rig.Path], Directory.GetFiles(rig.Folder));
    }

    [AvaloniaFact]
    public async Task SignedFileWarnsAndNeverSaysValid()
    {
        using var rig = await RedactApplyRig.OpenAsync("signed-docmdp-1.pdf");
        rig.MarkArea(0, new PageRect(20, 20, 120, 60));
        var card = await ApplyCardAsync(rig);

        var lost = Lines(card, "PART_Lost");
        var signed = Assert.Single(lost, l => l.StartsWith("The file is digitally signed", StringComparison.Ordinal));
        Assert.Contains("report the signature as broken", signed, StringComparison.Ordinal);
        Assert.Contains("The data of a digital signature (certificate name, contents) stays in the file.", Lines(card, "PART_NotCleaned"));
        var banned = new Regex(@"\b(valid|gültig|legally binding|rechtsverbindlich)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.All(RedactApplyRig.TextsOf(card), t => Assert.DoesNotMatch(banned, t));
        RedactApplyRig.Click(card, "PART_Back");
    }

    [AvaloniaFact]
    public async Task RefusedPageNeedsATickOrRemovalBeforeApply()
    {
        // The first page of redact-everywhere.pdf paints with a tiling pattern: PDFium cannot redact around it (Core refuses it as not redactable).
        using var rig = await RedactApplyRig.OpenAsync("redact-everywhere.pdf");
        rig.MarkArea(0, HarmlessArea);
        var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);
        var version = rig.Session.Version;
        var target = Path.Combine(rig.Folder, "out.pdf");
        rig.Picks.Enqueue(target);

        var run = rig.Flow.RunAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_SaveAs");

        // The page is listed with its numbers and a tick; the way out is blocked until it is ticked.
        var tick = PrintHost.Find<CheckBox>(card, "PART_Rescue_0");
        Assert.Matches(@"^Page 1: turn into an image \(\d+ dpi, \d+ x \d+ pixels, up to \d+ MB\)$", (string)tick.Content!);
        Assert.False(tick.IsChecked);
        Assert.False(PrintHost.Find<StackPanel>(card, "PART_Ways").IsEnabled);
        Assert.True(PrintHost.Find<TextBlock>(card, "PART_RescueBlocked").IsVisible);
        Assert.Contains(Lines(card, "PART_Raster"), l => l.StartsWith("Page 1: turned into an image at ", StringComparison.Ordinal));

        // A click that reaches the button around the disabled panel (a script) does nothing either.
        RedactApplyRig.Click(card, "PART_SaveAs");
        PrintHost.Pump();
        Assert.Same(card, rig.Host.ActiveDialog);
        Assert.False(run.IsCompleted);
        Assert.Equal(version, rig.Session.Version);

        // Ticked, it goes on; the page joins the plan as an image (one history step) before the write.
        tick.IsChecked = true;
        PrintHost.Pump();
        Assert.True(PrintHost.Find<StackPanel>(card, "PART_Ways").IsEnabled);
        Assert.False(PrintHost.Find<TextBlock>(card, "PART_RescueBlocked").IsVisible);
        RedactApplyRig.Click(card, "PART_SaveAs");
        var result = await rig.CardWithAsync("PART_Checked");
        RedactApplyRig.Click(result, "PART_Ok");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(60), Ct));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.Equal([0], rig.Session.Plan.RasterPages.Order());
        await using var written = await new PdfEngine().OpenAsync(target, null, Ct);
        Assert.Equal(string.Empty, (await written.GetTextAsync(0, Ct)).Trim());
        Assert.Equal(new PageObjectSummary(Images: 1, Paths: 0, Texts: 0, Others: 0, Annotations: 0), await written.GetPageObjectSummaryAsync(0, Ct));
    }

    [AvaloniaFact]
    public async Task BackAtTheRescueCardLeavesThePlanAsItWas()
    {
        using var rig = await RedactApplyRig.OpenAsync("redact-everywhere.pdf");
        rig.MarkArea(0, HarmlessArea);
        var version = rig.Session.Version;

        var run = rig.Flow.RunAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_Rescue_0");
        RedactApplyRig.Click(card, "PART_Back");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));

        // Nothing was added to the plan: removing the marks of the page is the other way out.
        Assert.Equal(version, rig.Session.Version);
        Assert.Empty(rig.Session.Plan.RasterPages);
        Assert.Equal([rig.Path], Directory.GetFiles(rig.Folder));
    }

    [AvaloniaFact]
    public async Task EncryptedAndPageKeysOfferNoRescue()
    {
        // Encrypted: Raffica does not remove protection silently.
        using (var rig = await RedactApplyRig.OpenAsync("encrypted_hello_world_r3.pdf", "hôtel"))
        {
            rig.MarkArea(0, new PageRect(10, 10, 80, 40));
            var bytes = await File.ReadAllBytesAsync(rig.Path, Ct);
            var run = rig.Flow.RunAsync(rig.Tab);
            var card = await rig.CardWithAsync("PART_Reason");

            Assert.Equal("Redaction not possible", card.Title);
            Assert.False(RedactApplyRig.Has(card, "PART_SaveAs"));
            Assert.False(card.GetVisualDescendants().OfType<CheckBox>().Any());
            Assert.Equal("• The file is encrypted. Raffica does not remove protection silently, so the file is not redacted.", PrintHost.Find<TextBlock>(card, "PART_Reason").Text);
            RedactApplyRig.Click(card, "PART_Back");
            Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(rig.Path, Ct));
            Assert.True(rig.Session.IsActive);
        }

        // Page keys that Raffica cannot remove: the pages are named, no rescue is offered.
        using (var rig = await RedactApplyRig.OpenAsync("redact-objstm.pdf"))
        {
            rig.MarkArea(0, new PageRect(15, 15, 200, 60));
            var run = rig.Flow.RunAsync(rig.Tab);
            var card = await rig.CardWithAsync("PART_Reason");

            Assert.False(card.GetVisualDescendants().OfType<CheckBox>().Any());
            Assert.Equal("• A thumbnail, private data, metadata, actions, attached files or article threads that Raffica cannot remove are on page 2, so the file is not redacted.", PrintHost.Find<TextBlock>(card, "PART_Reason").Text);
            Assert.Equal("Nothing was written. The file is unchanged and the marks are still open.", PrintHost.Find<TextBlock>(card, "PART_Intro").Text);
            RedactApplyRig.Click(card, "PART_Back");
            Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        }
    }

    [AvaloniaFact]
    public async Task SuccessAdoptsTheFileAndRebuildsTheTab()
    {
        using var rig = await RedactApplyRig.OpenAsync("redact-everywhere-nopattern.pdf");
        await rig.MarkTermAsync(Term);
        rig.MarkArea(0, new PageRect(248, 40, 290, 62));
        var original = await File.ReadAllBytesAsync(rig.Path, Ct);
        var target = Path.Combine(rig.Folder, "copy.pdf");
        rig.Picks.Enqueue(target);
        rig.CurrentPage = 1;

        var run = rig.Flow.RunAsync(rig.Tab);
        var apply = await rig.CardWithAsync("PART_SaveAs");
        RedactApplyRig.Click(apply, "PART_SaveAs");
        var result = await rig.CardWithAsync("PART_Checked");

        // The result card: removed, cleaned (read back), checked, and what was not checked (the lines of the check in the UI language).
        Assert.Equal("Redactions applied", result.Title);
        Assert.Equal("copy.pdf", PrintHost.Find<TextBlock>(result, "PART_FileName").Text);
        Assert.NotEmpty(Lines(result, "PART_Removed"));
        Assert.Equal("Cleaned, read back from the written file", PrintHost.Find<TextBlock>(result, "PART_Cleaned_Heading").Text);
        Assert.Contains(Lines(result, "PART_Checked"), l => l.StartsWith("Page text: every removed text occurs in the page text", StringComparison.Ordinal));
        Assert.Contains(Lines(result, "PART_Checked"), l => l.StartsWith("File bytes: ", StringComparison.Ordinal));
        Assert.Contains(Lines(result, "PART_NotChecked"), l => l.StartsWith("Render comparison: only the pages redacted in place", StringComparison.Ordinal));
        Assert.Contains(Lines(result, "PART_NotChecked"), l => l.StartsWith("Page keys: only /Thumb", StringComparison.Ordinal));
        Assert.All(Lines(result, "PART_NotChecked"), l => Assert.DoesNotContain("Raw bytes:", l, StringComparison.Ordinal));
        RedactApplyRig.Click(result, "PART_Ok");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(60), Ct));

        // The original is untouched, the tab follows the new file and is rebuilt once at the page that was shown, and the tab is clean.
        Assert.Equal(original, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.Equal(target, rig.Tab.Path);
        Assert.Equal("copy.pdf", rig.Tab.Title);
        Assert.Equal([target], rig.PathChanges);
        Assert.Equal([(rig.Tab, 1)], rig.Rebuilds);
        Assert.Equal(2, rig.Tab.Session.Document!.PageCount);
        Assert.False(rig.Tab.IsDirty);
        Assert.False(rig.Flow.IsRunning);
        PrintHost.Pump();
        Assert.Null(rig.Host.ActiveDialog);
    }

    [AvaloniaFact]
    public async Task AnUnredactedOriginalThatCouldNotBeDeletedIsNamedAfterTheResult()
    {
        using var rig = await RedactApplyRig.OpenAsync();
        await rig.MarkTermAsync(Term);
        var target = Path.Combine(rig.Folder, "copy.pdf");
        var leftover = Path.Combine(rig.Folder, ".pw-0123456789ab.old");
        rig.Picks.Enqueue(target);
        rig.Flow.Applier = async (session, engine, request, progress, token) =>
        {
            var applied = await Raffica.Core.Redaction.RedactionApplier.ApplyAsync(session, engine, request, progress, token);
            return applied with { Saved = applied.Saved with { LeftoverPath = leftover } };
        };

        var run = rig.Flow.RunAsync(rig.Tab);
        RedactApplyRig.Click(await rig.CardWithAsync("PART_SaveAs"), "PART_SaveAs");
        RedactApplyRig.Click(await rig.CardWithAsync("PART_Checked"), "PART_Ok");

        // After the result card, a warning that names the path and says to delete it.
        var warning = await rig.CardWithAsync("PART_Message");
        Assert.Equal($"The redacted file was written. The unredacted original is still at {leftover}; delete it.", PrintHost.Find<TextBlock>(warning, "PART_Message").Text);
        RedactApplyRig.Click(warning, "PART_Ok");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(60), Ct));
        Assert.Equal(target, rig.Tab.Path);
    }

    [AvaloniaFact]
    public async Task MarksSavedFirstOnAgreementAndNotWhenTheUserSaysBack()
    {
        using var rig = await RedactApplyRig.OpenAsync();
        await rig.MarkTermAsync(Term);
        Assert.NotNull(await AnnotatedFile.HighlightAsync(rig.Tab.Annotations));
        await rig.Tab.Annotations.FlushAsync();
        Assert.True(rig.Tab.Annotations.Editor.IsDirty);
        var saved = 0;
        rig.SaveMarks = async tab =>
        {
            saved++;
            await tab.Annotations.Editor.SaveAsync(new PdfEngine(), tab.Path!, false, new Progress<double>(), Ct);
            return true;
        };

        // Back: nothing is saved, nothing is applied.
        var run = rig.Flow.RunAsync(rig.Tab);
        var question = await rig.CardWithAsync("PART_SaveFirst");
        Assert.Equal("Save the marks first?", question.Title);
        Assert.Contains("then the redactions are applied", PrintHost.Find<TextBlock>(question, "PART_Message").Text, StringComparison.Ordinal);
        RedactApplyRig.Click(question, "PART_Back");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal(0, saved);
        Assert.True(rig.Session.IsActive);

        // Save first: the marks go into the file, then the Apply card follows.
        run = rig.Flow.RunAsync(rig.Tab);
        question = await rig.CardWithAsync("PART_SaveFirst");
        RedactApplyRig.Click(question, "PART_SaveFirst");
        var apply = await rig.CardWithAsync("PART_SaveAs");
        Assert.Equal(1, saved);
        RedactApplyRig.Click(apply, "PART_Back");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
    }
    public static TheoryData<string> OriginalSpellings()
    {
        // The same file, spelled differently: as it is, relative to the working folder, with a detour through a folder, and (on Windows) in capitals.
        var data = new TheoryData<string> { "same", "relative", "detour", "symlink" };
        if (OperatingSystem.IsWindows())
        {
            data.Add("capitals");
            data.Add("prefixed");
        }

        return data;
    }

    private static string Spell(string how, string path) => how switch
    {
        "relative" => Path.GetRelativePath(Environment.CurrentDirectory, path),
        "detour" => Path.Combine(Path.GetDirectoryName(path)!, "detour", "..", Path.GetFileName(path)),
        // A symbolic link and the extended-length spelling are other paths to the same file: only the file system can say so.
        "symlink" => Link(path),
        "prefixed" => @"\\?\" + path,
        "capitals" => Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(path).ToUpperInvariant()),
        _ => path,
    };

    private static string Link(string path)
    {
        var link = Path.Combine(Path.GetDirectoryName(path)!, "link-to-" + Path.GetFileName(path));
        try
        {
            File.CreateSymbolicLink(link, path);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            Assert.Skip("This account or system may not create symbolic links: " + ex.GetType().Name);
        }

        return link;
    }

    [AvaloniaTheory]
    [MemberData(nameof(OriginalSpellings))]
    public async Task SaveAsToTheOriginalPathIsArmed(string how)
    {
        using var rig = await RedactApplyRig.OpenAsync();
        await rig.MarkTermAsync(Term);
        var before = await File.ReadAllBytesAsync(rig.Path, Ct);
        rig.Picks.Enqueue(Spell(how, rig.Path));

        var run = rig.Flow.RunAsync(rig.Tab, ApplyTarget.SaveAs);
        RedactApplyRig.Click(await rig.CardWithAsync("PART_SaveAs"), "PART_SaveAs");

        // The pick is the open file: the armed confirmation stands in front of the write, Back is the default.
        var confirm = await rig.CardWithAsync("PART_Original");
        Assert.Equal("Replace the original file?", confirm.Title);
        Assert.Contains("primary", PrintHost.Find<Button>(confirm, "PART_Back").Classes);
        Assert.DoesNotContain("primary", PrintHost.Find<Button>(confirm, "PART_Original").Classes);
        Assert.False(PrintHost.Find<Button>(confirm, "PART_Original").IsEnabled);
        RedactApplyRig.Click(confirm, "PART_Original");
        PrintHost.Pump();
        rig.Time.Advance(ArmingLock.DelayMilliseconds - 50);
        RedactApplyRig.Click(confirm, "PART_Original");
        PrintHost.Pump();
        Assert.Same(confirm, rig.Host.ActiveDialog);
        Assert.Equal(before, await File.ReadAllBytesAsync(rig.Path, Ct));

        rig.Time.Advance(100);
        RedactApplyRig.Click(confirm, "PART_Original");
        RedactApplyRig.Click(await rig.CardWithAsync("PART_Checked"), "PART_Ok");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(60), Ct));
        if (how == "symlink")
        {
            // The write goes through the link: the file behind it holds the redacted content and the link stays a link.
            Assert.NotEqual(before, await File.ReadAllBytesAsync(rig.Path, Ct));
            return;
        }

        Assert.NotEqual(before, await File.ReadAllBytesAsync(rig.Path, Ct));
        if (how != "prefixed")
        {
            // The extended-length spelling is another string for the path: the tab follows what was picked.
            Assert.Equal(rig.Path, rig.Tab.Path);
            Assert.Empty(rig.PathChanges);
        }
    }

    [AvaloniaFact]
    public async Task CancellingTheOriginalConfirmationLeavesFileAndPlanUnchanged()
    {
        using var rig = await RedactApplyRig.OpenAsync();
        await rig.MarkTermAsync(Term);
        var before = await File.ReadAllBytesAsync(rig.Path, Ct);
        var version = rig.Session.Version;
        rig.Picks.Enqueue(rig.Path);

        var run = rig.Flow.RunAsync(rig.Tab, ApplyTarget.SaveAs);
        RedactApplyRig.Click(await rig.CardWithAsync("PART_SaveAs"), "PART_SaveAs");
        RedactApplyRig.Click(await rig.CardWithAsync("PART_Original"), "PART_Back");

        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        PrintHost.Pump();
        Assert.Null(rig.Host.ActiveDialog);
        Assert.Equal(before, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.Equal(version, rig.Session.Version);
        Assert.True(rig.Session.IsActive);
        Assert.Empty(rig.Rebuilds);
        Assert.Equal([rig.Path], Directory.GetFiles(rig.Folder));
    }

    [AvaloniaFact]
    public async Task ACancelledPickerAfterARescueTickLeavesThePlanAsItWas()
    {
        using var rig = await RedactApplyRig.OpenAsync("redact-everywhere.pdf");
        rig.MarkArea(0, HarmlessArea);
        var version = rig.Session.Version;
        var plan = rig.Session.Plan;

        var run = rig.Flow.RunAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_Rescue_0");
        PrintHost.Find<CheckBox>(card, "PART_Rescue_0").IsChecked = true;
        PrintHost.Pump();
        RedactApplyRig.Click(card, "PART_SaveAs"); // the picker is cancelled: no path is queued

        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal(version, rig.Session.Version);
        Assert.Same(plan, rig.Session.Plan);
        Assert.Empty(rig.Session.Plan.RasterPages);
        Assert.Equal([rig.Path], Directory.GetFiles(rig.Folder));
    }

    [AvaloniaFact]
    public async Task TheOriginalOfTheRescueCardIsArmedFromTheTickNotFromTheCard()
    {
        using var rig = await RedactApplyRig.OpenAsync("redact-everywhere.pdf");
        rig.MarkArea(0, HarmlessArea);
        var before = await File.ReadAllBytesAsync(rig.Path, Ct);

        var run = rig.Flow.RunAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_Rescue_0");

        // The card is old by now; the ways out were blocked meanwhile.
        rig.Time.Advance(5000);
        PrintHost.Find<CheckBox>(card, "PART_Rescue_0").IsChecked = true;
        PrintHost.Pump();
        Assert.False(PrintHost.Find<Button>(card, "PART_Original").IsEnabled);
        RedactApplyRig.Click(card, "PART_Original");
        PrintHost.Pump();
        Assert.Same(card, rig.Host.ActiveDialog);
        Assert.False(run.IsCompleted);

        rig.Time.Advance(ArmingLock.DelayMilliseconds + 50);
        RedactApplyRig.Click(card, "PART_Original");
        RedactApplyRig.Click(await rig.CardWithAsync("PART_Checked"), "PART_Ok");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(60), Ct));
        Assert.NotEqual(before, await File.ReadAllBytesAsync(rig.Path, Ct));
    }

    [AvaloniaFact]
    public async Task SaveAsToAnotherFileIsNotArmedAndWritesNormally()
    {
        using var rig = await RedactApplyRig.OpenAsync();
        await rig.MarkTermAsync(Term);
        var before = await File.ReadAllBytesAsync(rig.Path, Ct);
        var other = rig.CopyOf("redact-covered.pdf", "another.pdf");
        rig.Picks.Enqueue(other);

        var run = rig.Flow.RunAsync(rig.Tab, ApplyTarget.SaveAs);
        RedactApplyRig.Click(await rig.CardWithAsync("PART_SaveAs"), "PART_SaveAs");

        // A file that exists but is another one: the system dialog confirmed replacing it, nothing more is asked.
        RedactApplyRig.Click(await rig.CardWithAsync("PART_Checked"), "PART_Ok");
        Assert.True(await run.WaitAsync(TimeSpan.FromSeconds(60), Ct));
        Assert.Equal(before, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.Equal(other, rig.Tab.Path);
        Assert.NotEqual(before, await File.ReadAllBytesAsync(other, Ct));
    }
}
