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

    /// <summary>The names of the phases in the order of their numbers (the words of the measurement line).</summary>
    internal static readonly string[] Names = ["lease", "operation", "selection", "layout", "changed", "selection-event"];

    private readonly long[] _ticks = new long[Names.Length];

    internal void Add(int phase, long start) => _ticks[phase] += Stopwatch.GetTimestamp() - start;

    /// <summary>The milliseconds of a phase since the last <see cref="Reset"/>.</summary>
    internal double Milliseconds(int phase) => Stopwatch.GetElapsedTime(0, _ticks[phase]).TotalMilliseconds;

    internal void Reset() => Array.Clear(_ticks);
}
