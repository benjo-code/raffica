using System.Globalization;
using System.Text;
using Raffica.Write.Model;

namespace Raffica.Write.Layout;

public enum NumberSuffix
{
    Tab,
    Space,
    Nothing,
}

/// <summary>The label of a numbered paragraph: its text, the formatting of that text and what follows it.</summary>
public sealed record NumberingLabel(string Text, ResolvedRun Run, NumberSuffix Suffix);

/// <summary>
/// Counts list numbers in document order. Lists that share an abstract definition share their counters, as in Word; a
/// <c>w:startOverride</c> restarts a level the first time its list is used. Call <see cref="Next"/> once per paragraph, in
/// document order, and for every paragraph (a paragraph without a list gives null and changes nothing).
/// </summary>
public sealed class NumberingCounter
{
    private const int Levels = 9;

    private readonly NumberingDefinitions? _numbering;
    private readonly Dictionary<int, ListState> _states = [];
    private readonly HashSet<(int NumId, int Level)> _overridden = [];
    private readonly Dictionary<(int NumId, int Level), LevelFacts?> _facts = [];
    private int _version;
    private NumberingState? _cached;
    private int _cachedVersion = -1;

    public NumberingCounter(NumberingDefinitions? numbering) => _numbering = numbering;

    /// <summary>The label of a numbered paragraph, advancing the counters; null when the paragraph has no list, or the list or level does not exist.</summary>
    public NumberingLabel? Next(ResolvedParagraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        if (_numbering is null || paragraph.NumberingId <= 0 || paragraph.NumberingLevel is < 0 or >= Levels)
        {
            return null;
        }

        var numId = paragraph.NumberingId;
        var level = paragraph.NumberingLevel;
        if (_numbering.FindInstance(numId) is not { AbstractNumId: { } abstractId } instance || Facts(numId, level) is not { } facts)
        {
            return null;
        }

        // From here on the counters change: a snapshot made before this call is no longer current.
        _version++;
        if (!_states.TryGetValue(abstractId, out var state))
        {
            state = new ListState();
            _states[abstractId] = state;
        }

        if (_overridden.Add((numId, level)))
        {
            foreach (var over in instance.Overrides)
            {
                if (over.Level == level && over.StartOverride is { } start)
                {
                    state.Counters[level] = start - 1;
                    state.Started[level] = true;
                    ResetDeeper(numId, state, level);
                }
            }
        }

        var value = state.Started[level] ? state.Counters[level] + 1 : facts.Start;
        state.Counters[level] = value;
        state.Started[level] = true;
        ResetDeeper(numId, state, level);

        var label = Format(numId, state, level, value, facts);
        var run = paragraph.MarkRun.Clone();
        run.Overlay(facts.Level.RunProperties.Children);
        if (facts.Bullet && label.FromSymbolFont)
        {
            // The symbol fonts of Word are not shipped; the plain bullet is drawn in the paragraph's own font.
            run.Font = paragraph.MarkRun.Font;
        }

        return new NumberingLabel(label.Text, run.ToResolved(paragraphMarkOnly: true), facts.Suffix);
    }

    /// <summary>
    /// Everything that changes while <see cref="Next"/> runs: the counters and started flags of every list, and the set of levels whose
    /// start override was applied. (The facts cache is derived from the immutable definitions and is not state.)
    /// </summary>
    /// <remarks>
    /// While nothing changed since the last snapshot, the same object comes back (a block that planned without touching a list makes no copy), so
    /// <see cref="NumberingState.SameAs"/> is a reference test for it.
    /// </remarks>
    internal NumberingState Snapshot()
    {
        if (_cached is { } known && _cachedVersion == _version)
        {
            return known;
        }

        var lists = new Dictionary<int, (int[] Counters, bool[] Started)>(_states.Count);
        foreach (var (id, state) in _states)
        {
            lists[id] = ((int[])state.Counters.Clone(), (bool[])state.Started.Clone());
        }

        var overridden = _overridden.ToArray();
        Array.Sort(overridden);
        var snapshot = new NumberingState(lists, overridden);
        _cached = snapshot;
        _cachedVersion = _version;
        return snapshot;
    }

    /// <summary>Makes the counters exactly what they were when <paramref name="state"/> was taken; the state itself stays unchanged and can be restored again.</summary>
    internal void Restore(NumberingState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _states.Clear();
        foreach (var (id, list) in state.Lists)
        {
            var copy = new ListState();
            list.Counters.CopyTo(copy.Counters, 0);
            list.Started.CopyTo(copy.Started, 0);
            _states[id] = copy;
        }

        _overridden.Clear();
        _overridden.UnionWith(state.Overridden);

        // The state is immutable, so it is also the snapshot of what the counters hold now.
        _version++;
        _cached = state;
        _cachedVersion = _version;
    }

    private static string Letters(int value, bool upper)
    {
        var letter = (char)((upper ? 'A' : 'a') + ((value - 1) % 26));
        return new string(letter, ((value - 1) / 26) + 1);
    }

    private static string Roman(int value, bool upper)
    {
        if (value is <= 0 or >= 4000)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        int[] numbers = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
        string[] symbols = ["m", "cm", "d", "cd", "c", "xc", "l", "xl", "x", "ix", "v", "iv", "i"];
        var builder = new StringBuilder();
        for (var i = 0; i < numbers.Length; i++)
        {
            while (value >= numbers[i])
            {
                builder.Append(symbols[i]);
                value -= numbers[i];
            }
        }

        var text = builder.ToString();
        return upper ? text.ToUpperInvariant() : text;
    }

