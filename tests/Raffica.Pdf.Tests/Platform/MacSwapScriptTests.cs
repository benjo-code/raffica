using System.Diagnostics;
using Raffica.Pdf.Platform.MacOS;
using Xunit;

namespace Raffica.Pdf.Tests.Platform;

public sealed class MacSwapScriptTests : IDisposable
{
    private const string TrueTool = "/usr/bin/true";
    private const string FalseTool = "/usr/bin/false";

    private readonly string _top = Path.Combine(Path.GetTempPath(), "raffica-swap-" + Guid.NewGuid().ToString("N"));
    private readonly string _root;
    private readonly string _openerLog;
    private readonly string _opener;

    public MacSwapScriptTests()
    {
        _root = Path.Combine(_top, "a b'c");
        Directory.CreateDirectory(_root);
        _openerLog = Path.Combine(_top, "opened.log");
        _opener = Path.Combine(_top, "opener.sh");
        if (!OperatingSystem.IsWindows())
        {
            // The recorder writes one argument per line; the log lies outside the folder that holds the quoted names.
            File.WriteAllText(_opener, "#!/bin/sh\nfor a in \"$@\"; do printf '%s\\n' \"$a\" >> \"" + _openerLog + "\"; done\n");
            File.SetUnixFileMode(_opener, (UnixFileMode)Convert.ToInt32("755", 8));
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_top, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void TheNewBundleReplacesTheOldAndIsOpenedWithTheRestoreFlag()
    {
        SkipOnWindows();
        var (old, fresh, backup) = Bundles();

        var code = Run(old, fresh, backup, TrueTool, restore: true);

        Assert.Equal(0, code);
        Assert.Equal("new", File.ReadAllText(Path.Combine(old, "Contents", "id.txt")));
        Assert.False(Directory.Exists(backup));
        Assert.False(Directory.Exists(fresh));
        Assert.Equal([old, "--args", "--restore-session"], File.ReadAllLines(_openerLog));
    }

    [Fact]
    public void NoRestoreFlagWhenNoSessionWasWritten()
    {
        SkipOnWindows();
        var (old, fresh, backup) = Bundles();

        var code = Run(old, fresh, backup, TrueTool, restore: false);

        Assert.Equal(0, code);
        Assert.Equal([old], File.ReadAllLines(_openerLog));
    }

    [Fact]
    public void AFailedVerifyKeepsTheOldBundleRemovesTheNewAndReopensTheOld()
    {
        SkipOnWindows();
        var (old, fresh, backup) = Bundles();

        var code = Run(old, fresh, backup, FalseTool, restore: true);

        Assert.Equal(1, code);
        Assert.Equal("old", File.ReadAllText(Path.Combine(old, "Contents", "id.txt")));
        Assert.False(Directory.Exists(fresh));
        Assert.False(Directory.Exists(backup));
        Assert.Equal([old], File.ReadAllLines(_openerLog));
    }

    [Fact]
    public void AFailedMoveRestoresTheBackup()
    {
        SkipOnWindows();
        var (old, fresh, backup) = Bundles();
        Directory.Delete(fresh, recursive: true); // the verifier is a stub, so the second move is what fails

        var code = Run(old, fresh, backup, TrueTool, restore: true);

        Assert.Equal(1, code);
        Assert.Equal("old", File.ReadAllText(Path.Combine(old, "Contents", "id.txt")));
        Assert.False(Directory.Exists(backup));
        Assert.Equal([old], File.ReadAllLines(_openerLog));
    }

    [Fact]
    public void AFailedFirstMoveReopensTheOldBundle()
    {
        SkipOnWindows();
        var (old, fresh, _) = Bundles();
        var backup = Path.Combine(_root, "missing folder", "backup.app");

        var code = Run(old, fresh, backup, TrueTool, restore: true);

        Assert.Equal(1, code);
        Assert.Equal("old", File.ReadAllText(Path.Combine(old, "Contents", "id.txt")));
        Assert.Equal([old], File.ReadAllLines(_openerLog));
    }

    [Fact]
    public void TheScriptWaitsForTheProcessToEnd()
    {
        SkipOnWindows();
        var (old, fresh, backup) = Bundles();
        using var sleeper = Process.Start(new ProcessStartInfo("sleep", "2") { UseShellExecute = false })!;

        var watch = Stopwatch.StartNew();
        var code = Run(old, fresh, backup, TrueTool, restore: false, processId: sleeper.Id);
        watch.Stop();

        Assert.Equal(0, code);
        Assert.True(sleeper.HasExited);
        Assert.True(watch.Elapsed >= TimeSpan.FromSeconds(1), $"The script did not wait ({watch.Elapsed}).");
        Assert.Equal("new", File.ReadAllText(Path.Combine(old, "Contents", "id.txt")));
    }

    [Fact]
    public void PathsWithSpacesAndQuotesAreArgumentsNotCode()
    {
        SkipOnWindows();
        var marker = Path.Combine(_root, "injected");
        var old = Path.Combine(_root, "Old $(touch injected) \"x\" a b'c.app");
        var fresh = Path.Combine(_root, "New `touch injected`; touch injected.app");
        var backup = Path.Combine(_root, "Backup a b'c.app");
        Make(old, "old");
        Make(fresh, "new");

        var code = Run(old, fresh, backup, TrueTool, restore: true);

        Assert.Equal(0, code);
        Assert.False(File.Exists(marker));
        Assert.Equal("new", File.ReadAllText(Path.Combine(old, "Contents", "id.txt")));
    }

    [Fact]
    public void TheArgumentsCarryEveryValueAsItsOwnParameter()
    {
        var args = MacSwapScript.Arguments(42, "/o ld", "/n'ew", "/b", "/usr/bin/open", "/usr/bin/codesign", restoreSession: true);

        Assert.Equal(
            ["-c", MacSwapScript.Text, "sh", "42", "/o ld", "/n'ew", "/b", "/usr/bin/open", "/usr/bin/codesign", "1"],
            args);
        Assert.DoesNotContain("/o ld", MacSwapScript.Text, StringComparison.Ordinal);
    }

    private static void SkipOnWindows() =>
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows: needs /bin/sh");

    private static void Make(string bundle, string id)
    {
        Directory.CreateDirectory(Path.Combine(bundle, "Contents"));
        File.WriteAllText(Path.Combine(bundle, "Contents", "id.txt"), id);
    }

    private (string Old, string New, string Backup) Bundles()
    {
        var old = Path.Combine(_root, "Raffica PDF.app");
        var fresh = Path.Combine(_root, "Raffica PDF new.app");
        Make(old, "old");
        Make(fresh, "new");
        return (old, fresh, Path.Combine(_root, "Raffica PDF backup.app"));
    }

    private int Run(string old, string fresh, string backup, string verifier, bool restore, int? processId = null)
    {
        // The working folder is the test folder, so a relative "touch injected" in a name would leave its marker there.
        var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, WorkingDirectory = _root };

        // A process id that does not exist ends the wait at once.
        foreach (var argument in MacSwapScript.Arguments(processId ?? 2_000_000_000, old, fresh, backup, _opener, verifier, restore))
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        Assert.True(process.WaitForExit(60_000), "The swap script did not end.");
        return process.ExitCode;
    }
}
