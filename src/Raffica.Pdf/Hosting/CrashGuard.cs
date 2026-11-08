using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Raffica.Pdf.Hosting;

/// <summary>
/// The last net of the app: an exception nobody caught is written to <c>crash.log</c> (time, type, message and stack, never a path or the text of a
/// document), and one on the UI thread is answered with one card instead of the end of the process. More than three UI exceptions within ten seconds
/// mean the app is in a loop: it logs and ends through <c>exit</c>. An <see cref="OutOfMemoryException"/> is logged and left unhandled (nothing can be
/// promised after it). Nothing is made before the first exception: no file, no folder. The callbacks can run on any thread, so the guard never
/// reads the dispatcher itself; the owner hands in <c>showCard</c> and <c>exit</c> that post through a dispatcher it captured on the UI thread.
/// </summary>
internal sealed partial class CrashGuard
{
    /// <summary>The name of the log file in the config folder.</summary>
    public const string LogFileName = "crash.log";

    /// <summary>The cap of the log; when an entry would pass it the older half is dropped.</summary>
    public const long MaxLogBytes = 1024 * 1024;

    private const int MaxUiExceptions = 3;
    private const int InnerDepth = 5;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly string _logDirectory;
    private readonly TimeProvider _clock;
    private readonly Action<Exception> _showCard;
    private readonly Action _exit;
    private readonly object _gate = new();
    private readonly Queue<DateTimeOffset> _recent = new();
    private bool _exiting;

    public CrashGuard(string logDirectory, TimeProvider clock, Action<Exception> showCard, Action exit)
    {
        ArgumentException.ThrowIfNullOrEmpty(logDirectory);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(showCard);
        ArgumentNullException.ThrowIfNull(exit);
        _logDirectory = logDirectory;
        _clock = clock;
        _showCard = showCard;
        _exit = exit;
    }

    /// <summary>The log file this guard writes (it may not exist yet).</summary>
    public string LogPath => Path.Combine(_logDirectory, LogFileName);

    /// <summary>An exception of the UI thread: logged, then answered with a card (<paramref name="handled"/> true) or, in a loop, the end of the app.</summary>
    public void OnUiException(Exception ex, out bool handled)
    {
        ArgumentNullException.ThrowIfNull(ex);
        var now = _clock.GetUtcNow();
        bool loop;
        bool alreadyExiting;
        lock (_gate)
        {
            while (_recent.Count > 0 && now - _recent.Peek() > Window)
            {
                _recent.Dequeue();
            }

            _recent.Enqueue(now);
            loop = _recent.Count > MaxUiExceptions;
            alreadyExiting = _exiting;
            _exiting |= loop;
        }

        Write(ex, now);
        if (ex is OutOfMemoryException)
        {
            handled = false;
            return;
        }

        handled = true;
        if (alreadyExiting)
        {
            return;
        }

        try
        {
            if (loop)
            {
                _exit();
            }
            else
            {
                _showCard(ex);
            }
        }
        catch (Exception inner) when (inner is not OutOfMemoryException)
        {
            // The answer failed too; it is in the count, and a loop ends the app.
            Write(inner, now);
        }
    }

    /// <summary>An exception of another thread: logged only; the process may end as it did before.</summary>
    public void OnBackgroundException(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        Write(ex, _clock.GetUtcNow());
    }

    private void Write(Exception ex, DateTimeOffset now)
    {
        try
        {
            var entry = Format(ex, now);
            lock (_gate)
            {
                Directory.CreateDirectory(_logDirectory);
                var path = LogPath;
                Trim(path, Encoding.UTF8.GetByteCount(entry));
                File.AppendAllText(path, entry, new UTF8Encoding(false));
            }
        }
        catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            // A log that cannot be written must never become a second crash.
        }
    }

    // Keeps the newest entries when the file plus the new entry would pass the cap: the older half goes, cut at an entry start.
    private static void Trim(string path, long incoming)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length + incoming <= MaxLogBytes)
        {
            return;
        }

        var bytes = File.ReadAllBytes(path);
        var start = Math.Min(bytes.Length, (int)Math.Max(bytes.Length / 2, bytes.Length + incoming - MaxLogBytes));
        var text = Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
        var marker = text.IndexOf("\n=== ", StringComparison.Ordinal);
        File.WriteAllText(path, marker >= 0 ? text[(marker + 1)..] : string.Empty, new UTF8Encoding(false));
    }

    private static string Format(Exception ex, DateTimeOffset now)
    {
        var text = new StringBuilder();
        text.Append("=== ").Append(now.ToString("O", CultureInfo.InvariantCulture)).Append(" ===\n");
        Append(text, ex, 0);
        text.Append('\n');
        return text.ToString();
    }

    private static void Append(StringBuilder text, Exception ex, int depth)
    {
        text.Append(depth == 0 ? string.Empty : "--- inner: ").Append(ex.GetType().FullName).Append(": ").Append(Scrub(ex.Message)).Append('\n');
        if (ex.StackTrace is { } stack)
        {
            text.Append(Scrub(stack)).Append('\n');
        }

        if (depth + 1 >= InnerDepth)
        {
            return;
        }

        if (ex is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                Append(text, inner, depth + 1);
            }
        }
        else if (ex.InnerException is { } single)
        {
            Append(text, single, depth + 1);
        }
    }

    /// <summary>
    /// Replaces every absolute path in a text by <c>&lt;path&gt;</c>: a drive path, a UNC path and the common roots of a user's files. A path
    /// may contain spaces, so it runs to the next quote or the end of the line (a little text after it is lost, which is the safe side).
    /// </summary>
    internal static string Scrub(string text) => text.Length == 0 ? text : PathPattern().Replace(text, "<path>");

    [GeneratedRegex(@"(?:[A-Za-z]:[\\/]|\\\\|/(?:home|Users|root|Volumes|mnt|media|tmp|var|private|run)/)[^'""\r\n<>|]*", RegexOptions.CultureInvariant)]
    private static partial Regex PathPattern();
}
