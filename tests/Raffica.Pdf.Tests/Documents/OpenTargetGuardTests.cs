using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Raffica.Core.Pdf;
using Raffica.Core.Saving;
using Raffica.Core.Viewing;
using Raffica.Pdf.Dialogs;
using Raffica.Pdf.Documents;
using Raffica.Pdf.Tests.Annotating;
using Raffica.Pdf.Tests.Organizing;
using Raffica.Pdf.Tests.Printing;
using Raffica.Pdf.Tests.Redacting;
using Raffica.Pdf.Tests.Text;
using Raffica.Pdf.Tests.Viewing;
using Raffica.Shell.Controls;
using Raffica.TestSupport;
using Xunit;

namespace Raffica.Pdf.Tests.Documents;

/// <summary>
/// The guard that refuses a write onto a file another tab has open (the unit tests, on temp copies), and one test per flow that writes to a picked path:
/// Save as, Apply with Save as, Redact Apply with Save as and Extract each end in the note and write nothing.
/// </summary>
public sealed class OpenTargetGuardTests : IDisposable
{
    private readonly List<ViewerSession> _sessions = [];
    private readonly List<TempCopy> _copies = [];
    private readonly PdfEngine _engine = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Workspace NewWorkspace() => new(new CloseGuard(_ => Task.FromResult(CloseChoice.DontSave), _ => Task.FromResult(true)));

    public void Dispose()
    {
        foreach (var session in _sessions)
        {
            session.Dispose();
        }

        foreach (var copy in _copies)
        {
            copy.Dispose();
        }
    }

    private TempCopy Copy(string file = "hello_world.pdf")
    {
        var copy = new TempCopy(file);
        _copies.Add(copy);
        return copy;
    }

    private async Task<PdfTab> AddAsync(Workspace workspace, string path)
    {
        var session = new ViewerSession(_engine);
        _sessions.Add(session);
        await session.OpenAsync(path, null);
        return workspace.Add(session, path);
    }

    private static string NoteText(Dialog card) => PrintHost.Find<TextBlock>(card, "PART_Message").Text!;

    private static void Click(Dialog card, string name) =>
        PrintHost.Find<Button>(card, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public async Task ASpellingOfAnotherTabsPathIsFound()
    {
        var workspace = NewWorkspace();
        var self = await AddAsync(workspace, Copy().Path);
        var other = await AddAsync(workspace, Copy().Path);

        Assert.Same(other, await OpenTargetGuard.FindOtherTabAsync(workspace, self, other.Path!, Ct));

        // A ".." segment is another spelling of the same path.
        var directory = Path.GetDirectoryName(other.Path)!;
        var spelled = Path.Combine(directory, "..", Path.GetFileName(directory), Path.GetFileName(other.Path)!);
        Assert.Same(other, await OpenTargetGuard.FindOtherTabAsync(workspace, self, spelled, Ct));
    }

    [Fact]
    public async Task TheCaseIsIgnoredOnWindowsAndMacOsOnly()
    {
        var workspace = NewWorkspace();
        var self = await AddAsync(workspace, Copy().Path);
        var other = await AddAsync(workspace, Copy().Path);
        var upper = other.Path!.ToUpperInvariant();

        var found = await OpenTargetGuard.FindOtherTabAsync(workspace, self, upper, Ct, (_, _) => false);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            Assert.Same(other, found);
        }
        else
        {
            Assert.Null(found);
        }
    }

    [Fact]
    public async Task AFileThatNoTabHasOpenIsFree()
    {
        var workspace = NewWorkspace();
        var self = await AddAsync(workspace, Copy().Path);
        await AddAsync(workspace, Copy().Path);
        var free = Path.Combine(Copy().Folder, "not-open.pdf");
        File.Copy(TestData.Pdf("hello_world.pdf"), free);

        Assert.Null(await OpenTargetGuard.FindOtherTabAsync(workspace, self, free, Ct));
        Assert.Null(await OpenTargetGuard.FindOtherTabAsync(workspace, self, Path.Combine(Copy().Folder, "missing.pdf"), Ct));
    }

