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
    private const int MaxTextChars = 8 * 1024;
    private const int MaxEntryChars = 64 * 1024;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly string _logDirectory;
    private readonly TimeProvider _clock;
    private readonly Action<Exception> _showCard;
    private readonly Action _exit;
    private readonly object _gate = new();
    private readonly Queue<long> _recent = new();
    private bool _exiting;
    private bool _cardShowing;

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

    /// <summary>
    /// An exception of the UI thread: logged, then answered with one card (<paramref name="handled"/> true) or, in a loop, the end of the app.
    /// While a card shows (until <see cref="CardClosed"/>) repeats are only logged, and no card follows the decision to end the app.
    /// </summary>
    public void OnUiException(Exception ex, out bool handled)
    {
        ArgumentNullException.ThrowIfNull(ex);
        var wall = _clock.GetUtcNow();
        var stamp = _clock.GetTimestamp();
        bool endNow;
        bool showCard;
        lock (_gate)
        {
            // The ten seconds run on the monotonic clock: a change of the wall clock must neither hide a loop nor invent one.
            while (_recent.Count > 0 && _clock.GetElapsedTime(_recent.Peek(), stamp) > Window)
            {
                _recent.Dequeue();
            }

            _recent.Enqueue(stamp);
            var loop = _recent.Count > MaxUiExceptions;
            endNow = loop && !_exiting;
            showCard = !loop && !_exiting && !_cardShowing && ex is not OutOfMemoryException;
            _exiting |= loop;
            _cardShowing |= showCard;
        }

        Write(ex, wall);
        if (ex is OutOfMemoryException)
        {
            handled = false;
            return;
        }

        handled = true;
        try
        {
            if (endNow)
            {
                _exit();
            }
            else if (showCard)
            {
                _showCard(ex);
            }
        }
        catch (Exception inner) when (inner is not OutOfMemoryException)
        {
            // The answer failed too: the next exception may try a card again (a loop still ends the app).
            CardClosed();
            Write(inner, wall);
        }
    }

    /// <summary>Tells the guard that the crash card is gone (dismissed, or it could not be shown), so the next exception may show one again.</summary>
    public void CardClosed()
    {
        lock (_gate)
        {
            _cardShowing = false;
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
        if (text.Length > MaxEntryChars)
        {
            text.Length = MaxEntryChars;
            text.Append("\n[truncated]");
        }

        text.Append('\n');
        return text.ToString();
    }

    private static void Append(StringBuilder text, Exception ex, int depth)
    {
        text.Append(depth == 0 ? string.Empty : "--- inner: ").Append(ex.GetType().FullName).Append(": ").Append(Cut(Scrub(ex.Message))).Append('\n');
        if (ex.StackTrace is { } stack)
        {
            text.Append(Cut(Scrub(stack))).Append('\n');
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

    // One message or stack never passes 8 KiB, so no entry can fill the log.
    private static string Cut(string text) => text.Length <= MaxTextChars ? text : text[..MaxTextChars] + " [truncated]";

    /// <summary>
    /// Replaces every absolute path in a text by <c>&lt;path&gt;</c>: a drive path, a UNC path and the common roots of a user's files. A path
    /// may contain spaces, so it runs to the next double quote or the end of the line (a little text after it is lost, which is the safe side).
    /// </summary>
    internal static string Scrub(string text) => text.Length == 0 ? text : PathPattern().Replace(text, "<path>");

    // A path may hold spaces and apostrophes (O'Brien), so it runs to the next double quote or the end of the line. A drive letter needs a boundary and no
    // second slash after the colon (http://host/x and avares://app/x are addresses, not paths); relative starts (~/, ../, ..\) and the usual roots count.
    [GeneratedRegex(@"(?:(?<![A-Za-z0-9])[A-Za-z]:(?:\\|/(?!/))|\\\\|(?<![A-Za-z0-9.~])~/|(?<![A-Za-z0-9])\.\.[\\/]|/(?:home|Users|root|opt|srv|data|mnt|media|tmp|var|Volumes|private|run)/)[^""\r\n]*", RegexOptions.CultureInvariant)]
    private static partial Regex PathPattern();
}