    private static string Number(string format, int value) => format switch
    {
        "lowerLetter" when value > 0 => Letters(value, upper: false),
        "upperLetter" when value > 0 => Letters(value, upper: true),
        "lowerRoman" => Roman(value, upper: false),
        "upperRoman" => Roman(value, upper: true),
        "decimalZero" when value is >= 0 and < 10 => "0" + value.ToString(CultureInfo.InvariantCulture),
        "none" => string.Empty,
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    private void ResetDeeper(int numId, ListState state, int level)
    {
        for (var deeper = level + 1; deeper < Levels; deeper++)
        {
            var restart = Facts(numId, deeper)?.Restart;
            // lvlRestart 0: never restart; n: restart after level n (1 based) or any level above it; none: after any higher level.
            if (restart is null || (restart > 0 && level <= restart - 1))
            {
                state.Started[deeper] = false;
            }
        }
    }

    private (string Text, bool FromSymbolFont) Format(int numId, ListState state, int level, int value, LevelFacts facts)
    {
        if (facts.Bullet)
        {
            return BulletText(facts.Text);
        }

        var text = facts.Text;
        var builder = new StringBuilder(text.Length + 4);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '%' && i + 1 < text.Length && text[i + 1] is >= '1' and <= '9')
            {
                var referenced = text[i + 1] - '1';
                i++;
                var number = referenced == level ? value : state.Started[referenced] ? state.Counters[referenced] : Facts(numId, referenced)?.Start ?? 1;
                var format = facts.Legal ? "decimal" : (referenced == level ? facts.Format : Facts(numId, referenced)?.Format ?? "decimal");
                builder.Append(Number(format, number));
            }
            else
            {
                builder.Append(text[i]);
            }
        }

        return (builder.ToString(), false);
    }

    private static (string Text, bool FromSymbolFont) BulletText(string text)
    {
        if (text.Length == 0)
        {
            return ("•", true);
        }

        var builder = new StringBuilder(text.Length);
        var symbol = false;
        foreach (var c in text)
        {
            // Word writes bullets as private-use code points of the Symbol, Wingdings and Courier New fonts.
            if (c is >= '' and <= '')
            {
                builder.Append(c == '' ? '▪' : '•');
                symbol = true;
            }
            else
            {
                builder.Append(c);
            }
        }

        return (builder.ToString(), symbol);
    }

    private LevelFacts? Facts(int numId, int level)
    {
        if (_numbering is null)
        {
            return null;
        }

        if (_facts.TryGetValue((numId, level), out var cached))
        {
            return cached;
        }

        LevelFacts? facts = null;
        if (StyleResolver.LookupLevel(_numbering, numId, level) is { } definition)
        {
            facts = new LevelFacts(definition);
        }

        _facts[(numId, level)] = facts;
        return facts;
    }

    private sealed class ListState
    {
        public int[] Counters { get; } = new int[Levels];

        public bool[] Started { get; } = new bool[Levels];
    }

    private sealed class LevelFacts
    {
        public LevelFacts(NumberingLevel level)
        {
            Level = level;
            Format = level.Format ?? "decimal";
            Text = level.Text ?? string.Empty;
            Start = level.Start ?? 1;
            Bullet = Format == "bullet";
            foreach (var kept in level.KeptChildren)
            {
                var element = kept.Content.Element;
                if (element.Name.Namespace != Ooxml.W)
                {
                    continue;
                }

                switch (element.Name.LocalName)
                {
                    case "lvlRestart":
                        Restart = Ooxml.Int(Ooxml.Val(element));
                        break;
                    case "isLgl":
                        Legal = Ooxml.OnOff(element);
                        break;
                    case "suff":
                        Suffix = Ooxml.Val(element) switch
                        {
                            "space" => NumberSuffix.Space,
                            "nothing" => NumberSuffix.Nothing,
                            _ => NumberSuffix.Tab,
                        };
                        break;
                }
            }
        }

        public NumberingLevel Level { get; }

        public string Format { get; }

        public string Text { get; }

        public int Start { get; }

        public bool Bullet { get; }

        public int? Restart { get; }

        public bool Legal { get; }

        public NumberSuffix Suffix { get; } = NumberSuffix.Tab;
    }
}

/// <summary>A copy of the counters of a <see cref="NumberingCounter"/> at one moment; never changes.</summary>
internal sealed class NumberingState
{
    internal NumberingState(Dictionary<int, (int[] Counters, bool[] Started)> lists, (int NumId, int Level)[] overridden)
    {
        Lists = lists;
        Overridden = overridden;
    }

    internal IReadOnlyDictionary<int, (int[] Counters, bool[] Started)> Lists { get; }

    /// <summary>The levels whose start override was applied, sorted.</summary>
    internal (int NumId, int Level)[] Overridden { get; }

    /// <summary>True when both hold the same counters, started flags and applied overrides.</summary>
    internal bool SameAs(NumberingState? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || Lists.Count != other.Lists.Count || !Overridden.AsSpan().SequenceEqual(other.Overridden))
        {
            return false;
        }

        foreach (var (id, list) in Lists)
        {
            if (!other.Lists.TryGetValue(id, out var them) || !list.Counters.AsSpan().SequenceEqual(them.Counters) || !list.Started.AsSpan().SequenceEqual(them.Started))
            {
                return false;
            }
        }

        return true;
    }
}