    [Fact]
    public async Task TheTabItselfIsNeverTheOtherTab()
    {
        var workspace = NewWorkspace();
        var self = await AddAsync(workspace, Copy().Path);

        Assert.Null(await OpenTargetGuard.FindOtherTabAsync(workspace, self, self.Path!, Ct));
    }

    [Fact]
    public async Task ASymbolicLinkToAnOpenFileIsFound()
    {
        var workspace = NewWorkspace();
        var self = await AddAsync(workspace, Copy().Path);
        var other = await AddAsync(workspace, Copy().Path);
        var link = Path.Combine(Copy().Folder, "link.pdf");
        try
        {
            File.CreateSymbolicLink(link, other.Path!);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            // Creating a link needs a privilege on Windows (developer mode or an elevated process): nothing to check here.
            return;
        }

        Assert.Same(other, await OpenTargetGuard.FindOtherTabAsync(workspace, self, link, Ct));
    }

    [Fact]
    public async Task AHardLinkToAnOpenFileIsFound()
    {
        var workspace = NewWorkspace();
        var self = await AddAsync(workspace, Copy().Path);
        var other = await AddAsync(workspace, Copy().Path);
        var link = Path.Combine(Copy().Folder, "hard.pdf");
        if (!TryHardLink(link, other.Path!))
        {
            return;
        }

        Assert.Same(other, await OpenTargetGuard.FindOtherTabAsync(workspace, self, link, Ct));
    }

    [Fact]
    public async Task ANullAnswerOfTheFileSystemCountsAsTheSameFile()
    {
        var workspace = NewWorkspace();
        var self = await AddAsync(workspace, Copy().Path);
        var other = await AddAsync(workspace, Copy().Path);
        var elsewhere = Path.Combine(Copy().Folder, "elsewhere.pdf");

        Assert.Same(other, await OpenTargetGuard.FindOtherTabAsync(workspace, self, elsewhere, Ct, (_, _) => null));
        Assert.Null(await OpenTargetGuard.FindOtherTabAsync(workspace, self, elsewhere, Ct, (_, _) => false));
        Assert.Same(other, await OpenTargetGuard.FindOtherTabAsync(workspace, self, elsewhere, Ct, (_, _) => true));
    }

    [AvaloniaFact]
    public async Task TheFileSystemQuestionDoesNotRunOnTheUiThread()
    {
        var workspace = NewWorkspace();
        var self = await AddAsync(workspace, Copy().Path);
        await AddAsync(workspace, Copy().Path);
        Assert.True(Avalonia.Threading.Dispatcher.UIThread.CheckAccess());
        var asked = new List<bool>();

        await OpenTargetGuard.FindOtherTabAsync(workspace, self, Path.Combine(Copy().Folder, "elsewhere.pdf"), Ct, (_, _) =>
        {
            asked.Add(Avalonia.Threading.Dispatcher.UIThread.CheckAccess());
            return false;
        });

        Assert.NotEmpty(asked);
        Assert.DoesNotContain(true, asked);
    }

