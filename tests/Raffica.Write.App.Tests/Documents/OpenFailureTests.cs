using System.IO.Compression;
using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Write.Documents;
using Xunit;

namespace Raffica.WriteApp.Tests.Documents;

/// <summary>A file that cannot be opened ends in a card with a reason, makes no tab, leaves the app alive and holds nothing (Review Focus 3 of the plan).</summary>
public sealed class OpenFailureTests
{
    private static byte[] ZipWith(string entry)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = zip.CreateEntry(entry).Open();
            stream.Write("hello"u8);
        }

        return memory.ToArray();
    }

    private static void AssertCardAndNoTab(WriteWindow window, string fileName, OpenError error)
    {
        Assert.True(window.Host.IsOpen, "a card was expected");
        var texts = WindowFixture.CardTexts(window);
        Assert.Contains(fileName, texts);
        Assert.Contains(WriteStrings.ForOpenError(error), texts);
        Assert.Equal(WriteStrings.ErrorTitle, window.Host.ActiveDialog!.Title);
        Assert.Empty(window.Workspace.Tabs);
        Assert.Empty(window.Tabs.Items);
        Assert.Same(window.Start, window.Frame.Document);
        Assert.True(window.IsVisible);
    }

    private static async Task DismissAndOpenAGoodFileAsync(WriteWindow window, string good)
    {
        // The card goes away with OK, and the app opens the next file as if nothing had happened.
        window.Host.ActiveDialog!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Ok").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WindowFixture.Pump();
        Assert.False(window.Host.IsOpen);
        Assert.IsType<Opened>(await window.Flow.OpenAsync(good));
        Assert.Single(window.Workspace.Tabs);
    }

    public static TheoryData<string, OpenError> Cases => new()
    {
        { "text", OpenError.NotADocx },
        { "empty", OpenError.NotADocx },
        { "other-zip", OpenError.NotADocx },
        { "truncated", OpenError.Corrupt },
        { "garbage-part", OpenError.Corrupt },
        { "deleted", OpenError.NotFound },
        { "folder", OpenError.NotADocx },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public async Task OpenFailureShowsACardAndMakesNoTab(string kind, OpenError expected)
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var poi = File.ReadAllBytes(WindowFixture.Poi);
        var path = kind switch
        {
            "text" => fixture.Write("notes.docx", Encoding.UTF8.GetBytes("this is not a zip file at all")),
            "empty" => fixture.Write("empty.docx", []),
            "other-zip" => fixture.Write("sheet.docx", ZipWith("xl/workbook.xml")),
            "truncated" => fixture.Write("cut.docx", poi[..(poi.Length / 2)]),
            "garbage-part" => fixture.Write("broken.docx", Corrupted(poi)),
            "deleted" => Path.Combine(fixture.Folder, "gone.docx"),
            _ => Directory.CreateDirectory(Path.Combine(fixture.Folder, "folder.docx")).FullName,
        };

        await window.OpenFilesAsync([path]);
        WindowFixture.Pump();

        AssertCardAndNoTab(window, Path.GetFileName(path), expected);
        Assert.Equal(0, window.Work.LayoutsCreated);
        await DismissAndOpenAGoodFileAsync(window, fixture.Copy(WindowFixture.Poi, "good.docx"));
    }

    // A real package whose main part is not XML: it opens as a package and fails to read, so it is damaged and not "no Word document".
    private static byte[] Corrupted(byte[] docx)
    {
        using var input = new MemoryStream(docx);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var copy = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in archive.Entries)
            {
                using var target = copy.CreateEntry(entry.FullName).Open();
                if (entry.FullName == "word/document.xml")
                {
                    target.Write("<<< not xml"u8);
                }
                else
                {
                    using var source = entry.Open();
                    source.CopyTo(target);
                }
            }
        }

        return output.ToArray();
    }

    [AvaloniaFact]
    public async Task ALockedFileEndsInACardAndMakesNoTab()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var path = fixture.Copy(WindowFixture.Poi, "locked.docx");

        // Another program holds the file and lets nobody read it (a lock the platform enforces; Windows and a Unix lock through FileShare.None).
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            await window.OpenFilesAsync([path]);
            WindowFixture.Pump();

            Assert.True(window.Host.IsOpen);
            var texts = WindowFixture.CardTexts(window);
            Assert.Contains("locked.docx", texts);
            Assert.True(
                texts.Contains(WriteStrings.ForOpenError(OpenError.AccessDenied)) || (!OperatingSystem.IsWindows() && texts.Contains(WriteStrings.ForOpenError(OpenError.Other))),
                "the card names the lock");
            Assert.Empty(window.Workspace.Tabs);
            Assert.True(window.IsVisible);
        }

        // Once the lock is gone the same file opens.
        window.Host.Close();
        WindowFixture.Pump();
        Assert.IsType<Opened>(await window.Flow.OpenAsync(path));
    }

    [AvaloniaFact]
    public async Task ATooLargeFileIsRefusedBeforeItIsRead()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var work = window.Work;
        var flow = new OpenFlow(window.Workspace, work, (_, _) => { }, null, maxFileLength: 1024);
        var path = fixture.Copy(WindowFixture.Poi, "big.docx");

        var result = await flow.OpenAsync(path);

        Assert.Equal(OpenError.TooLarge, Assert.IsType<Failed>(result).Error);
        Assert.Equal(0, work.FilesOpened);
        Assert.False(work.FontsCreated);
    }

    [AvaloniaFact]
    public async Task SeveralFailuresAreOneCardAndOneFailureIsOneCard()
    {
        using var fixture = new WindowFixture();
        var window = fixture.Show();
        var names = Enumerable.Range(0, 7).Select(i => Path.Combine(fixture.Folder, $"missing-{i}.docx")).ToList();

        await window.OpenFilesAsync(names);
        WindowFixture.Pump();

        var texts = WindowFixture.CardTexts(window);
        Assert.Contains("missing-0.docx", texts);
        Assert.Contains("missing-4.docx", texts);
        Assert.DoesNotContain("missing-5.docx", texts);
        Assert.Contains("and 2 more", texts);
        window.Host.ActiveDialog!.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_Ok").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        WindowFixture.Pump();
        Assert.False(window.Host.IsOpen);
    }

    [AvaloniaFact]
    public async Task OnlyFileNotFoundRemovesARecentEntry()
    {
        using var fixture = new WindowFixture();
        var missing = Path.Combine(fixture.Folder, "gone.docx");
        var notDocx = fixture.Write("notes.docx", Encoding.UTF8.GetBytes("plain text"));
        var good = fixture.Copy(WindowFixture.Poi, "good.docx");
        fixture.SeedRecent(missing, notDocx, good);
        var window = fixture.Show();
        await WindowFixture.WaitAsync(() => window.Start.Recent.Count == 3);

        // A file that is not a document stays: it may be a share that is back tomorrow, or a file the user will repair.
        var entry = window.Start.Recent.Single(r => r.Name == "notes.docx");
        Assert.Null(await window.OpenRecentAsync(entry));
        WindowFixture.Pump();
        window.Host.Close();
        WindowFixture.Pump();
        Assert.Equal(3, window.Recent.ForStartView(10).Count);

        // A file that is gone leaves the list.
        entry = window.Start.Recent.Single(r => r.Name == "gone.docx");
        Assert.Null(await window.OpenRecentAsync(entry));
        WindowFixture.Pump();
        Assert.Equal(["good.docx", "notes.docx"], window.Recent.ForStartView(10).Select(r => r.Name).Order(StringComparer.Ordinal).ToArray());

        // A good file opens and moves to the top.
        entry = window.Start.Recent.Single(r => r.Name == "good.docx");
        Assert.NotNull(await window.OpenRecentAsync(entry));
        Assert.Single(window.Workspace.Tabs);
    }
}
