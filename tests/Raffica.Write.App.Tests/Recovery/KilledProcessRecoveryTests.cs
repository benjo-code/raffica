using System.Diagnostics;
using System.Globalization;
using System.Text;
using Raffica.Write.Docx;
using Raffica.Write.Model;
using Raffica.Write.Recovery;
using Xunit;

namespace Raffica.WriteApp.Tests.Recovery;

/// <summary>
/// The acceptance test of milestone 4.3c: a child process (the harness) runs the real <see cref="AutosaveScheduler"/>, <see cref="RecoveryStore"/> and <c>DocxFile</c> while it types
/// known text through the editor, and is killed with <see cref="Process.Kill()"/>; this process then reads what the store holds, as the next start would, and compares the text
/// paragraph by paragraph. It never skips: a missing harness is a failure that says how to build it.
/// </summary>
public sealed class KilledProcessRecoveryTests
{
    private static readonly TimeSpan s_wait = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task RecoveryAfterAKilledProcessRestoresTheText()
    {
        const int paragraphs = 40;
        using var run = new HarnessRun(paragraphs);
        var last = await run.WaitForCopyAsync(copy => copy.Paragraphs == paragraphs);
        run.Kill();

        var entry = Assert.Single(run.Store.List().Entries);
        Assert.False(RecoveryLiveness.IsLive(entry, Environment.ProcessId, new SystemProcessProbe()), "the killed owner must not count as live");
        Assert.Equal(run.ProcessId, entry.OwnerProcessId);
        Assert.Equal(paragraphs, last.Paragraphs);
        Assert.Equal(Lines(paragraphs), TextOfCopy(run.Store, entry));
    }

    [Fact]
    public async Task AKillBetweenTwoCopiesLeavesAWholeCopy()
    {
        // Many paragraphs, so typing is still going when the process is killed.
        const int paragraphs = 600;
        using var run = new HarnessRun(paragraphs);
        var third = await run.WaitForCopyAsync(copy => copy.Index == 3);
        run.Kill();

        // A copy may have finished after the third line was read and before the kill; the last one printed is a floor, never a ceiling.
        var printed = run.Copies.Max(copy => copy.Paragraphs);
        Assert.True(printed >= third.Paragraphs);
        var entry = Assert.Single(run.Store.List().Entries);
        Assert.False(RecoveryLiveness.IsLive(entry, Environment.ProcessId, new SystemProcessProbe()));
        var text = TextOfCopy(run.Store, entry);
        Assert.True(text.Count >= printed, $"the copy holds {text.Count} paragraphs, the last printed copy had {printed}");
        Assert.True(text.Count <= paragraphs);
        Assert.Equal(Lines(text.Count), text);
    }

    private static List<string> Lines(int count) => [.. Enumerable.Range(1, count).Select(i => "line " + i.ToString(CultureInfo.InvariantCulture))];

    /// <summary>Opens the copy as the restore does and returns the text of every body paragraph.</summary>
    private static List<string> TextOfCopy(RecoveryStore store, RecoveryEntry entry)
    {
        using var docx = DocxFile.Open(File.ReadAllBytes(store.DocxPathOf(entry.Id)));
        return [.. docx.Model.Body.Blocks.OfType<Paragraph>().Select(ParagraphText)];
    }

    private static string ParagraphText(Paragraph paragraph) =>
        string.Concat(paragraph.Inlines.OfType<Run>().SelectMany(run => run.Content).OfType<TextContent>().Select(text => text.Text));

    private sealed record Copy(int Index, int Revision, int Paragraphs);

    private sealed class HarnessRun : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("raffica-killed-").FullName;
        private readonly Process _process;
        private readonly object _gate = new();
        private readonly List<Copy> _copies = [];
        private readonly StringBuilder _output = new();
        private readonly StringBuilder _errors = new();
        private bool _killed;

        public HarnessRun(int paragraphs)
        {
            try
            {
                var exe = RepoPaths.RecoveryHarness;
                if (!File.Exists(exe))
                {
                    throw new InvalidOperationException($"The recovery harness is not built ({exe}). Run `dotnet build Raffica.slnx -c Release` first.");
                }

                Store = new RecoveryStore(Path.Combine(_root, "recovery"));
                _process = Start(exe, paragraphs);
            }
            catch
            {
                // A constructor that throws is never disposed: the temp folder goes here.
                DeleteRoot();
                throw;
            }
        }

        private Process Start(string exe, int paragraphs)
        {
            var start = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("--folder");
            start.ArgumentList.Add(Store.Folder);
            start.ArgumentList.Add("--interval-ms");
            start.ArgumentList.Add("40");
            start.ArgumentList.Add("--paragraphs");
            start.ArgumentList.Add(paragraphs.ToString(CultureInfo.InvariantCulture));
            var process = new Process { StartInfo = start };
            process.OutputDataReceived += (_, e) => OnLine(e.Data);
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    lock (_gate)
                    {
                        _errors.AppendLine(e.Data);
                    }
                }
            };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }

        public RecoveryStore Store { get; } = null!;

        public int ProcessId => _process.Id;

        public IReadOnlyList<Copy> Copies
        {
            get
            {
                lock (_gate)
                {
                    return [.. _copies];
                }
            }
        }

        public async Task<Copy> WaitForCopyAsync(Func<Copy, bool> wanted)
        {
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < s_wait)
            {
                lock (_gate)
                {
                    if (_copies.FirstOrDefault(wanted) is { } found)
                    {
                        return found;
                    }
                }

                if (_process.HasExited)
                {
                    break;
                }

                await Task.Delay(5);
            }

            throw new TimeoutException($"No wanted COPY line within {s_wait.TotalSeconds:0} s (exited: {_process.HasExited}).\nstdout:\n{Snapshot(_output)}\nstderr:\n{Snapshot(_errors)}");
        }

        /// <summary>Kills the whole process and waits until it is gone (the files it held are free then).</summary>
        public void Kill()
        {
            _killed = true;
            _process.Kill(entireProcessTree: true);
            if (!_process.WaitForExit(s_wait))
            {
                throw new TimeoutException("The harness did not end after Kill.");
            }

            // The pipes close with the process; this wait lets the readers finish, so Copies is complete (bounded: a grandchild that holds a pipe open must not hang the test).
            if (!Task.Run(() => _process.WaitForExit()).Wait(s_wait))
            {
                throw new TimeoutException($"The output of the harness did not end within {s_wait.TotalSeconds:0} s after Kill.\nstdout:\n{Snapshot(_output)}\nstderr:\n{Snapshot(_errors)}");
            }
        }

        public void Dispose()
        {
            try
            {
                if (!_killed && !_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    _process.WaitForExit(s_wait);
                }
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }

            _process.Dispose();
            DeleteRoot();
        }

        private void DeleteRoot()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A temp folder left behind is not a failure of the test.
            }
        }

        private string Snapshot(StringBuilder builder)
        {
            lock (_gate)
            {
                return builder.ToString();
            }
        }

        private void OnLine(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (_gate)
            {
                _output.AppendLine(line);
                var parts = line.Split(' ');
                if (parts is ["COPY", var revision, var count]
                    && int.TryParse(revision, NumberStyles.None, CultureInfo.InvariantCulture, out var r)
                    && int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out var c))
                {
                    _copies.Add(new Copy(_copies.Count + 1, r, c));
                }
            }
        }
    }
}