    private static bool TryHardLink(string link, string existing)
    {
        try
        {
            var info = OperatingSystem.IsWindows()
                ? new ProcessStartInfo("cmd.exe", $"/c mklink /H \"{link}\" \"{existing}\"")
                : new ProcessStartInfo("ln", $"\"{existing}\" \"{link}\"");
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            using var process = Process.Start(info);
            process?.WaitForExit();
            return process?.ExitCode == 0 && File.Exists(link);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    [AvaloniaFact]
    public async Task SaveAsOntoAnotherOpenTabWritesNothingAndSaysSo()
    {
        using var a = new TempCopy("text-rich.pdf");
        using var b = new TempCopy("hello_world.pdf");
        var bBytes = await File.ReadAllBytesAsync(b.Path, Ct);
        var aBytes = await File.ReadAllBytesAsync(a.Path, Ct);
        using var windows = WindowFixture.Create();
        var tabA = await windows.Window.Flow.OpenAsync(a.Path) ?? throw new InvalidOperationException("did not open");
        ViewFixture.Pump();
        Assert.NotNull(await AnnotatedFile.HighlightAsync(tabA.Annotations));
        await tabA.Annotations.FlushAsync();
        Assert.NotNull(await windows.Window.Flow.OpenAsync(b.Path));
        var calls = 0;
        var controller = new SaveController(
            new DialogQueue(windows.Window.Host),
            _ => Task.FromResult<string?>(b.Path),
            () => new PdfEngine(),
            runner: (_, _, _, _, _) =>
            {
                calls++;
                throw new InvalidOperationException("must not write");
            },
            workspace: windows.Window.Workspace);

        var save = controller.SaveAsAsync(tabA);
        await PrintHost.UntilAsync(() => windows.Window.Host.ActiveDialog is not null);
        var card = windows.Window.Host.ActiveDialog!;
        Assert.Equal("hello_world.pdf is open in another tab. Close it first.", NoteText(card));
        Click(card, "PART_Ok");

        Assert.False(await save.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal(0, calls);
        Assert.Equal(bBytes, await File.ReadAllBytesAsync(b.Path, Ct));
        Assert.Equal(aBytes, await File.ReadAllBytesAsync(a.Path, Ct));
        Assert.Equal(a.Path, tabA.Path);
        Assert.False(controller.IsSaving);
    }

    [Fact]
    public async Task ANullAnswerOnAnUnsupportedOsMeansAnotherFile()
    {
        var workspace = NewWorkspace();
        var self = await AddAsync(workspace, Copy().Path);
        var other = await AddAsync(workspace, Copy().Path);
        var elsewhere = Path.Combine(Copy().Folder, "elsewhere.pdf");

        // No identity call exists there: null is the answer for every pair, so only the spelling decides.
        Assert.Null(await OpenTargetGuard.FindOtherTabAsync(workspace, self, elsewhere, Ct, (_, _) => null, identitySupported: false));
        Assert.Same(other, await OpenTargetGuard.FindOtherTabAsync(workspace, self, other.Path!, Ct, (_, _) => null, identitySupported: false));

        // A definite answer is used on every OS.
        Assert.Same(other, await OpenTargetGuard.FindOtherTabAsync(workspace, self, elsewhere, Ct, (_, _) => true, identitySupported: false));
        Assert.Same(other, await OpenTargetGuard.FindOtherTabAsync(workspace, self, elsewhere, Ct, (_, _) => null, identitySupported: true));
    }

    [AvaloniaFact]
    public async Task SaveAsOntoAFreePathStillWorksWithAnotherTabOpen()
    {
        using var a = new TempCopy("text-rich.pdf");
        using var b = new TempCopy("hello_world.pdf");
        using var windows = WindowFixture.Create();
        var tabA = await windows.Window.Flow.OpenAsync(a.Path) ?? throw new InvalidOperationException("did not open");
        ViewFixture.Pump();
        Assert.NotNull(await AnnotatedFile.HighlightAsync(tabA.Annotations));
        await tabA.Annotations.FlushAsync();
        Assert.NotNull(await windows.Window.Flow.OpenAsync(b.Path));
        var free = Path.Combine(a.Folder, "free-copy.pdf");
        var engine = new PdfEngine();
        var controller = new SaveController(
            new DialogQueue(windows.Window.Host),
            _ => Task.FromResult<string?>(free),
            () => engine,
            workspace: windows.Window.Workspace);

        Assert.True(await controller.SaveAsAsync(tabA).WaitAsync(TimeSpan.FromSeconds(60), Ct));

        Assert.True(File.Exists(free));
        Assert.Equal(free, tabA.Path);
        Assert.Null(windows.Window.Host.ActiveDialog);
    }

    [AvaloniaFact]
    public async Task ATabThatOpensTheTargetWhileTheSignedCardIsUpIsRefused()
    {
        using var a = new TempCopy("signed-docmdp-1.pdf");
        using var b = new TempCopy("hello_world.pdf");
        var bBytes = await File.ReadAllBytesAsync(b.Path, Ct);
        var time = new ManualTime();
        using var windows = WindowFixture.Create(time: time);
        var tabA = await windows.Window.Flow.OpenAsync(a.Path) ?? throw new InvalidOperationException("did not open");
        var calls = 0;
        var controller = new SaveController(
            new DialogQueue(windows.Window.Host),
            _ => Task.FromResult<string?>(b.Path),
            () => new PdfEngine(),
            time: time,
            runner: (_, _, _, _, _) =>
            {
                calls++;
                throw new InvalidOperationException("must not write");
            },
            workspace: windows.Window.Workspace);

        // b.pdf is not open when the picker returns; the signed-file card is up.
        var save = controller.SaveAsAsync(tabA);
        await PrintHost.UntilAsync(() => windows.Window.Host.ActiveDialog is { } d && d.GetVisualDescendants().OfType<Button>().Any(x => x.Name == "PART_SaveAnyway"));
        var signed = windows.Window.Host.ActiveDialog!;

        // While the card is up, a tab opens on the target.
        Assert.NotNull(await windows.Window.Flow.OpenAsync(b.Path));
        time.Advance(500);
        Click(signed, "PART_SaveAnyway");

        await PrintHost.UntilAsync(() => windows.Window.Host.ActiveDialog is { } d && d != signed && d.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Name == "PART_Message"));
        var note = windows.Window.Host.ActiveDialog!;
        Assert.Equal("hello_world.pdf is open in another tab. Close it first.", NoteText(note));
        Click(note, "PART_Ok");
        Assert.False(await save.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal(0, calls);
        Assert.Equal(bBytes, await File.ReadAllBytesAsync(b.Path, Ct));
    }

    [AvaloniaFact]
    public async Task SavingWhileASaveOfTheSameTabRunsSaysItIsBusy()
    {
        using var a = new TempCopy("text-rich.pdf");
        using var windows = WindowFixture.Create();
        var tab = await windows.Window.Flow.OpenAsync(a.Path) ?? throw new InvalidOperationException("did not open");
        ViewFixture.Pump();
        Assert.NotNull(await AnnotatedFile.HighlightAsync(tab.Annotations));
        await tab.Annotations.FlushAsync();
        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var controller = new SaveController(
            new DialogQueue(windows.Window.Host),
            _ => Task.FromResult<string?>(null),
            () => new PdfEngine(),
            runner: async (_, _, _, _, token) =>
            {
                started.TrySetResult();
                await gate.Task.WaitAsync(token);
                throw new OperationCanceledException();
            });

        var first = controller.SaveAsync(tab);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        Assert.True(controller.IsSaving);

        // The close question asks Save while the save runs: it says so instead of returning silently.
        var second = controller.SaveAsync(tab);
        await PrintHost.UntilAsync(() => windows.Window.Host.ActiveDialog is { } d && PrintHost.Find<TextBlock>(d, "PART_Message").Text == PdfStrings.SaveBusy);
        Assert.False(await second.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Click(windows.Window.Host.ActiveDialog!, "PART_Ok");

        gate.SetResult();
        Assert.False(await first.WaitAsync(TimeSpan.FromSeconds(30), Ct));
    }

    [AvaloniaFact]
    public async Task ApplyWithSaveAsOntoAnotherOpenTabWritesNothingAndSaysSo()
    {
        using var rig = await ApplyRig.OpenAsync();
        var other = rig.CopyOf("hello_world.pdf");
        await rig.AddOtherAsync(other);
        var otherBytes = await File.ReadAllBytesAsync(other, Ct);
        var original = await File.ReadAllBytesAsync(rig.Path, Ct);
        rig.Session.Selection.Set(6, [0]);
        Assert.True(rig.Session.Delete().IsDone);
        var version = rig.Session.Version;
        rig.Picks.Enqueue(other);

        var run = rig.Flow.RunAsync(rig.Tab);
        var apply = await rig.CardWithAsync("PART_SaveAs");
        rig.Time.Advance(ArmingLock.DelayMilliseconds + 100);
        ApplyRig.Click(apply, "PART_SaveAs");
        var note = await rig.CardWithAsync("PART_Ok");

        Assert.Equal("hello_world.pdf is open in another tab. Close it first.", NoteText(note));
        ApplyRig.Click(note, "PART_Ok");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal(otherBytes, await File.ReadAllBytesAsync(other, Ct));
        Assert.Equal(original, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.Equal(version, rig.Session.Version);
        Assert.True(rig.Session.IsActive);
        Assert.Empty(rig.Rebuilds);
    }

    [AvaloniaFact]
    public async Task RedactApplyWithSaveAsOntoAnotherOpenTabWritesNothingAndSaysSo()
    {
        using var rig = await RedactApplyRig.OpenAsync("redact-everywhere-nopattern.pdf");
        await rig.MarkTermAsync("Zephyrstraße 17");
        rig.MarkArea(0, new Raffica.Core.Text.PageRect(248, 40, 290, 62));
        var other = rig.CopyOf("hello_world.pdf");
        await rig.AddOtherAsync(other);
        var otherBytes = await File.ReadAllBytesAsync(other, Ct);
        var original = await File.ReadAllBytesAsync(rig.Path, Ct);
        var version = rig.Session.Version;
        rig.Picks.Enqueue(other);

        var run = rig.Flow.RunAsync(rig.Tab);
        var apply = await rig.CardWithAsync("PART_SaveAs");
        rig.Time.Advance(ArmingLock.DelayMilliseconds + 100);
        RedactApplyRig.Click(apply, "PART_SaveAs");
        var note = await rig.CardWithAsync("PART_Ok");

        Assert.Equal("hello_world.pdf is open in another tab. Close it first.", NoteText(note));
        RedactApplyRig.Click(note, "PART_Ok");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal(otherBytes, await File.ReadAllBytesAsync(other, Ct));
        Assert.Equal(original, await File.ReadAllBytesAsync(rig.Path, Ct));
        Assert.Equal(version, rig.Session.Version);
        Assert.Empty(rig.Rebuilds);
    }

    [AvaloniaFact]
    public async Task ExtractOntoAnotherOpenTabWritesNothingAndSaysSo()
    {
        using var rig = await SplitRig.OpenAsync();
        var folder = rig.NewFolder();
        var other = Path.Combine(folder, "hello_world.pdf");
        File.Copy(TestData.Pdf("hello_world.pdf"), other);
        await rig.AddOtherAsync(other);
        var otherBytes = await File.ReadAllBytesAsync(other, Ct);
        rig.SavePicks.Enqueue(other);

        var run = rig.Flow.ExtractAsync(rig.Tab);
        var card = await rig.CardWithAsync("PART_Range");
        PrintHost.Find<TextBox>(card, "PART_Range").Text = "1";
        SplitRig.Click(card, "PART_Extract");

        // The file has bookmarks and labels: the loss card comes first.
        await rig.PressAsync("PART_Go");
        var note = await rig.CardWithAsync("PART_Ok");

        Assert.Equal("hello_world.pdf is open in another tab. Close it first.", NoteText(note));
        SplitRig.Click(note, "PART_Ok");
        Assert.False(await run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Equal(otherBytes, await File.ReadAllBytesAsync(other, Ct));
        Assert.Empty(rig.Opened);
    }

    [Fact]
    public void TheNoteIsGermanInTheGermanUi()
    {
        PdfStrings.Culture = System.Globalization.CultureInfo.GetCultureInfo("de");
        try
        {
            Assert.Equal("a.pdf ist in einem anderen Tab geöffnet. Schließen Sie ihn zuerst.", OpenTargetGuard.NoteFor(Path.Combine("x", "a.pdf")));
        }
        finally
        {
            TestCulture.Reset();
        }
    }
}
