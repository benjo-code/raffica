namespace Raffica.Write.Layout;

/// <summary>
/// Liang's hyphenation algorithm over the TeX patterns of hyph-utf8 (American and British English, German). Words of fewer than five
/// letters, words with anything but letters and any other language get no points. Safe to call from several threads.
/// </summary>
public sealed class Hyphenator : IHyphenator
{
    private const int MinLetters = 5;

    private readonly HyphenationData _data = new();

    internal Hyphenator()
    {
    }

    /// <summary>The shared instance; its pattern sets are read on first use.</summary>
    public static Hyphenator Default { get; } = new();

    /// <summary>How many pattern sets have been parsed by this instance.</summary>
    internal int PatternSetsLoaded => _data.Loaded;

    /// <inheritdoc/>
    public void Find(ReadOnlySpan<char> word, string? language, List<int> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);
        if (word.Length < MinLetters || HyphenationData.KeyFor(language) is null)
        {
            return;
        }

        foreach (var c in word)
        {
            if (!char.IsLetter(c))
            {
                return;
            }
        }

        var set = _data.Get(language);
        if (set is null)
        {
            return;
        }

        // ".word." in lower case; levels[j] is the strength of the break before dotted[j].
        var dotted = new char[word.Length + 2];
        dotted[0] = '.';
        for (var i = 0; i < word.Length; i++)
        {
            dotted[i + 1] = char.ToLowerInvariant(word[i]);
        }

        dotted[^1] = '.';
        var levels = new byte[dotted.Length + 1];
        for (var start = 0; start < dotted.Length; start++)
        {
            var longest = Math.Min(set.Longest, dotted.Length - start);
            for (var length = 1; length <= longest; length++)
            {
                if (set.TryGet(dotted.AsSpan(start, length), out var digits))
                {
                    for (var k = 0; k <= length; k++)
                    {
                        if (digits[k] > levels[start + k])
                        {
                            levels[start + k] = digits[k];
                        }
                    }
                }
            }
        }

        // A hyphen before word letter i is levels[i + 1]; odd means allowed.
        var from = set.LeftMin;
        var to = word.Length - set.RightMin;
        for (var i = from; i <= to; i++)
        {
            if ((levels[i + 1] & 1) == 1)
            {
                positions.Add(i);
            }
        }
    }
}
