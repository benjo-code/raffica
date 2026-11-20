using System.Diagnostics;

namespace Raffica.Write.Editing;

/// <summary>
/// Where the time of one edit goes inside <see cref="EditSession.Apply"/>, for the measurement runs only: a session has none unless the measurement sets one, and then each phase adds the
/// ticks it took (a null check and nothing else in a normal run). Recorded and never judged; it holds no text.
/// </summary>
internal sealed class EditPhases
{
    internal const int Lease = 0;
    internal const int Operation = 1;
    internal const int Selection = 2;
    internal const int Layout = 3;
    internal const int Changed = 4;
    internal const int SelectionEvent = 5;

    // Parts of Changed (the view's and the tab's handlers), inside it: they are not added to the top-level phases when the rest of the handler is worked out.
    internal const int ViewCurrent = 6;
    internal const int ViewScrollBars = 7;
    internal const int ViewBlink = 8;
    internal const int ViewCaret = 9;
    internal const int ViewInvalidate = 10;
    internal const int ViewPublish = 11;
    internal const int ViewNotify = 12;
    internal const int ViewIme = 13;
    internal const int TabDirty = 14;

    /// <summary>How many of the first phases are top-level (the rest are parts of <see cref="Changed"/>).</summary>
    internal const int TopLevel = 6;

    /// <summary>The names of the phases in the order of their numbers (the words of the measurement line).</summary>
    internal static readonly string[] Names = ["lease", "operation", "selection", "layout", "changed", "selection-event", "v-current", "v-scrollbars", "v-blink", "v-caret", "v-invalidate", "v-publish", "v-notify", "v-ime", "tab-dirty"];

    private readonly long[] _ticks = new long[Names.Length];

    /// <summary>The start stamp of a lap (0 without phases).</summary>
    internal static long Now(EditPhases? phases) => phases is null ? 0 : Stopwatch.GetTimestamp();

    /// <summary>Adds the time since <paramref name="stamp"/> to a phase and starts the next lap (nothing without phases).</summary>
    internal static void Lap(EditPhases? phases, int phase, ref long stamp)
    {
        if (phases is null)
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        phases._ticks[phase] += now - stamp;
        stamp = now;
    }

    internal void Add(int phase, long start) => _ticks[phase] += Stopwatch.GetTimestamp() - start;

    /// <summary>The milliseconds of a phase since the last <see cref="Reset"/>.</summary>
    internal double Milliseconds(int phase) => Stopwatch.GetElapsedTime(0, _ticks[phase]).TotalMilliseconds;

    internal void Reset() => Array.Clear(_ticks);
}
